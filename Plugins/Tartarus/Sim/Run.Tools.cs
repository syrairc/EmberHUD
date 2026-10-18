using System.Linq;

namespace Tartarus.Sim;

public sealed partial class Run
{
    void OpenRandom(int count)
    {
        foreach (var door in Sample(LockedCells(), count)) OpenDoor(door);
    }

    void OnPatrolOpen(int cell)
    {
        if (Holds(ToolId.DogTag) && Roll(25)) Raise(cell, 1);
        if (Holds(ToolId.Blacklist) && LockedCells().Count <= 5)
            foreach (var inmate in Sample(Inmates(), 2)) Raise(inmate, 1);
        if (Holds(ToolId.Fire) && Roll(35) && Cells[cell].Open && LockedCells() is { Count: > 0 } dark)
            Fill(dark[Pick(dark.Count)], new Cell(true, Cells[cell].Beast, Cells[cell].Tier, false));
        if (Holds(ToolId.Keys) && Roll(80))
            foreach (var side in new[] { cell - 1, cell + 1 })
            {
                var neighbour = (side + Cells.Length) % Cells.Length;
                if (Locked(neighbour)) OpenDoor(neighbour);
            }
        if (Holds(ToolId.Forged) && Roll(50)) OpenRandom(1);
    }

    void Shackles(int steps)
    {
        if (Holds(ToolId.Shackles) && steps >= 4 && Roll(40))
        {
            var inmates = Inmates();
            if (inmates.Count > 0) Raise(inmates[Pick(inmates.Count)], 1);
        }
        if (Holds(ToolId.ShortChain) && steps <= 3 && Roll(50)) OpenRandom(1);
    }

    void Scout()
    {
        var dark = LockedCells().Where(door => !Cells[door].Markers.HasFlag(Marker.Nest)).ToList();
        foreach (var door in dark) Cells[door] = Reveal(door);
        if (dark.Count == 0) return;
        var best = dark.Max(door => Cells[door].Tier);
        var tied = dark.Where(door => Cells[door].Tier == best).ToList();
        var chosen = tied[Pick(tied.Count)];
        Cells[chosen] = Cells[chosen] with { Known = true };
    }

    void MorningTools()
    {
        if (Holds(ToolId.Tongs))
        {
            var bare = Inmates().Where(cell => Cells[cell].Beast == Beast.Mammoth && !Cells[cell].Markers.HasFlag(Marker.Tusk)).ToList();
            if (bare.Count > 0)
            {
                var chosen = bare[Pick(bare.Count)];
                Cells[chosen] = Cells[chosen] with { Markers = Cells[chosen].Markers | Marker.Tusk };
            }
        }
        if (Holds(ToolId.Claws))
        {
            var bare = LockedCells().Where(cell => !Cells[cell].Markers.HasFlag(Marker.Scratch) && !Cells[cell].Markers.HasFlag(Marker.Nest)).ToList();
            if (bare.Count > 0)
            {
                var chosen = bare[Pick(bare.Count)];
                Cells[chosen] = Reveal(chosen) with { Known = true, Markers = Cells[chosen].Markers | Marker.Scratch };
            }
        }
        if (Holds(ToolId.IronBox))
        {
            var free = LockedCells().Where(cell => !Cells[cell].Markers.HasFlag(Marker.Nest)).ToList();
            if (free.Count > 0)
            {
                var chosen = free[Pick(free.Count)];
                Cells[chosen] = Cells[chosen] with { Markers = Cells[chosen].Markers | Marker.Nest, NestDay = Day + 2 };
            }
        }
        if (Holds(ToolId.Ravenous)) Devour();
        if (Holds(ToolId.Coin)) Fate();
    }

    void Devour()
    {
        var inmates = Inmates();
        if (inmates.Count == 0) return;
        var top = inmates.Max(cell => Cells[cell].Tier);
        var tied = inmates.Where(cell => Cells[cell].Tier == top).ToList();
        var eater = tied[Pick(tied.Count)];
        foreach (var side in new[] { eater - 1, eater + 1 })
        {
            var neighbour = (side + Cells.Length) % Cells.Length;
            if (neighbour == eater || !Cells[neighbour].Open) continue;
            Lock(neighbour);
            if (Roll(40)) Raise(eater, 1);
        }
    }

    void Fate()
    {
        var groups = Inmates().GroupBy(cell => (Cells[cell].Beast, Cells[cell].Tier)).Where(group => group.Count() >= 2).ToList();
        if (groups.Count == 0 || !Roll(60)) return;
        var matching = groups[Pick(groups.Count)].ToList();
        var gone = matching[Pick(matching.Count)];
        matching.Remove(gone);
        Lock(gone);
        Raise(matching[Pick(matching.Count)], 2);
    }
}
