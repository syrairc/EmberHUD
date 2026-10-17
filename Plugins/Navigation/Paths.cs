using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using EmberHUD;

namespace NavigationPlugin;

internal sealed record PathState(int Id, string Name, Color Colour, Vector2 Target, Vector2[]? Points, float Length);

internal sealed class Paths(Random random)
{
    public const float RecomputeDistance = 150f;
    public const float MatchDistance = 500f;

    sealed class Tracked(int id, Color colour, Vector2 target)
    {
        public int Id = id;
        public Color Colour = colour;
        public Vector2 Target = target;
        public int Number;
        public string Name = "";
        public Vector2? Planned;
        public bool Resolved;
        public Vector2[]? Points;
        public float Length;
    }

    sealed record Result((Tracked Tracked, Vector2[]? Points, float Length)[] Items);

    readonly List<Tracked> tracked = [];
    int area;
    int nextNumber;
    bool busy;
    bool dirty;
    Terrain? plannedTerrain;
    Vector2 plannedStart;
    Vector2[] plannedCentres = [];
    float plannedRadius;
    Terrain? stampSource;
    Vector2[] stampCentres = [];
    float stampRadius;
    Terrain? stamped;
    volatile Result? done;

    public Func<Terrain, Vector2, Vector2, Vector2[]?> Finder { get; set; } = PathFinder.Find;

    public Action<Action> Runner { get; set; } = job => Task.Run(job);

    public float ObstacleRadius { get; set; }

    public IReadOnlyList<PathState> Current { get; private set; } = [];

    public void Update(GameState state)
    {
        Land();
        if (state.AreaId != area) Reset(state.AreaId);
        if (state.Terrain is not { } terrain || state.Player is not { } player)
        {
            if (Current.Count > 0)
            {
                Current = [];
                dirty = true;
            }
            return;
        }
        Follow(state.QuestTargets);
        var centres = state.Entities.Where(e => e.Kind == EntityKind.MapObject).Select(e => new Vector2(e.Position.X, e.Position.Y)).ToArray();
        Dispatch(terrain, new Vector2(player.Position.X, player.Position.Y), centres);
        Land();
        if (dirty) Publish();
    }

    void Reset(int newArea)
    {
        area = newArea;
        nextNumber = 0;
        tracked.Clear();
        plannedTerrain = null;
        stamped = null;
        dirty = true;
    }

    void Follow(QuestTarget[] targets)
    {
        var ordered = targets.GroupBy(t => t.Id).Select(g => g.First()).OrderBy(t => t.Id).ToArray();
        var live = ordered.Select(t => t.Id).ToHashSet();
        var vanished = tracked.Where(t => !live.Contains(t.Id)).ToList();
        var known = tracked.Select(t => t.Id).ToHashSet();
        var changed = vanished.Count > 0;
        foreach (var target in ordered)
        {
            if (known.Contains(target.Id)) continue;
            changed = true;
            var inherit = vanished.OrderBy(v => Vector2.Distance(v.Target, target.Position)).FirstOrDefault(v => Vector2.Distance(v.Target, target.Position) <= MatchDistance);
            if (inherit is null) continue;
            vanished.Remove(inherit);
            inherit.Id = target.Id;
            known.Add(target.Id);
        }
        tracked.RemoveAll(vanished.Contains);
        foreach (var target in ordered)
            if (!known.Contains(target.Id))
                tracked.Add(new Tracked(target.Id, Palette.Pick(tracked.Select(t => t.Colour).ToHashSet(), random), target.Position) { Number = ++nextNumber });
        tracked.Sort((a, b) => a.Id.CompareTo(b.Id));

        var byId = ordered.ToDictionary(t => t.Id);
        foreach (var item in tracked)
        {
            var source = byId[item.Id];
            var name = source.Name is { Length: > 0 } ? source.Name : $"Objective {item.Number}";
            if (item.Name != name || item.Target != source.Position) dirty = true;
            item.Name = name;
            item.Target = source.Position;
        }
        dirty |= changed;
    }

    Terrain Stamped(Terrain terrain, Vector2[] centres, float radius)
    {
        if (stamped is null || !ReferenceEquals(terrain, stampSource) || radius != stampRadius || !centres.AsSpan().SequenceEqual(stampCentres))
        {
            stamped = Obstacles.Stamp(terrain, centres, radius);
            stampSource = terrain;
            stampCentres = centres;
            stampRadius = radius;
        }
        return stamped;
    }

    void Dispatch(Terrain terrain, Vector2 start, Vector2[] centres)
    {
        if (busy || tracked.Count == 0) return;
        var radius = ObstacleRadius;
        var due = !ReferenceEquals(terrain, plannedTerrain)
            || radius != plannedRadius
            || !centres.AsSpan().SequenceEqual(plannedCentres)
            || Vector2.Distance(start, plannedStart) >= RecomputeDistance
            || tracked.Any(t => t.Planned is not { } planned || Vector2.Distance(planned, t.Target) > terrain.CellSize);
        if (!due) return;

        plannedTerrain = terrain;
        plannedStart = start;
        plannedCentres = centres;
        plannedRadius = radius;
        var routing = Stamped(terrain, centres, radius);
        busy = true;
        var work = tracked.Select(t => (Tracked: t, Target: t.Target)).ToArray();
        foreach (var (item, target) in work) item.Planned = target;
        try
        {
            Runner(() =>
            {
                var items = new (Tracked, Vector2[]?, float)[work.Length];
                for (var i = 0; i < work.Length; i++)
                {
                    Vector2[]? points = null;
                    var length = 0f;
                    try
                    {
                        points = Finder(routing, start, work[i].Target);
                        if (points is null && !ReferenceEquals(routing, terrain)) points = Finder(terrain, start, work[i].Target);
                        if (points is not null) length = PathFinder.Length(points);
                    }
                    catch (Exception)
                    {
                        points = null;
                    }
                    items[i] = (work[i].Tracked, points, length);
                }
                done = new Result(items);
            });
        }
        catch (Exception)
        {
            busy = false;
            plannedTerrain = null;
        }
    }

    void Land()
    {
        var result = done;
        if (result is null) return;
        done = null;
        busy = false;
        foreach (var (item, points, length) in result.Items)
        {
            item.Points = points;
            item.Length = length;
            item.Resolved = true;
        }
        dirty = true;
    }

    void Publish()
    {
        dirty = false;
        Current = tracked.Where(t => t.Resolved).Select(t => new PathState(t.Id, t.Name, t.Colour, t.Target, t.Points, t.Length)).ToArray();
    }
}
