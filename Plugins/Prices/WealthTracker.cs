using System;
using System.Collections.Generic;
using System.Linq;
using EmberHUD;

namespace Prices;

public sealed class WealthTracker(Func<DateTimeOffset> now)
{
    static readonly TimeSpan StashEvery = TimeSpan.FromSeconds(3);

    readonly RunTracker runs = new();
    IReadOnlyDictionary<int, Price> prices = new Dictionary<int, Price>();
    (int AreaId, string Name)? lastArea;
    DateTimeOffset lastSnapshotAt;
    static readonly TimeSpan LiveEvery = TimeSpan.FromSeconds(1);

    Line[]? lastLive;
    DateTimeOffset lastLiveAt;
    int? lastTab;
    DateTimeOffset lastTabAt;

    public ProfitSession Session { get; } = new(now);

    public Func<string, WealthStore> Open { get; set; } = _ => new WealthStore();

    public string? Key { get; private set; }

    public WealthStore? Store { get; private set; }

    public Snapshot? Last { get; private set; }

    public Snapshot? Current { get; private set; }

    public bool Paused { get; set; }

    public Func<InventoryItem, bool> Include { get; set; } = _ => true;

    public float? BagWealth { get; private set; }

    readonly List<(string Key, WealthStore Store)> retired = [];

    public (string Key, WealthStore Store)[] Retired
    {
        get
        {
            lock (retired) return [.. retired];
        }
    }

    public void Release((string Key, WealthStore Store) item)
    {
        lock (retired) retired.Remove(item);
    }

    public Run? CurrentRun => runs.Open is { } open && Last is { } last
        ? CurrentOf(open, last)
        : null;

    Run CurrentOf(Snapshot open, Snapshot last)
    {
        var loot = Valuation.Diff(open.Lines, last.Lines);
        return new Run(open.At, last.At, open.AreaName, open.Total, last.Total, runs.Before is null, Valuation.AnyUnpriced(loot), loot, []);
    }

    public bool Tick(GameState state, IReadOnlyDictionary<int, Price> current, int? season, int snapshotMinutes, string? character)
    {
        if (Paused || string.IsNullOrEmpty(character) || season is not { } known || state.Inventory is not { Length: > 0 } bag || state.Area is not { } area || current.Count == 0) return false;
        prices = current;
        var key = $"{character}-{known}";
        if (key != Key) Switch(key);
        var store = Store!;
        var at = now();
        var added = false;
        StashTab? stored = null;
        if (state.Stash is { } stash && state.StashItems is { } items && (lastTab != stash.Tab || at - lastTabAt >= StashEvery))
        {
            var lines = Valuation.Value(items.Where(Include).ToArray(), prices, at, state.AreaId, area.Name, area.IsTown).Lines;
            var old = store.Data.Tabs.FirstOrDefault(tab => tab.Index == stash.Tab);
            if (old is null || !old.Lines.SequenceEqual(lines))
            {
                stored = new StashTab(stash.Tab, at, lines);
                store.SetTab(stored);
                added = true;
            }
            (lastTab, lastTabAt) = (stash.Tab, at);
        }
        var due = lastArea != (state.AreaId, area.Name) || at - lastSnapshotAt >= TimeSpan.FromMinutes(Math.Max(1, snapshotMinutes));
        if (!due)
        {
            if (at - lastLiveAt < LiveEvery) return added;
            lastLiveAt = at;
            var live = Current = Valuation.Value(bag, prices, at, state.AreaId, area.Name, area.IsTown);
            BagWealth = Shown(bag, at, state, area);
            if (lastLive is not null && live.Lines.SequenceEqual(lastLive)) return added;
            lastLive = live.Lines;
            if (runs.Open is null)
            {
                if (area.IsTown) runs.Baseline(live);
                return added;
            }
            store.SetLive(live);
            return true;
        }
        var snapshot = Valuation.Value(bag, prices, at, state.AreaId, area.Name, area.IsTown);
        BagWealth = Shown(bag, at, state, area);
        store.AddSnapshot(snapshot, stored is null ? [] : [stored], Names(snapshot, store), Icons(snapshot, store));
        lastLive = snapshot.Lines;
        if (runs.OnSnapshot(snapshot, prices) is { } run)
        {
            store.AddRun(run);
            Session.Add(run);
        }
        store.SetOpen(runs.Open);
        (Last, Current, lastArea, lastSnapshotAt) = (snapshot, snapshot, (state.AreaId, area.Name), at);
        return true;
    }

    float Shown(InventoryItem[] bag, DateTimeOffset at, GameState state, MapInfo area) =>
        Valuation.Value(bag.Where(Include).ToArray(), prices, at, state.AreaId, area.Name, area.IsTown).Total;

    public void Reset()
    {
        Store?.Clear();
        runs.Reset();
        Session.Restart();
        (Last, Current, lastArea, lastTab, lastLive, BagWealth) = (null, null, null, null, null, null);
    }

    void Switch(string key)
    {
        if (Key is { } old && Store is { } oldStore)
            lock (retired) retired.Add((old, oldStore));
        WealthStore? back = null;
        lock (retired)
        {
            var again = retired.FindIndex(item => item.Key == key);
            if (again >= 0)
            {
                back = retired[again].Store;
                retired.RemoveAt(again);
            }
        }
        (Key, Store) = (key, back ?? Open(key));
        runs.Reset();
        Session.Restart();
        (Last, Current, lastArea, lastTab, lastLive, BagWealth) = (null, null, null, null, null, null);
    }

    static IEnumerable<int> Ids(Snapshot snapshot, WealthStore store) =>
        snapshot.Lines.Concat(store.Data.Tabs.SelectMany(tab => tab.Lines)).Select(line => line.BaseId).Distinct();

    static Dictionary<int, string> Names(Snapshot snapshot, WealthStore store)
    {
        var names = new Dictionary<int, string>();
        foreach (var id in Ids(snapshot, store))
            if (ItemNames.Of(id) is { Length: > 0 } name) names[id] = name;
        return names;
    }

    internal static Dictionary<int, string> Icons(Snapshot snapshot, WealthStore store)
    {
        var icons = new Dictionary<int, string>();
        foreach (var id in Ids(snapshot, store))
            if (Items.TryGet(id, out var info) && !string.IsNullOrEmpty(info.Icon)) icons[id] = info.Icon;
        return icons;
    }
}
