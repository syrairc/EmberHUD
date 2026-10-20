using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Prices;

public sealed record ShareContent(int Season, IReadOnlyList<LocalRow>? LocalRows, IReadOnlyDictionary<int, string>? Affixes, IReadOnlyDictionary<int, Price>? Titrack, IReadOnlyDictionary<int, Price>? Etor);

public static class Share
{
    public const int Format = 1;
    public const long MaxBytes = 256L << 20;
    public const int MaxRows = 2_000_000;

    sealed class FileDto
    {
        [JsonPropertyName("format")] public int Format { get; set; }
        [JsonPropertyName("season")] public int Season { get; set; }
        [JsonPropertyName("exported")] public DateTimeOffset Exported { get; set; }
        [JsonPropertyName("databases")] public DatabasesDto? Databases { get; set; }
    }

    sealed class DatabasesDto
    {
        [JsonPropertyName("local")] public LocalDto? Local { get; set; }
        [JsonPropertyName("titrack")] public Dictionary<string, PriceDto?>? Titrack { get; set; }
        [JsonPropertyName("etor")] public Dictionary<string, PriceDto?>? Etor { get; set; }
    }

    sealed class LocalDto
    {
        [JsonPropertyName("affixes")] public Dictionary<string, string>? Affixes { get; set; }
        [JsonPropertyName("rows")] public List<LocalRow?>? Rows { get; set; }
    }

    sealed class PriceDto
    {
        [JsonPropertyName("median")] public float Median { get; set; }
        [JsonPropertyName("low")] public float? Low { get; set; }
        [JsonPropertyName("high")] public float? High { get; set; }
        [JsonPropertyName("updated")] public DateTimeOffset? Updated { get; set; }
        [JsonPropertyName("samples")] public int? Samples { get; set; }
        [JsonPropertyName("devices")] public int? Devices { get; set; }
    }

    public static string FileName(int season, DateTimeOffset now) => $"ember-prices-{season}-{now.UtcDateTime:yyyyMMdd-HHmm}.json.gz";

    static Dictionary<string, PriceDto?>? Dto(IReadOnlyDictionary<int, Price>? table) =>
        table?.OrderBy(pair => pair.Key).ToDictionary(pair => pair.Key.ToString(), pair => (PriceDto?)new PriceDto { Median = pair.Value.Median, Low = pair.Value.Low, High = pair.Value.High, Updated = pair.Value.Updated, Samples = pair.Value.Submissions, Devices = pair.Value.Devices });

    static Dictionary<int, Price>? Prices(Dictionary<string, PriceDto?>? table)
    {
        if (table is null) return null;
        var prices = new Dictionary<int, Price>();
        foreach (var (key, dto) in table)
            if (int.TryParse(key, out var id) && dto is { } d && float.IsFinite(d.Median) && d.Median > 0) prices[id] = new Price(d.Median, d.Low, d.High, d.Updated, d.Samples, d.Devices);
        return prices;
    }

    public static void Write(string path, ShareContent content, DateTimeOffset now)
    {
        var file = new FileDto
        {
            Format = Format,
            Season = content.Season,
            Exported = now.ToUniversalTime(),
            Databases = new DatabasesDto
            {
                Local = content.LocalRows is null ? null : new LocalDto { Affixes = (content.Affixes ?? new Dictionary<int, string>()).OrderBy(pair => pair.Key).ToDictionary(pair => pair.Key.ToString(), pair => pair.Value), Rows = [.. content.LocalRows] },
                Titrack = Dto(content.Titrack),
                Etor = Dto(content.Etor),
            },
        };
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        using (var stream = File.Create(temp))
        {
            using var zip = new GZipStream(stream, CompressionLevel.Optimal);
            JsonSerializer.Serialize(zip, file, LocalBook.Options);
        }
        File.Move(temp, path, true);
    }

    // the whole file is parsed and checked before anything is applied
    public static (ShareContent? Content, string? Error) Read(string path)
    {
        try
        {
            if (!File.Exists(path)) return (null, "File not found");
            if (new FileInfo(path).Length > MaxBytes) return (null, "File is too big");
            using var stream = File.OpenRead(path);
            var magic = new byte[2];
            var gzip = stream.Read(magic, 0, 2) == 2 && magic[0] == 0x1F && magic[1] == 0x8B;
            stream.Position = 0;
            using var zip = gzip ? new GZipStream(stream, CompressionMode.Decompress) : null;
            using var text = new MemoryStream();
            var buffer = new byte[81920];
            for (int read; (read = (zip ?? (Stream)stream).Read(buffer, 0, buffer.Length)) > 0;)
            {
                if (text.Length + read > MaxBytes) return (null, "File is too big");
                text.Write(buffer, 0, read);
            }
            text.Position = 0;
            var file = JsonSerializer.Deserialize<FileDto>(text, LocalBook.Options);
            if (file is null || file.Databases is null) return (null, "Not an Ember price file");
            if (file.Format != Format) return (null, $"Unsupported file format {file.Format}");
            if (file.Season <= 0) return (null, "The file has no season");
            if (file.Databases.Local?.Rows is { Count: > MaxRows }) return (null, "Too many listings in the file");
            return (Content(file), null);
        }
        catch (Exception ex)
        {
            return (null, "Could not read the file: " + ex.Message);
        }
    }

    static ShareContent Content(FileDto file)
    {
        var databases = file.Databases!;
        var affixes = new Dictionary<int, string>();
        foreach (var (key, text) in databases.Local?.Affixes ?? [])
            if (int.TryParse(key, out var id) && text is not null) affixes[id] = text;
        return new ShareContent(file.Season, databases.Local is null ? null : [.. (databases.Local.Rows ?? []).OfType<LocalRow>()], databases.Local is null ? null : affixes, Prices(databases.Titrack), Prices(databases.Etor));
    }

    // the price tables go into the persistent imported-* files, the downloaded tables are never touched
    public static (int Titrack, int Etor) ImportTables(string directory, ShareContent content)
    {
        int Import(PriceSource source, IReadOnlyDictionary<int, Price>? incoming)
        {
            if (incoming is null) return 0;
            var path = PriceSources.ImportedPath(directory, source, content.Season);
            var (rows, changed) = PriceSources.Newest(PriceSync.Load(path)?.Rows ?? [], incoming);
            if (changed > 0) PriceSync.Save(path, new PriceFile(content.Season, null, rows));
            return changed;
        }
        return (Import(PriceSource.Titrack, content.Titrack), Import(PriceSource.Etor, content.Etor));
    }
}
