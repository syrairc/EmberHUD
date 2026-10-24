using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Prices;

public sealed record Point(DateTimeOffset At, float Bags, float Stash, float Total);

public sealed record StashTab(int Index, DateTimeOffset SeenAt, Line[] Lines);

public sealed record ProfileData(int Version, Run[] Runs, Point[] Totals, Snapshot? Bag, StashTab[] Tabs, Dictionary<int, double[][]> PriceHistory, Dictionary<int, string> Names, Snapshot? Open = null, Snapshot? Live = null, Dictionary<int, string>? Icons = null);

public sealed class WealthStore
{
    const int MaxRuns = 1000;
    const int MaxTotals = 3000;
    const int MaxPrices = 60;
    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    static readonly object saveLock = new();

    static ProfileData Empty => new(1, [], [], null, [], [], [], null, null, []);

    public ProfileData Data { get; private set; } = Empty;

    public void AddSnapshot(Snapshot bag, IReadOnlyList<StashTab> tabs, IReadOnlyDictionary<int, string> names, IReadOnlyDictionary<int, string>? icons = null)
    {
        if (!float.IsFinite(bag.Total) || !float.IsFinite(bag.Fe) || !Finite(bag.Lines)) return;
        foreach (var tab in tabs) SetTab(tab);
        var stash = Data.Tabs.Sum(tab => tab.Lines.Sum(line => line.Value));
        var history = new Dictionary<int, double[][]>(Data.PriceHistory);
        var stamp = bag.At.ToUnixTimeSeconds();
        foreach (var line in bag.Lines.Concat(tabs.SelectMany(tab => tab.Lines)).GroupBy(line => line.BaseId).Select(group => group.First()))
        {
            if (!float.IsFinite(line.Unit) || line.Unit <= 0) continue;
            var prices = history.GetValueOrDefault(line.BaseId) ?? [];
            if (prices.Length > 0 && prices[^1][1] == line.Unit) continue;
            history[line.BaseId] = prices.Append([stamp, line.Unit]).TakeLast(MaxPrices).ToArray();
        }
        var merged = new Dictionary<int, string>(Data.Names);
        foreach (var (id, name) in names) merged[id] = name;
        var mergedIcons = new Dictionary<int, string>(Data.Icons ?? []);
        foreach (var (id, icon) in icons ?? new Dictionary<int, string>())
            if (!string.IsNullOrEmpty(icon)) mergedIcons[id] = icon;
        Data = Data with
        {
            Bag = bag,
            Totals = Data.Totals.Append(new Point(bag.At, bag.Total, stash, bag.Total + stash)).TakeLast(MaxTotals).ToArray(),
            PriceHistory = history,
            Names = merged,
            Icons = mergedIcons,
            Live = null,
        };
    }

    public void AddRun(Run run)
    {
        if (!float.IsFinite(run.StartTotal) || !float.IsFinite(run.EndTotal) || !Finite(run.Loot) || !Finite(run.Costs)) return;
        Data = Data with { Runs = Data.Runs.Append(run).TakeLast(MaxRuns).ToArray() };
    }

    public void SetOpen(Snapshot? open)
    {
        if (open is null || Finite(open)) Data = Data with { Open = open };
    }

    public void SetLive(Snapshot? live)
    {
        if (live is null || Finite(live)) Data = Data with { Live = live };
    }

    public void SetTab(StashTab tab)
    {
        if (!Finite(tab.Lines)) return;
        Data = Data with { Tabs = Data.Tabs.Where(t => t.Index != tab.Index).Append(tab).OrderBy(t => t.Index).ToArray() };
    }

    public void Clear() => Data = Empty;

    public void Save(string directory, string key)
    {
        lock (saveLock) SaveLocked(directory, key);
    }

    void SaveLocked(string directory, string key)
    {
        key = Safe(key);
        Directory.CreateDirectory(directory);
        var json = JsonSerializer.Serialize(Data, Json);
        Write(Path.Combine(directory, key + ".json"), json);
        Write(Path.Combine(directory, $"wealth-{key}.js"), $"(window.WEALTH = window.WEALTH || {{}})[{JsonSerializer.Serialize(key)}] = {json};");
        WriteIndex(directory);
    }

    public static WealthStore Load(string directory, string key)
    {
        var path = Path.Combine(directory, Safe(key) + ".json");
        try
        {
            return JsonSerializer.Deserialize<ProfileData>(File.ReadAllText(path), Json) is { } data && Valid(data)
                ? new WealthStore { Data = data }
                : Quarantine(path);
        }
        catch (FileNotFoundException)
        {
            return new WealthStore();
        }
        catch (DirectoryNotFoundException)
        {
            return new WealthStore();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return Quarantine(path);
        }
    }

    static bool Valid(ProfileData data) =>
        data.Version == 1
        && data.Runs is not null && data.Runs.All(run => run is { Loot: not null, Costs: not null } && run.Loot.All(line => line is not null) && run.Costs.All(line => line is not null))
        && data.Totals is not null && data.Totals.All(point => point is not null)
        && data.Tabs is not null && data.Tabs.All(tab => tab is { Lines: not null } && tab.Lines.All(line => line is not null))
        && (data.Bag is null || data.Bag.Lines is not null && data.Bag.Lines.All(line => line is not null))
        && data.PriceHistory is not null && data.PriceHistory.Values.All(pairs => pairs is not null && pairs.All(pair => pair is { Length: 2 }))
        && data.Names is not null
        && Sound(data.Open) && Sound(data.Live);

    static bool Sound(Snapshot? s) => s is null || s.Lines is not null && s.Lines.All(line => line is not null);

    static bool Finite(Snapshot s) => float.IsFinite(s.Total) && float.IsFinite(s.Fe) && Finite(s.Lines);

    static bool Finite(Line[] lines) => lines.All(line => float.IsFinite(line.Unit) && float.IsFinite(line.Value));

    static WealthStore Quarantine(string path)
    {
        try
        {
            File.Move(path, path + ".bad", true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // still start empty, the next save overwrites it
        }
        return new WealthStore();
    }

    static void WriteIndex(string directory)
    {
        var entries = new DirectoryInfo(directory).GetFiles("*.json")
            .Select(file => Path.GetFileNameWithoutExtension(file.Name))
            .OrderBy(key => key, StringComparer.Ordinal)
            .Select(key => key.LastIndexOf('-') is var dash and > 0 ? new IndexEntry(key, key[..dash], key[(dash + 1)..]) : new IndexEntry(key, key, ""));
        Write(Path.Combine(directory, "wealth-index.js"), $"window.WEALTH_INDEX = {JsonSerializer.Serialize(entries, Json)};");
    }

    static void Write(string path, string text)
    {
        var temp = path + ".tmp";
        File.WriteAllText(temp, text);
        File.Move(temp, path, true);
    }

    static readonly char[] Invalid = Path.GetInvalidFileNameChars();

    static string Safe(string key) => string.Concat(key.Select(c => Array.IndexOf(Invalid, c) >= 0 ? '_' : c));

    sealed record IndexEntry(string Key, string Character, string Season);
}
