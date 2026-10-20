using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using EmberHUD;

namespace Prices;

[JsonConverter(typeof(RowAffixConverter))]
public readonly record struct RowAffix(TradeAffixKind Kind, int Id, int Tier);

// [kind, id, tier] - tiers are kept (0 on a legendary, they have none), the rolled values are not, the tier range is in the shared text
public sealed class RowAffixConverter : JsonConverter<RowAffix>
{
    public override RowAffix Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartArray) throw new JsonException();
        reader.Read();
        var kind = reader.GetInt32();
        reader.Read();
        var id = reader.GetInt32();
        reader.Read();
        var tier = reader.GetInt32();
        reader.Read();
        if (reader.TokenType != JsonTokenType.EndArray) throw new JsonException();
        return new RowAffix((TradeAffixKind)kind, id, tier);
    }

    public override void Write(Utf8JsonWriter writer, RowAffix value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        writer.WriteNumberValue((int)value.Kind);
        writer.WriteNumberValue(value.Id);
        writer.WriteNumberValue(value.Tier);
        writer.WriteEndArray();
    }
}

public sealed class LocalRow
{
    [JsonPropertyName("g")] public long GoodId { get; set; }
    [JsonPropertyName("b")] public int BaseId { get; set; }
    [JsonPropertyName("sp")] public int SpecialId { get; set; }
    [JsonPropertyName("r")] public int Rare { get; set; }
    [JsonPropertyName("l")] public int Level { get; set; }
    [JsonPropertyName("il")] public int ItemLevel { get; set; }
    [JsonPropertyName("q")] public int Quality { get; set; }
    [JsonPropertyName("t")] public int Type { get; set; }
    [JsonPropertyName("c")] public bool Commodity { get; set; }
    [JsonPropertyName("p")] public long Price { get; set; }
    [JsonPropertyName("n")] public int Quantity { get; set; }
    [JsonPropertyName("f")] public long FirstSeen { get; set; }
    [JsonPropertyName("s")] public long LastSeen { get; set; }
    [JsonPropertyName("x")] public bool Sold { get; set; }
    [JsonPropertyName("a")]
    public RowAffix[]? Affixes
    {
        get => _affixes;
        set => (_affixes, _tierKey) = (value, null);
    }

    [JsonPropertyName("fl")] public string[]? Flags { get; set; }
    [JsonPropertyName("sk")] public int SkillId { get; set; }
    [JsonPropertyName("st")] public int SkillType { get; set; }
    [JsonPropertyName("h")] public int HoleNum { get; set; }
    [JsonPropertyName("e")] public int EnhanceCount { get; set; }
    [JsonPropertyName("fo")] public bool Forged { get; set; }
    [JsonPropertyName("co")] public bool Corrode { get; set; }
    [JsonPropertyName("ui")] public bool Unidentified { get; set; }
    [JsonIgnore] public long WrittenSeen { get; set; }

    // how many affixes sit at each tier, e.g. "A:2x5,3x1;B:2x1" (A = prefixes and suffixes, B = base attributes), empty when nothing has a tier (legendaries)
    [JsonIgnore] public string TierKey => _tierKey ??= TierKeyOf(Affixes);

    RowAffix[]? _affixes;
    string? _tierKey;

    static string TierKeyOf(RowAffix[]? affixes)
    {
        static string Part(IEnumerable<RowAffix> list) => string.Join(',', list.Where(affix => affix.Tier > 0).GroupBy(affix => affix.Tier).OrderBy(group => group.Key).Select(group => $"{group.Key}x{group.Count()}"));
        var rolled = Part((affixes ?? []).Where(affix => affix.Kind is TradeAffixKind.Prefix or TradeAffixKind.Suffix));
        var bases = Part((affixes ?? []).Where(affix => affix.Kind == TradeAffixKind.BaseAttribute));
        return rolled.Length == 0 && bases.Length == 0 ? "" : $"A:{rolled};B:{bases}";
    }

    [JsonIgnore] public ulong VariantKey => LegendaryPrices.Key(SpecialId, (Affixes ?? []).Where(affix => affix.Kind is TradeAffixKind.Prefix or TradeAffixKind.Suffix).Select(affix => affix.Id));

