using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Prices;

public sealed record PriceFile(int Season, string? Newest, Dictionary<int, Price> Rows);

public sealed record Synced(PriceFile? File, int? Season, string Source, string? Error, string Databases = "", IReadOnlyDictionary<PriceSource, IReadOnlyDictionary<int, Price>>? Tables = null);

public static class PriceSync
{
    public const string Base = "https://qhjulyngunwiculnharg.supabase.co";
    public const string Key = "sb_publishable_YgqYSMUarrM_IKvcNpJlBw_KwTpp7ho";
    const int PageSize = 1000;
    internal const int MaxPages = 100;
    static readonly TimeSpan Recent = TimeSpan.FromDays(7);

    public static string PricesUrl(int season, string? since, int offset) =>
        $"{Base}/rest/v1/aggregated_prices?season_id=eq.{season}&select=config_base_id,price_fe_median,price_fe_p10,price_fe_p90,submission_count,unique_devices,updated_at&order=config_base_id&limit={PageSize}&offset={offset}"
        + (since is null ? "" : $"&updated_at=gt.{since.Replace("+", "%2B")}");

    public static (Dictionary<int, Price> Rows, string? Newest) Parse(string json)
    {
        var (rows, newest, _) = ParsePage(json);
        return (rows, newest);
    }

    static (Dictionary<int, Price> Rows, string? Newest, int Count) ParsePage(string json)
    {
        using var document = JsonDocument.Parse(json);
        var rows = new Dictionary<int, Price>();
        string? newest = null;
        var count = 0;
        foreach (var row in document.RootElement.EnumerateArray())
        {
            count++;
            newest = Later(newest, row.GetProperty("updated_at").GetString());
            if (row.GetProperty("price_fe_median").ValueKind != JsonValueKind.Number) continue;
            rows[row.GetProperty("config_base_id").GetInt32()] = new Price(row.GetProperty("price_fe_median").GetSingle(), Optional(row, "price_fe_p10"), Optional(row, "price_fe_p90"), Stamp(row.GetProperty("updated_at").GetString()), OptionalInt(row, "submission_count"), OptionalInt(row, "unique_devices"));
        }
        return (rows, newest, count);
    }

    public static PriceFile Merge(PriceFile file, Dictionary<int, Price> rows, string? newest)
    {
        var merged = new Dictionary<int, Price>(file.Rows);
        foreach (var (id, price) in rows) merged[id] = price;
        return file with { Newest = Later(file.Newest, newest), Rows = merged };
    }

    public static int? LatestSeason(string seasonsJson, DateTimeOffset now)
    {
        using var document = JsonDocument.Parse(seasonsJson);
        int? latest = null;
        foreach (var season in document.RootElement.EnumerateArray())
        {
            var id = season.GetProperty("season_id").GetInt32();
            if (id > (latest ?? -1) && Stamp(season.GetProperty("latest_update").GetString()) >= now - Recent) latest = id;
        }
        return latest;
    }

    public static async Task<PriceFile> Refresh(HttpClient http, PriceFile? cached, int season, bool full = false)
    {
        var file = cached ?? new PriceFile(season, null, []);
        var fetched = new Dictionary<int, Price>();
        string? newest = null;
        for (var page = 0; page < MaxPages; page++)
        {
            var (rows, pageNewest, count) = ParsePage(await Send(http, new HttpRequestMessage(HttpMethod.Get, PricesUrl(season, full ? null : file.Newest, page * PageSize))));
            foreach (var (id, price) in rows) fetched[id] = price;
            newest = Later(newest, pageNewest);
            if (count < PageSize) break;
        }
        return full ? new PriceFile(season, newest, fetched) : Merge(file, fetched, newest);
    }

    public static Synced Sync(HttpClient http, string directory, int setting, int? log, bool full = false)
    {
        try
        {
            return SyncCore(http, directory, setting, log, full);
        }
        catch (Exception ex)
        {
            return new Synced(null, null, "error", ex.Message);
        }
    }

    public static int? CachedSeason(string directory, int setting, int? log) => setting > 0 ? setting : log ?? NewestCachedSeason(directory);

    public static string? CachedPath(string directory, int setting, int? log) =>
        CachedSeason(directory, setting, log) is { } season ? Path.Combine(directory, $"prices-{season}.json") : null;

    internal static (int? Season, string Source, string? Error) ResolveSeason(HttpClient http, string directory, int setting, int? log)
    {
        if (setting > 0) return (setting, "settings", null);
        if (log is { } fromLog) return (fromLog, "game log", null);
        int? season;
        string? error = null;
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Post, Base + "/rest/v1/rpc/get_available_seasons") { Content = new StringContent("{}", Encoding.UTF8, "application/json") };
            season = LatestSeason(Send(http, request).GetAwaiter().GetResult(), DateTimeOffset.UtcNow);
            if (season is null) error = "no season with recent prices";
        }
        catch (Exception ex)
        {
            season = null;
            error = ex.Message;
        }
        return season is null ? (NewestCachedSeason(directory), "cached", error) : (season, "server", error);
    }

    static Synced SyncCore(HttpClient http, string directory, int setting, int? log, bool full)
    {
        var (season, source, error) = ResolveSeason(http, directory, setting, log);
        if (season is not { } id) return new Synced(null, null, source, error);
        var path = Path.Combine(directory, $"prices-{id}.json");
        var cached = Load(path);
        PriceFile file;
        try
        {
            file = Refresh(http, cached, id, full).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            return new Synced(cached, id, source, error ?? ex.Message);
        }
        try
        {
            if (cached is not null || file.Rows.Count > 0) Save(path, file);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // the table is still good, only the next run pays a full download
        }
        return new Synced(file, id, source, error);
    }

    public static PriceFile? Load(string path)
    {
        try
        {
            return JsonSerializer.Deserialize<PriceFile>(File.ReadAllText(path)) is { Rows: not null } file ? file : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    public static void Save(string path, PriceFile file)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(file));
        File.Move(temp, path, true);
    }

    internal static async Task<string> Send(HttpClient http, HttpRequestMessage request)
    {
        request.Headers.Add("apikey", Key);
        using var response = await http.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync();
    }

    static int? NewestCachedSeason(string directory)
    {
        try
        {
            return new DirectoryInfo(directory).GetFiles("prices-*.json")
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .Select(file => int.TryParse(Path.GetFileNameWithoutExtension(file.Name)[7..], out var id) ? id : (int?)null)
                .FirstOrDefault(id => id is not null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    static float? Optional(JsonElement row, string name) =>
        row.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetSingle() : null;

    static int? OptionalInt(JsonElement row, string name) =>
        row.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetInt32() : null;

    static DateTimeOffset Stamp(string? stamp) => DateTimeOffset.Parse(stamp!, CultureInfo.InvariantCulture);

    static string? Later(string? a, string? b) => a is null ? b : b is null ? a : Stamp(b) > Stamp(a) ? b : a;
}
