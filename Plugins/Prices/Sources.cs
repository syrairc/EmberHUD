using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace Prices;

public enum PriceSource { Titrack, Etor, Local }

// ETor's price server sends one protobuf message: PriceResponse { items = 2 } with PriceItem { id = 1 string, price = 3 float }
public static class EtorSync
{
    internal static readonly string[] Hosts = ["https://etor-zero.981001.xyz/etor-api", "https://etor-beta.710421059.xyz/etor-api"];

    public static Dictionary<int, Price> Parse(byte[] data)
    {
        var rows = new Dictionary<int, Price>();
        try
        {
            var at = 0;
            while (at < data.Length)
            {
                var (field, bytes) = Field(data, ref at, data.Length);
                if (field != 2 || bytes is not { } item) continue;
                string? id = null;
                string? updated = null;
                var price = 0f;
                var position = item.Start;
                while (position < item.End)
                {
                    var (inner, value) = Field(data, ref position, item.End);
                    if (value is not { } slice) continue;
                    if (inner == 1) id = System.Text.Encoding.UTF8.GetString(data, slice.Start, slice.End - slice.Start);
                    else if (inner == 4) updated = System.Text.Encoding.UTF8.GetString(data, slice.Start, slice.End - slice.Start);
                    else if (inner == 3 && slice.End - slice.Start == 4) price = BitConverter.ToSingle(data, slice.Start);
                }
                if (int.TryParse(id, out var baseId) && price > 0) rows[baseId] = new Price(price, null, null, DateTimeOffset.TryParse(updated, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal, out var stamp) ? stamp : null);
            }
        }
        catch (Exception ex) when (ex is IndexOutOfRangeException or ArgumentException)
        {
            throw new FormatException("not a price snapshot", ex);
        }
        return rows;
    }

    static (int Field, (int Start, int End)? Bytes) Field(byte[] data, ref int at, int end)
    {
        var tag = Varint(data, ref at, end);
        var field = (int)(tag >> 3);
        switch (tag & 7)
        {
            case 0:
                Varint(data, ref at, end);
                return (field, null);
            case 1:
                return (field, Take(ref at, 8, end));
            case 2:
                return (field, Take(ref at, (int)Varint(data, ref at, end), end));
            case 5:
                return (field, Take(ref at, 4, end));
            default:
                throw new FormatException("not a price snapshot");
        }
    }

    static (int, int) Take(ref int at, int length, int end)
    {
        if (length < 0 || at + length > end) throw new FormatException("not a price snapshot");
        var start = at;
        at += length;
        return (start, at);
    }

    static ulong Varint(byte[] data, ref int at, int end)
    {
        ulong value = 0;
        for (var shift = 0; shift < 64; shift += 7)
        {
            if (at >= end) throw new FormatException("not a price snapshot");
            var b = data[at++];
            value |= (ulong)(b & 0x7F) << shift;
            if (b < 0x80) return value;
        }
        throw new FormatException("not a price snapshot");
    }

    public static async Task<Dictionary<int, Price>> Fetch(HttpClient http, int season)
    {
        Exception? last = null;
        foreach (var host in Hosts)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, $"{host}/api/prices-snapshot/{season}?format=protobuf");
                request.Headers.Accept.ParseAdd("application/x-protobuf");
                using var response = await http.SendAsync(request);
                response.EnsureSuccessStatusCode();
                var rows = Parse(await response.Content.ReadAsByteArrayAsync());
                if (rows.Count > 0) return rows;
                last = new FormatException("empty price snapshot");
            }
            catch (Exception ex) when (ex is HttpRequestException or FormatException or TaskCanceledException)
            {
                last = ex;
            }
        }
        throw last!;
    }

    internal static string CachePath(string directory, int season) => Path.Combine(directory, $"etor-{season}.json");
}

public static class PriceSources
{
    static string Name(PriceSource source) => source switch { PriceSource.Titrack => "TITrack", PriceSource.Etor => "ETor", _ => "Local" };

    // a price with a known sample count under minSamples is skipped, so a lower database can still price the item - unknown counts (ETor) always pass
    // a known Updated older than notBefore is skipped the same way - unknown Updated always passes
    public static Dictionary<int, Price> Combine(IEnumerable<IReadOnlyDictionary<int, Price>> inPriorityOrder, int minSamples = 0, DateTimeOffset? notBefore = null)
    {
        var merged = new Dictionary<int, Price>();
        foreach (var table in inPriorityOrder)
            foreach (var (id, price) in table)
                if (!(price.Submissions < minSamples) && !(price.Updated < notBefore)) merged.TryAdd(id, price);
        return merged;
    }