    // legendaries have no tiers: the same special id with a different set of modifiers (or corroded) is its own variant
    [JsonIgnore] public string Variant => SpecialId == 0 ? "" : $"{SpecialId}:{string.Join(',', (Affixes ?? []).Select(affix => affix.Id).Order())}{(Corrode ? ":c" : "")}{(Unidentified ? ":u" : "")}";
}

public sealed class LocalBook
{
    public static readonly TimeSpan Recent = TimeSpan.FromHours(24);
    const int Fe = 100300;
    const int WriteSeenEvery = 60;

    sealed record AffixLine([property: JsonPropertyName("i")] int Id, [property: JsonPropertyName("t")] string Text);

    sealed record Update([property: JsonPropertyName("g")] long GoodId, [property: JsonPropertyName("s")] long LastSeen, [property: JsonPropertyName("x")] bool Sold, [property: JsonPropertyName("p")] long Price);

    sealed class Line
    {
        [JsonPropertyName("a")] public AffixLine? Affix { get; set; }
        [JsonPropertyName("r")] public LocalRow? Row { get; set; }
        [JsonPropertyName("u")] public Update? Update { get; set; }
    }

    internal static readonly JsonSerializerOptions Options = new() { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault };
    static readonly Regex Markup = new("<[^>]*>");
    static readonly Regex Roll = new(@"<p>(\+?)(-?[\d.]+)(\([^)]*\))?</p>");

    // rolls are not kept, so the shared text shows # where the roll was and keeps the tier range
    static string Template(string text) => Markup.Replace(Roll.Replace(text, match => $"{match.Groups[1].Value}#{match.Groups[3].Value}"), "").Trim();

    readonly Dictionary<long, LocalRow> _rows = [];
    readonly Dictionary<int, string> _affixes = [];
    readonly HashSet<long> _newRows = [];
    readonly HashSet<long> _updatedRows = [];
    readonly HashSet<int> _newAffixes = [];
    int _fileUpdates;
    bool _compact;
    int? _baseCount;
    ILookup<string, LocalRow>? _tiers;

    public int Version { get; private set; }

    public int Count => _rows.Count;

    public int BaseCount => _baseCount ??= _rows.Values.Select(row => row.BaseId).Distinct().Count();

    public bool Dirty => _compact || _newRows.Count > 0 || _updatedRows.Count > 0 || _newAffixes.Count > 0;

    internal IReadOnlyDictionary<int, string> AffixTexts => _affixes;

    internal IEnumerable<LocalRow> Rows => _rows.Values;

    public IEnumerable<LocalRow> WithTierKey(string key) => (_tiers ??= _rows.Values.ToLookup(row => row.TierKey))[key];

    public bool Add(IEnumerable<TradeGood> goods, long nowUnix)
    {
        var changed = false;
        foreach (var good in goods)
        {
            if (good.PriceType != Fe) continue;
            var sold = good.Status == "Sold";
            if (!_rows.TryGetValue(good.GoodId, out var row))
            {
                _rows[good.GoodId] = Create(good, sold, nowUnix);
                _newRows.Add(good.GoodId);
                (_baseCount, _tiers) = (null, null);
                Version++;
                changed = true;
                continue;
            }
            var important = false;
            if (sold && !row.Sold)
            {
                row.Sold = true;
                important = true;
            }
            if (good.Price != row.Price)
            {
                row.Price = good.Price;
                important = true;
            }
            if (nowUnix > row.LastSeen)
            {
                row.LastSeen = nowUnix;
                changed = true;
            }
            if (important)
            {
                Version++;
                changed = true;
                MarkUpdated(row);
            }
            else if (row.LastSeen - row.WrittenSeen >= WriteSeenEvery) MarkUpdated(row);
        }
        return changed;
    }

