using System;
using System.Collections.Generic;
using System.Linq;

namespace Tartarus.Sim;

internal sealed record Chance(double[]? BeastWeights = null, double[]? DoorTierWeights = null, double[]? ReplaceTierWeights = null, Func<int, int>? Index = null, ToolTable? Tools = null, int? CoinPercent = null);

public sealed partial class Run
{
    const int Office = 0;
    const int MaxDays = 60;
    const int MaxRing = 64;

    Rng rng;

    internal Run(Rules rules, Rng rng)
    {
        Rules = rules;
        this.rng = rng;
        MaxDay = Math.Clamp(rules.Card == StartCard.IronLaw ? 30 : rules.Days, 1, MaxDays);
        Day = 1;
        Cells = [.. Enumerable.Repeat(new Cell(false, Beast.Mammoth, -1, false), Math.Clamp(rules.RingSize, 2, MaxRing))];
    }

    public static Run Start(Rules rules, ulong seed)
    {
        var run = new Run(rules, new Rng(seed));
        run.SpawnEvent(rules.EventStartChance);
        run.Objective = new Objective(run.Pick(2) == 0 ? ObjectiveKind.King : ObjectiveKind.Walk);
        for (var i = 0; i < rules.StartTools; i++) run.OfferTools();
        if (run.Queue.Count == 0) run.RollDoors();
        return run;
    }

    public Rules Rules { get; }

    public int MaxDay { get; }

    public int Position { get; internal set; }

    public int Day { get; internal set; }

    public int Chromatics { get; internal set; }

    public Phase Phase { get; internal set; }

    public bool Over => Phase == Phase.Ended;

    public Cell[] Cells { get; private set; }

    public IReadOnlyList<ToolId> Tools => Held;

    internal List<ToolId> Held { get; private set; } = [];

    internal int[] Doors { get; set; } = [];

    internal Chance Chance { get; set; } = new();

    public int Chaining { get; internal set; } = -1;

    internal bool Consumed { get; set; }

    internal bool Replaced { get; set; }

    internal bool Hunted { get; set; }

    public Run Clone()
    {
        var copy = (Run)MemberwiseClone();
        copy.rng = rng.Clone();
        copy.Cells = [.. Cells];
        copy.Held = [.. Held];
        copy.Pool = [.. Pool];
        copy.Prey = [.. Prey];
        copy.Queue = [.. Queue];
        return copy;
    }

    public IReadOnlyList<Move> Legal()
    {
        if (Phase == Phase.Ended) return [];
        if (Queue.Count > 0)
        {
            var offer = Queue[0];
            var fresh = offer.Kind == MoveKind.Instrument ? offer.Options.Where(option => !Holds((ToolId)option)).ToArray() : [];
            return [.. (fresh.Length > 0 ? fresh : offer.Options).Select(option => new Move(offer.Kind, Param: option))];
        }
        return Phase switch
        {
            Phase.Patrol => [.. Doors.Select(door => new Move(MoveKind.Door, door))],
            Phase.Manage when Chaining >= 0 => [.. Meals(Chaining).Select(meal => new Move(MoveKind.Consume, meal)), new Move(MoveKind.StopChain)],
            Phase.Manage => [.. ManageMoves(), new Move(MoveKind.End)],
            Phase.Office => [new Move(MoveKind.Instigate), new Move(MoveKind.Bestow), new Move(MoveKind.End)],
            Phase.Event => PendingEvent == EventKind.TreasureHunt ? [new Move(MoveKind.Confirm)] : [new Move(MoveKind.Confirm), new Move(MoveKind.Skip)],
            _ => [],
        };
    }

    public StepResult Apply(Move move)
    {
        if (!Legal().Contains(move)) throw new ArgumentException($"illegal move {move} in phase {Phase}", nameof(move));
        switch (move.Kind)
        {
            case MoveKind.Door:
                Walk(move.Cell);
                break;
            case MoveKind.Consume:
                Bite(move.Cell);
                break;
            case MoveKind.Replace:
                Replace();
                break;
            case MoveKind.Hunt:
                Stalk(move.Cell);
                break;
            case MoveKind.StopChain:
                Chaining = -1;
                break;
            case MoveKind.Instigate:
                Instigate();
                break;
            case MoveKind.Bestow:
                Bestow();
                break;
            case MoveKind.Confirm:
            case MoveKind.Skip:
                Resolve(move.Kind == MoveKind.Confirm);
                break;
            case MoveKind.Instrument:
            case MoveKind.Reward:
                Choose(move);
                break;
            case MoveKind.End:
                EndDay();
                break;
        }
        if (move.Kind is MoveKind.Consume or MoveKind.Replace or MoveKind.Hunt or MoveKind.StopChain or MoveKind.Confirm or MoveKind.Skip && Chaining < 0) SpawnEvent(Rules.EventActionChance);
        return new StepResult(true, null);
    }

    internal int Pick(int count) => Chance.Index?.Invoke(count) ?? rng.Next(count);

    internal bool Holds(ToolId tool) => Held.Contains(tool);