    internal static string ImportedPath(string directory, PriceSource source, int season) => Path.Combine(directory, $"imported-{(source == PriceSource.Titrack ? "titrack" : "etor")}-{season}.json");

    // the newer Updated wins per base id, a missing Updated loses to any date, equal keeps ours
    public static (Dictionary<int, Price> Rows, int Changed) Newest(IReadOnlyDictionary<int, Price> ours, IReadOnlyDictionary<int, Price> theirs)
    {
        var merged = new Dictionary<int, Price>(ours);
        var changed = 0;
        foreach (var (id, price) in theirs)
        {
            if (merged.TryGetValue(id, out var mine) && !(price.Updated is { } at && (mine.Updated is not { } was || at > was))) continue;
            merged[id] = price;
            changed++;
        }
        return (merged, changed);
    }

    // a downloaded table with the user's imported prices on top, the import alone when nothing was downloaded
    internal static IReadOnlyDictionary<int, Price>? WithImported(string directory, PriceSource source, int season, IReadOnlyDictionary<int, Price>? downloaded)
    {
        var imported = PriceSync.Load(ImportedPath(directory, source, season))?.Rows;
        if (imported is null || imported.Count == 0) return downloaded;
        return Newest(downloaded ?? new Dictionary<int, Price>(), imported).Rows;
    }

    public static Synced Sync(HttpClient http, string directory, PriceSource[] active, int setting, int? log, bool full)
    {
        var tables = new Dictionary<PriceSource, IReadOnlyDictionary<int, Price>>();
        Synced? titrack = null;
        int? season;
        string source;
        string? error;
        if (active.Contains(PriceSource.Titrack))
        {
            titrack = PriceSync.Sync(http, directory, setting, log, full);
            (season, source, error) = (titrack.Season, titrack.Source, titrack.Error);
        }
        else (season, source, error) = PriceSync.ResolveSeason(http, directory, setting, log);
        if (season is not { } id) return new Synced(titrack?.File, null, source, error);
        if (titrack is not null && WithImported(directory, PriceSource.Titrack, id, titrack.File?.Rows) is { } titrackRows) tables[PriceSource.Titrack] = titrackRows;
        if (active.Contains(PriceSource.Etor))
        {
            var path = EtorSync.CachePath(directory, id);
            try
            {
                var rows = EtorSync.Fetch(http, id).GetAwaiter().GetResult();
                tables[PriceSource.Etor] = WithImported(directory, PriceSource.Etor, id, rows)!;
                TrySave(path, new PriceFile(id, null, rows));
            }
            catch (Exception ex) when (ex is HttpRequestException or FormatException or TaskCanceledException)
            {
                error = error is null ? "ETor: " + ex.Message : error + "; ETor: " + ex.Message;
                if (WithImported(directory, PriceSource.Etor, id, PriceSync.Load(path)?.Rows) is { } etorRows) tables[PriceSource.Etor] = etorRows;
            }
        }
        // Local is not downloaded, Main fills it from the trade house
        var used = active.Where(tables.ContainsKey).ToArray();
        if (used.Length == 0) return new Synced(null, id, source, error);
        var merged = Combine(used.Select(s => tables[s]));
        return new Synced(new PriceFile(id, titrack?.File?.Newest, merged), id, source, error, string.Join(", ", used.Select(Name)), tables);
    }

    public static (Synced Synced, DateTime At)? Cached(string directory, PriceSource[] active, int setting, int? log)
    {
        if (PriceSync.CachedSeason(directory, setting, log) is not { } season) return null;
        var tables = new Dictionary<PriceSource, IReadOnlyDictionary<int, Price>>();
        var names = new List<string>();
        var at = DateTime.MinValue;
        foreach (var source in active)
        {
            var path = source switch { PriceSource.Titrack => Path.Combine(directory, $"prices-{season}.json"), PriceSource.Etor => EtorSync.CachePath(directory, season), _ => null };
            if (path is null || WithImported(directory, source, season, PriceSync.Load(path)?.Rows) is not { } rows) continue;
            tables[source] = rows;
            names.Add(Name(source));
            foreach (var file in new[] { path, ImportedPath(directory, source, season) })
                if (File.Exists(file) && File.GetLastWriteTime(file) is var written && written > at) at = written;
        }
        if (tables.Count == 0) return null;
        return (new Synced(new PriceFile(season, null, Combine(tables.Values)), season, "cached", null, string.Join(", ", names), tables), at);
    }

    static void TrySave(string path, PriceFile file)
    {
        try
        {
            PriceSync.Save(path, file);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // the table is still good, only the next offline start has nothing to fall back on
        }
    }
}