    // copies are taken as they are, a known listing keeps what we have and learns what only the file knows - a changed row needs the full rewrite, the update lines carry only seen / sold / price
    public (int Added, int Updated, int Skipped) Merge(IEnumerable<LocalRow> incoming, IReadOnlyDictionary<int, string> affixTexts)
    {
        int added = 0, updated = 0, skipped = 0;
        foreach (var row in incoming)
        {
            if (row.GoodId <= 0 || row.Quantity <= 0 || row.Price < 0)
            {
                skipped++;
                continue;
            }
            if (_rows.TryGetValue(row.GoodId, out var ours))
            {
                if (Absorb(ours, row)) updated++;
                continue;
            }
            row.WrittenSeen = row.LastSeen;
            _rows[row.GoodId] = row;
            _newRows.Add(row.GoodId);
            added++;
        }
        foreach (var (id, text) in affixTexts)
            if (_affixes.TryAdd(id, text)) _newAffixes.Add(id);
        if (added + updated == 0) return (0, 0, skipped);
        if (updated > 0) _compact = true;
        (_baseCount, _tiers) = (null, null);
        Version++;
        return (added, updated, skipped);
    }

    static (long, long, bool, long, int, int, int, int, int, int, int, int, bool, int, int, int, int, bool, bool, bool, RowAffix[]?, string[]?) Shape(LocalRow row) =>
        (row.FirstSeen, row.LastSeen, row.Sold, row.Price, row.Quantity, row.BaseId, row.SpecialId, row.Rare, row.Level, row.ItemLevel, row.Quality, row.Type, row.Commodity, row.SkillId, row.SkillType, row.HoleNum, row.EnhanceCount, row.Forged, row.Corrode, row.Unidentified, row.Affixes, row.Flags);

    static bool Absorb(LocalRow ours, LocalRow theirs)
    {
        var before = Shape(ours);
        if (theirs.LastSeen > ours.LastSeen) (ours.Price, ours.Quantity, ours.LastSeen) = (theirs.Price, theirs.Quantity, theirs.LastSeen);
        if (theirs.FirstSeen > 0) ours.FirstSeen = ours.FirstSeen > 0 ? Math.Min(ours.FirstSeen, theirs.FirstSeen) : theirs.FirstSeen;
        ours.Sold |= theirs.Sold;
        ours.BaseId = Fill(ours.BaseId, theirs.BaseId);
        ours.SpecialId = Fill(ours.SpecialId, theirs.SpecialId);
        ours.Rare = Fill(ours.Rare, theirs.Rare);
        ours.Level = Fill(ours.Level, theirs.Level);
        ours.ItemLevel = Fill(ours.ItemLevel, theirs.ItemLevel);
        ours.Quality = Fill(ours.Quality, theirs.Quality);
        ours.Type = Fill(ours.Type, theirs.Type);
        ours.SkillId = Fill(ours.SkillId, theirs.SkillId);
        ours.SkillType = Fill(ours.SkillType, theirs.SkillType);
        ours.HoleNum = Fill(ours.HoleNum, theirs.HoleNum);
        ours.EnhanceCount = Fill(ours.EnhanceCount, theirs.EnhanceCount);
        ours.Commodity |= theirs.Commodity;
        ours.Forged |= theirs.Forged;
        ours.Corrode |= theirs.Corrode;
        ours.Unidentified |= theirs.Unidentified;
        if (ours.Affixes is not { Length: > 0 } && theirs.Affixes is { Length: > 0 }) ours.Affixes = theirs.Affixes;
        if (ours.Flags is not { Length: > 0 } && theirs.Flags is { Length: > 0 }) ours.Flags = theirs.Flags;
        return !before.Equals(Shape(ours));
    }

    static int Fill(int mine, int theirs) => mine == 0 ? theirs : mine;

    void MarkUpdated(LocalRow row)
    {
        if (!_newRows.Contains(row.GoodId)) _updatedRows.Add(row.GoodId);
    }

