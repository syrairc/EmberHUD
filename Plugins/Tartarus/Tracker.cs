using System;
using System.Collections.Generic;
using EmberHUD;

namespace Tartarus;

public enum SightingEnd { Killed, Unsure }

public enum SightingSource { Life, SceneDead, SceneAlive }

public sealed record Sighting(int CfgId, InmateKind Kind, SightingEnd End, double LastLife, double MaxLife, float Distance, double SecondsSeen, SightingSource Source = SightingSource.Life);

public sealed class Tracker(Func<int, InmateKind?> classify)
{
    sealed record Seen(int CfgId, InmateKind Kind, double FirstSeen, double LastSeen, double Life, double MaxLife, float Distance);

    const double MissingSeconds = 0.5;

    readonly Dictionary<int, Seen> tracked = [];
    readonly List<int> gone = [];
    readonly HashSet<int> present = [];
    int areaId;

    internal static SightingEnd EndOf(double lastLife, double maxLife) => lastLife == 0 && maxLife > 0 ? SightingEnd.Killed : SightingEnd.Unsure;

    public Sighting[] Update(GameState state, double nowSeconds, Func<int, int, bool?>? sceneAlive = null)
    {
        if (state.AreaId != areaId)
        {
            areaId = state.AreaId;
            tracked.Clear();
        }

        present.Clear();
        foreach (var entity in state.Entities)
        {
            if (entity.Kind != EntityKind.Monster || classify(entity.CfgId) is not { } kind) continue;
            present.Add(entity.Id);
            var distance = state.Player?.FlatDistance(entity.Position) ?? 0;
            var first = tracked.TryGetValue(entity.Id, out var old) ? old.FirstSeen : nowSeconds;
            tracked[entity.Id] = new(entity.CfgId, kind, first, nowSeconds, entity.Life, entity.MaxLife, distance);
        }

        gone.Clear();
        var ended = new List<Sighting>();
        foreach (var (id, seen) in tracked)
        {
            if (present.Contains(id) || nowSeconds - seen.LastSeen < MissingSeconds) continue;
            gone.Add(id);
            var (end, source) = sceneAlive?.Invoke(id, seen.CfgId) switch
            {
                false => (SightingEnd.Killed, SightingSource.SceneDead),
                true => (SightingEnd.Unsure, SightingSource.SceneAlive),
                null => (EndOf(seen.Life, seen.MaxLife), SightingSource.Life),
            };
            ended.Add(new(seen.CfgId, seen.Kind, end, seen.Life, seen.MaxLife, seen.Distance, seen.LastSeen - seen.FirstSeen, source));
        }

        foreach (var id in gone) tracked.Remove(id);
        return [.. ended];
    }
}
