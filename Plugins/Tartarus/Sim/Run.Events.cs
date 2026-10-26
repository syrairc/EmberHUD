using System;
using System.Collections.Generic;
using System.Linq;

namespace Tartarus.Sim;

public sealed partial class Run
{
    static readonly Dictionary<EventKind, (int Count, int Gain, EventKind? Next)> Usurpations = new()
    {
        [EventKind.UsurpationI] = (1, 1, EventKind.UsurpationII),
        [EventKind.UsurpationII] = (1, 2, EventKind.UsurpationIII),
        [EventKind.UsurpationIII] = (2, 2, EventKind.UsurpationIV),
        [EventKind.UsurpationIV] = (int.MaxValue, 2, null),
    };

    public EventKind? PendingEvent { get; internal set; }

    public int EventCell { get; internal set; } = -1;

    public bool ChainArmed { get; internal set; }

    public IReadOnlyList<EventKind> EventPool => Pool;

    internal List<EventKind> Pool { get; set; } =
    [
        EventKind.ChainReaction, EventKind.Execution, EventKind.BehindClosedDoors, EventKind.RavenousAppetite, EventKind.HighRiskConvict,
        EventKind.CrowWhispers, EventKind.TasteOfFreedom, EventKind.UsurpationI, EventKind.SewerRatway, EventKind.TreasureHunt, EventKind.ExtraPortion,
    ];

    internal bool Ratway { get; set; }

    void Resolve(bool confirmed)
    {
        var cell = EventCell;
        var kind = PendingEvent;
        PendingEvent = null;
        EventCell = -1;
        Phase = Position == Office ? Phase.Office : Phase.Manage;
        if (confirmed && kind is { } happening) Happen(happening, cell);
    }

    void Happen(EventKind kind, int cell)
    {
        switch (kind)
        {
            case EventKind.ChainReaction:
                ChainArmed = true;
                break;
            case EventKind.Execution:
                Execute(cell);
                break;
            case EventKind.BehindClosedDoors:
                Disguise(cell);
                break;
            case EventKind.RavenousAppetite:
                Appetite();
                break;
            case EventKind.HighRiskConvict:
                var locked = LockedCells();
                if (locked.Count > 0) OpenDoor(locked[Pick(locked.Count)], 3, true);
                break;
            case EventKind.CrowWhispers:
                Whisper(cell);
                break;
            case EventKind.TasteOfFreedom:
                foreach (var door in Sample(LockedCells(), 3)) OpenDoor(door);
                break;
            case EventKind.SewerRatway:
                Ratway = true;
                Phase = Phase.Patrol;
                Doors = [.. Enumerable.Range(1, Cells.Length - 1)];
                break;
            case EventKind.TreasureHunt:
                Hunt();
                break;
            case EventKind.ExtraPortion:
                foreach (var inmate in Sample(Inmates(), 3)) Raise(inmate, 1);
                break;
            case EventKind.SharedFortune:
                Share(cell);
                break;
            case EventKind.Archenemy:
                Nemesis(cell);
                break;
            case EventKind.TransferOfConvicts:
                Transfer(cell);
                break;
            default:
                Usurp(kind);
                break;
        }
    }

    void Execute(int cell)
    {
        if (cell < 0 || !Cells[cell].Open) return;
        var executioner = Cells[cell];
        var victims = Inmates().Where(other => other != cell && Cells[other].Tier < executioner.Tier && Offset(executioner.Beast, Cells[other].Beast) == 1).ToList();
        foreach (var victim in victims) Lock(victim);
        if (victims.Count > 0) Raise(cell, victims.Count);
    }

    void Share(int cell)
    {
        if (cell < 0 || !Cells[cell].Open) return;
        // guess: the game's "Code" is the beast species
        var same = Inmates().Count(other => other != cell && Cells[other].Beast == Cells[cell].Beast);
        if (same > 0) Raise(cell, same);
    }

    void Nemesis(int cell)
    {
        if (cell < 0 || !Cells[cell].Open) return;
        var doors = LockedCells().Where(door => !Cells[door].Markers.HasFlag(Marker.Nest)).ToList();
        if (doors.Count == 0) return;
        var door = doors[Pick(doors.Count)];
        var eater = Cells[cell];
        var beasts = Enum.GetValues<Beast>().Where(beast => Eats(eater, beast)).ToList();
        OpenDoor(door);
        if (beasts.Count > 0 && eater.Tier < Tiers.Chromatic) Cells[door] = new Cell(true, beasts[Pick(beasts.Count)], eater.Tier, false);
    }

    void Transfer(int cell)
    {
        if (cell < 0 || !Cells[cell].Open) return;
        var locked = LockedCells();
        if (locked.Count > 0) Fill(locked[Pick(locked.Count)], new Cell(true, Cells[cell].Beast, Cells[cell].Tier, false));
    }

    void Disguise(int cell)
    {
        if (cell < 0 || !Cells[cell].Open) return;
        var counts = new int[4];
        foreach (var inmate in Inmates()) counts[(int)Cells[inmate].Beast]++;
        var scarcest = Enumerable.Range(0, 4).Where(beast => counts[beast] == counts.Min()).ToList();
        Cells[cell] = Cells[cell] with { Beast = (Beast)scarcest[Pick(scarcest.Count)], Tier = Tiers.Ultimate };
        Reach();
    }

    void Appetite()
    {
        var inmates = Inmates();
        if (inmates.Count == 0) return;
        var lowest = inmates.Min(inmate => Cells[inmate].Tier);
        var candidates = inmates.Where(inmate => Cells[inmate].Tier == lowest).ToList();
        var chosen = candidates[Pick(candidates.Count)];
        var successes = Enumerable.Range(0, 5).Count(_ => Roll(50));
        if (successes > 0) Raise(chosen, successes);
    }

    void Whisper(int cell)
    {
        if (cell < 0) return;
        var shown = 0;
        for (var step = 1; step < Cells.Length && shown < 4; step++)
        {
            var next = (cell + step) % Cells.Length;
            if (!Locked(next) || Cells[next].Markers.HasFlag(Marker.Nest)) continue;
            Cells[next] = Reveal(next) with { Known = true };
            shown++;
        }
    }

    void Hunt()
    {
        foreach (var door in LockedCells().Where(door => !Cells[door].Markers.HasFlag(Marker.Nest)).ToList())
        {
            var revealed = Reveal(door);
            Cells[door] = revealed.Tier < Tiers.Rare ? revealed with { Known = true } : revealed with { AtLeast = Tiers.Rare };
        }
    }

    void Usurp(EventKind kind)
    {
        if (!Usurpations.TryGetValue(kind, out var usurpation)) return;
        foreach (var inmate in Sample(Inmates(), usurpation.Count)) Raise(inmate, usurpation.Gain);
        Pool.Remove(kind);
        if (usurpation.Next is not { } next) return;
        if (!Pool.Contains(next)) Pool.Add(next);
        var free = InstigateCells();
        if (free.Count == 0) return;
        var chosen = free[Pick(free.Count)];
        Cells[chosen] = Cells[chosen] with { Event = next, Seen = true };
    }

    Cell Reveal(int index) => Cells[index].Tier >= 0 ? Cells[index] : Occupied(Cells[index], 0);
}