    LocalRow Create(TradeGood good, bool sold, long now)
    {
        var item = good.Item;
        foreach (var affix in good.Affixes)
            if (_affixes.TryAdd(affix.Id, Template(affix.Text))) _newAffixes.Add(affix.Id);
        return new LocalRow
        {
            GoodId = good.GoodId,
            BaseId = item.BaseId,
            SpecialId = good.SpecialId,
            Rare = item.RareValue,
            Level = item.Level,
            ItemLevel = good.ItemLevel,
            Quality = item.Quality,
            Type = item.Type,
            Commodity = item.StackMax > 1,
            Price = good.Price,
            Quantity = item.Count,
            FirstSeen = now,
            LastSeen = now,
            WrittenSeen = now,
            Sold = sold,
            Affixes = good.Affixes.Length > 0 ? [.. good.Affixes.Select(affix => new RowAffix(affix.Kind, affix.Id, good.SpecialId != 0 ? 0 : affix.Tier))] : null,
            Flags = good.Flags.Length > 0 ? good.Flags : null,
            SkillId = good.SkillId,
            SkillType = good.SkillType,
            HoleNum = good.HoleNum,
            EnhanceCount = good.EnhanceCount,
            Forged = good.Forged,
            Corrode = good.Corrode,
            Unidentified = good.Unidentified,
        };
    }

    public Dictionary<int, Price> Prices(long nowUnix, TimeSpan window, bool excludeOutliers = true)
    {
        var cutoff = nowUnix - (long)window.TotalSeconds;
        var groups = new Dictionary<int, List<(double Unit, long Seen)>>();
        foreach (var row in _rows.Values)
        {
            if (!row.Commodity || row.Sold || row.Quantity <= 0 || row.LastSeen < cutoff) continue;
            if (!groups.TryGetValue(row.BaseId, out var list)) groups[row.BaseId] = list = [];
            list.Add(((double)row.Price / row.Quantity, row.LastSeen));
        }
        var prices = new Dictionary<int, Price>();
        foreach (var (baseId, list) in groups) prices[baseId] = Pick(list, excludeOutliers);
        return prices;
    }

    internal static Price Pick(List<(double Unit, long Seen)> list, bool excludeOutliers = true)
    {
        list.Sort((a, b) => a.Unit.CompareTo(b.Unit));
        var q1 = list[list.Count / 4].Unit;
        var q3 = list[list.Count * 3 / 4].Unit;
        var fence = (q3 - q1) * 1.5;
        var kept = excludeOutliers ? list.Where(entry => entry.Unit >= q1 - fence && entry.Unit <= q3 + fence).ToList() : list;
        var units = kept.Select(entry => entry.Unit).ToArray();
        var newest = kept.Max(entry => entry.Seen);
        return new Price((float)Exchange.ReferencePrice(units), (float)units[0], (float)units[(int)((units.Length - 1) * 0.9)], DateTimeOffset.FromUnixTimeSeconds(newest), units.Length, 1);
    }

    public LegendaryPrices Legendary(long nowUnix, TimeSpan window, bool excludeOutliers = true)
    {
        var cutoff = nowUnix - (long)window.TotalSeconds;
        var unidentified = new Dictionary<int, List<(double, long)>>();
        var identified = new Dictionary<int, List<(double, long)>>();
        var variants = new Dictionary<ulong, List<(double, long)>>();
        static void AddTo<TKey>(Dictionary<TKey, List<(double, long)>> groups, TKey key, (double, long) entry) where TKey : notnull
        {
            if (!groups.TryGetValue(key, out var list)) groups[key] = list = [];
            list.Add(entry);
        }
        foreach (var row in _rows.Values)
        {
            if (row.Commodity || row.SpecialId == 0 || row.Sold || row.Quantity <= 0 || row.LastSeen < cutoff) continue;
            var entry = ((double)row.Price / row.Quantity, row.LastSeen);
            if (row.Unidentified) AddTo(unidentified, row.SpecialId, entry);
            else
            {
                AddTo(identified, row.SpecialId, entry);
                AddTo(variants, row.VariantKey, entry);
            }
        }
        Dictionary<TKey, Price> Priced<TKey>(Dictionary<TKey, List<(double, long)>> groups) where TKey : notnull => groups.ToDictionary(pair => pair.Key, pair => Pick(pair.Value, excludeOutliers));
        return new LegendaryPrices(Priced(unidentified), Priced(identified), Priced(variants));
    }