    internal void RollDoors()
    {
        var window = Math.Min(Rules.DoorWindow, Cells.Length - 1);
        var count = Math.Max(1, Rules.DoorChoices - Held.Count(tool => tool is ToolId.Forged or ToolId.Whip or ToolId.Stake));
        var steps = Enumerable.Range(1, window).ToArray();
        for (var i = 0; i < steps.Length - 1; i++)
        {
            var j = i + Pick(steps.Length - i);
            (steps[i], steps[j]) = (steps[j], steps[i]);
        }
        Doors = [.. steps.Take(count).Order().Select(step => (Position + step) % Cells.Length)];
    }

    void Walk(int destination)
    {
        var ratway = Ratway;
        var chain = ChainArmed && !ratway;
        Ratway = false;
        var steps = 0;
        if (!ratway)
        {
            steps = (destination - Position + Cells.Length) % Cells.Length;
            for (var step = 1; step <= steps; step++)
            {
                var passed = (Position + step) % Cells.Length;
                if (chain && Locked(passed)) OpenDoor(passed);
            }
            ChainArmed = false;
        }
        Position = destination;
        var fired = chain || destination == Office ? null : Cells[destination].Event ?? (Cells[destination].Marked ? RandomEvent() : null);
        if (Locked(destination))
        {
            OpenDoor(destination);
            OnPatrolOpen(destination);
        }
        else Cells[destination] = Cells[destination] with { Event = null, Marked = false };
        if (!ratway)
        {
            Shackles(steps);
            Progress(ObjectiveKind.Walk, steps);
        }
        SpawnEvent(Rules.EventWalkChance);
        if (destination == Office && Rules.Card == StartCard.Tyranny) Tyranny();
        if (fired is { } kind)
        {
            PendingEvent = kind;
            EventCell = destination;
            Phase = Phase.Event;
            Progress(ObjectiveKind.Incident, 1);
        }
        else Phase = destination == Office ? Phase.Office : Phase.Manage;
    }

    bool Locked(int cell) => cell != Office && !Cells[cell].Open;

    List<int> Inmates() => [.. Enumerable.Range(0, Cells.Length).Where(cell => Cells[cell].Open)];

    List<int> LockedCells() => [.. Enumerable.Range(1, Cells.Length - 1).Where(Locked)];

    List<int> Sample(List<int> items, int count)
    {
        for (var i = 0; i < Math.Min(count, items.Count); i++)
        {
            var j = i + Pick(items.Count - i);
            (items[i], items[j]) = (items[j], items[i]);
        }
        return items.Take(count).ToList();
    }

    bool Roll(int percent) => Pick(100) < percent;

    void Fill(int cell, Cell inmate)
    {
        Cells[cell] = inmate;
        Progress(ObjectiveKind.Doors, 1);
    }

    void Lock(int cell) => Cells[cell] = new Cell(false, Beast.Mammoth, -1, false);

    void SpawnEvent(int percent)
    {
        if (percent <= 0 || !Roll(percent)) return;
        var free = LockedCells().Where(cell => !Cells[cell].HasEvent && !Cells[cell].Markers.HasFlag(Marker.Nest)).ToList();
        if (free.Count == 0) return;
        var chosen = free[Pick(free.Count)];
        Cells[chosen] = Cells[chosen] with { Event = RandomEvent(), Seen = true };
    }

    EventKind? RandomEvent() => Pool.Count > 0 ? Pool[Pick(Pool.Count)] : null;

    internal int Raise(int cell, int gain)
    {
        if (!Cells[cell].Open) return -1;
        var top = Chromatics >= Rules.ChromaticCap ? Tiers.Chromatic - 1 : Tiers.Chromatic;
        var tier = Math.Min(Cells[cell].Tier + gain, top);
        if (tier >= Tiers.Ultimate) Reach();
        if (tier == Tiers.Chromatic)
        {
            Chromatics++;
            Lock(cell);
            for (var pick = 0; pick < (Rules.Card == StartCard.Opulence ? 3 : 1); pick++) Queue.Add(new Offer(MoveKind.Reward, [0, 1, 2, 3]));
        }
        else Cells[cell] = Cells[cell] with { Tier = tier };
        return tier;
    }

    Cell Occupied(Cell cell, int floor)
    {
        var tier = DrawDoorTier(Math.Max(floor, cell.AtLeast));
        return cell with { Tier = tier, Beast = Odds.DrawBeast(rng, tier, Chance.BeastWeights) };
    }

    void OpenDoor(int index, int floor = 0, bool fresh = false)
    {
        var cell = Cells[index];
        if (fresh || cell.Tier < 0 || Holds(ToolId.Hook) && cell.Tier == 0 && !cell.Known)
            cell = Occupied(cell, Math.Max(floor, Holds(ToolId.Hook) ? 1 : 0));
        Fill(index, new Cell(true, cell.Beast, cell.Tier, false));
    }

    int DrawDoorTier(int floor)
    {
        var weights = Chance.DoorTierWeights ?? Odds.DoorTierWeights;
        var allowed = weights.Select((weight, tier) => tier < floor ? 0 : weight).ToArray();
        return Odds.Draw(rng, allowed.Sum() > 0 ? allowed : weights);
    }
}
