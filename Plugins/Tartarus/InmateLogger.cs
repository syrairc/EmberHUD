using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using EmberHUD;

namespace Tartarus;

sealed class InmateLogger
{
    const double SaveEverySeconds = 5;
    const double RetryLookupSeconds = 10;

    readonly Dictionary<int, InmateKind?> _kinds = [];
    readonly Dictionary<int, double> _retryAt = [];
    readonly Tracker _tracker;
    readonly RunRecorder _recorder = new();
    InmateStore? _store;
    int _areaId;
    double _lastSave = double.NegativeInfinity;
    double _lastLoad = double.NegativeInfinity;

    readonly MinigameLogger _minigame;
    MinigameStore? _minigameStore;
    bool _runSynced;
    double _lastMinigameSave = double.NegativeInfinity;
    double _lastMinigameLoad = double.NegativeInfinity;

    public InmateLogger()
    {
        _tracker = new(Classify);
        _minigame = new((species, tier) => Store.Grid[species, tier]);
    }

    InmateKind? Classify(int cfgId)
    {
        if (_kinds.TryGetValue(cfgId, out var known)) return known;
        if (_retryAt.TryGetValue(cfgId, out var retry) && Seconds() < retry) return null;
        var ownKey = $"npc|name|{cfgId}";
        var lookupFailed = false;
        var kind = Inmates.Classify(cfgId, key =>
        {
            var text = Loc.Get(key);
            if (text is null && key == ownKey) lookupFailed = true;
            return text;
        });
        if (lookupFailed) _retryAt[cfgId] = Seconds() + RetryLookupSeconds;
        else _kinds[cfgId] = kind;
        return kind;
    }

    internal string DataDirectory { get; set; } = Path.Combine(AppContext.BaseDirectory, "Config", "Tartarus");

    internal Func<double> Seconds { get; set; } = () => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;

    internal Func<DateTime> Now { get; set; } = () => DateTime.Now;

    internal InmateStore Store => _store ??= InmateStore.Load(DataDirectory);

    internal MinigameStore Minigame => _minigameStore ??= MinigameStore.Load(DataDirectory);

    public void Tick()
    {
        Game.WantInventory();
        Game.WantTartarus();
        var state = Game.State;
        if (state.AreaId != _areaId)
        {
            _areaId = state.AreaId;
            _kinds.Clear();
            _retryAt.Clear();
        }

        var seconds = Seconds();
        var nameplates = Nameplates.Count(state.Inventory);
        Dictionary<int, SceneEntity>? scene = null;
        bool? SceneAlive(int id, int cfgId)
        {
            scene ??= Scene.Entities().GroupBy(entity => entity.Id).ToDictionary(group => group.Key, group => group.First());
            return scene.TryGetValue(id, out var entry) && entry.CfgId == cfgId ? entry.Alive : null;
        }

        foreach (var sighting in _tracker.Update(state, seconds, SceneAlive))
        {
            Store.Add(sighting, state.Area?.Name ?? "", nameplates, Now());
        }

        if (_recorder.Update(state.Tartarus, nameplates, Now(), seconds) is { } run) Store.AddRun(run);
        if (!_runSynced && Minigame.Loaded)
        {
            _runSynced = true;
            _minigame.Run = Math.Max(_minigame.Run, Minigame.Entries.Select(entry => entry.Run).DefaultIfEmpty(0).Max() + 1);
        }

        if (_minigame.Event(state.Tartarus, Now()) is { } landed) Minigame.Add(landed);
        foreach (var pull in _minigame.Update(state.Tartarus, Now(), seconds)) Minigame.Add(pull);
        if (!Minigame.Loaded && seconds - _lastMinigameLoad >= SaveEverySeconds)
        {
            _lastMinigameLoad = seconds;
            var fresh = MinigameStore.Load(DataDirectory);
            if (fresh.Loaded)
            {
                fresh.Absorb(Minigame);
                _minigameStore = fresh;
            }
        }

        if (Minigame.Dirty && seconds - _lastMinigameSave >= SaveEverySeconds)
        {
            Minigame.Save(DataDirectory);
            _lastMinigameSave = seconds;
        }

        if (!Store.Loaded && seconds - _lastLoad >= SaveEverySeconds)
        {
            _lastLoad = seconds;
            var fresh = InmateStore.Load(DataDirectory);
            if (fresh.Loaded)
            {
                fresh.Absorb(Store);
                _store = fresh;
            }
        }

        if (Store.Dirty && seconds - _lastSave >= SaveEverySeconds)
        {
            Store.Save(DataDirectory);
            _lastSave = seconds;
        }
    }

    public void DrawPage() => InmateLogUI.Draw(Store, Nameplates.Count(Game.State.Inventory), Store.Clear);

    public void DrawMinigamePage() => MinigameLogUI.Draw(Minigame, Minigame.Clear);

    public void Dispose()
    {
        if (_minigameStore is { Dirty: true }) _minigameStore.Save(DataDirectory);
        if (_store is { Dirty: true }) _store.Save(DataDirectory);
    }
}