    public int Purge(long cutoffUnix)
    {
        var stale = _rows.Values.Where(row => row.LastSeen < cutoffUnix).Select(row => row.GoodId).ToList();
        foreach (var id in stale)
        {
            _rows.Remove(id);
            _newRows.Remove(id);
            _updatedRows.Remove(id);
        }
        if (stale.Count == 0) return 0;
        var used = _rows.Values.Where(row => row.Affixes is not null).SelectMany(row => row.Affixes!).Select(affix => affix.Id).ToHashSet();
        foreach (var id in _affixes.Keys.Where(id => !used.Contains(id)).ToList())
        {
            _affixes.Remove(id);
            _newAffixes.Remove(id);
        }
        (_baseCount, _tiers) = (null, null);
        Version++;
        _compact = true;
        return stale.Count;
    }

    static string PathOf(string directory, int season) => Path.Combine(directory, $"local-{season}.jsonl");

    public void Load(string directory, int season)
    {
        try
        {
            var path = PathOf(directory, season);
            if (!File.Exists(path)) return;
            foreach (var text in File.ReadLines(path))
            {
                if (text.Length == 0) continue;
                if (!Replay(text)) _compact = true;
            }
            if (_fileUpdates > _rows.Count) _compact = true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // whatever was read stays, the rest comes back from the next scan
        }
        Version++;
    }

    bool Replay(string text)
    {
        Line? line;
        try
        {
            line = JsonSerializer.Deserialize<Line>(text, Options);
        }
        catch (JsonException)
        {
            return false;
        }
        if (line is null) return false;
        if (line.Affix is { } affix) _affixes.TryAdd(affix.Id, affix.Text);
        else if (line.Row is { } row)
        {
            row.WrittenSeen = row.LastSeen;
            if (_rows.TryGetValue(row.GoodId, out var known))
            {
                known.LastSeen = Math.Max(known.LastSeen, row.LastSeen);
                known.Sold |= row.Sold;
            }
            else
            {
                _rows[row.GoodId] = row;
                (_baseCount, _tiers) = (null, null);
            }
        }
        else if (line.Update is { } update)
        {
            _fileUpdates++;
            if (_rows.TryGetValue(update.GoodId, out var known))
            {
                known.LastSeen = Math.Max(known.LastSeen, update.LastSeen);
                known.Sold |= update.Sold;
                if (update.Price > 0) known.Price = update.Price;
            }
        }
        else return false;
        return true;
    }

    public void Flush(string directory, int season)
    {
        if (!Dirty) return;
        try
        {
            Directory.CreateDirectory(directory);
            var path = PathOf(directory, season);
            if (_compact || _fileUpdates + _updatedRows.Count > _rows.Count) Rewrite(path);
            else Append(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // dirty stays set, the next flush tries again
        }
    }

    string Serialize(Line line) => JsonSerializer.Serialize(line, Options);

    void Append(string path)
    {
        using var writer = new StreamWriter(path, true);
        foreach (var id in _newAffixes) writer.WriteLine(Serialize(new Line { Affix = new AffixLine(id, _affixes[id]) }));
        foreach (var id in _newRows) writer.WriteLine(Serialize(new Line { Row = _rows[id] }));
        foreach (var id in _updatedRows)
        {
            var row = _rows[id];
            writer.WriteLine(Serialize(new Line { Update = new Update(id, row.LastSeen, row.Sold, row.Price) }));
            _fileUpdates++;
        }
        writer.Flush();
        MarkWritten();
    }

    void Rewrite(string path)
    {
        var temp = path + ".tmp";
        using (var writer = new StreamWriter(temp))
        {
            foreach (var (id, text) in _affixes) writer.WriteLine(Serialize(new Line { Affix = new AffixLine(id, text) }));
            foreach (var row in _rows.Values) writer.WriteLine(Serialize(new Line { Row = row }));
        }
        File.Move(temp, path, true);
        _fileUpdates = 0;
        _compact = false;
        foreach (var row in _rows.Values) row.WrittenSeen = row.LastSeen;
        MarkWritten();
    }

    void MarkWritten()
    {
        foreach (var id in _newRows) _rows[id].WrittenSeen = _rows[id].LastSeen;
        foreach (var id in _updatedRows) _rows[id].WrittenSeen = _rows[id].LastSeen;
        _newRows.Clear();
        _updatedRows.Clear();
        _newAffixes.Clear();
    }
}
