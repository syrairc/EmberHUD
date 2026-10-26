using System;
using System.Collections.Generic;
using System.Linq;
using EmberHUD;
using Tartarus.Sim;

namespace Tartarus;

public sealed record MapResult(Run? Run, string[] Assumed, string? Mismatch, string? Unsupported = null);

public static class StateMapper
{
    static readonly Dictionary<int, ToolId> Tools = new()
    {
        [101] = ToolId.Muzzle,
        [102] = ToolId.Ravenous,
        [103] = ToolId.Collar,
        [104] = ToolId.Revenge,
        [105] = ToolId.Cage,
        [106] = ToolId.Whip,
        [107] = ToolId.Saw,
        [111] = ToolId.Blacklist,
        [112] = ToolId.Keys,
        [113] = ToolId.Coin,
        [114] = ToolId.Fire,
        [115] = ToolId.DogTag,
        [116] = ToolId.Forged,
        [117] = ToolId.Hook,
        [121] = ToolId.Scapegoat,
        [122] = ToolId.Axe,
        [123] = ToolId.Maiden,
        [124] = ToolId.Scales,
        [125] = ToolId.Rope,
        [126] = ToolId.Stake,
        [127] = ToolId.Pestle,
        [131] = ToolId.ShortChain,
        [132] = ToolId.Shackles,
        [141] = ToolId.Tongs,
        [142] = ToolId.Maul,
        [143] = ToolId.Trap,
        [144] = ToolId.Crown,
        [145] = ToolId.Claws,
        [146] = ToolId.Guillotine,
        [147] = ToolId.IronBox,
        [148] = ToolId.Skewer,
    };

    static ObjectiveKind? ObjectiveOf(int taskId) => taskId switch
    {
        1 => ObjectiveKind.King,
        2 => ObjectiveKind.Consume,
        3 => ObjectiveKind.Doors,
        4 => ObjectiveKind.Incident,
        5 => ObjectiveKind.Walk,
        _ => null,
    };

    static readonly Dictionary<int, EventKind> Events = new()
    {
        [201] = EventKind.HighRiskConvict,
        [202] = EventKind.TasteOfFreedom,
        [203] = EventKind.BehindClosedDoors,
        [211] = EventKind.RavenousAppetite,
        [212] = EventKind.ExtraPortion,
        [213] = EventKind.SharedFortune,
        [221] = EventKind.Archenemy,
        [222] = EventKind.Execution,
        [231] = EventKind.CrowWhispers,
        [232] = EventKind.TreasureHunt,
        [241] = EventKind.SewerRatway,
        [242] = EventKind.ChainReaction,
        [251] = EventKind.TransferOfConvicts,
        [252] = EventKind.UsurpationI,
        [253] = EventKind.UsurpationII,
        [254] = EventKind.UsurpationIII,
        [255] = EventKind.UsurpationIV,
    };

    static StartCard CardOf(int[]? themes, List<string> assumed)
    {
        var card = StartCard.None;
        foreach (var theme in themes ?? [])
        {
            StartCard? known = theme switch { 301 => StartCard.Tyranny, 302 => StartCard.Opulence, 303 => StartCard.IronLaw, 304 => StartCard.CageFight, _ => null };
            if (known is { } found) card = found;
            else assumed.Add($"Start card {theme} is not known to the simulator, so it is left out.");
        }
        return card;
    }

    internal static int IdOf(EventKind kind) => Events.FirstOrDefault(pair => pair.Value == kind).Key;

    internal static ToolId? ToolOf(int relicId) => Tools.TryGetValue(relicId, out var tool) ? tool : null;

    public static MapResult Map(TartarusState state, TrackedRun tracked, Rules rules)
    {
        var assumed = tracked?.Assumed.ToList() ?? [];
        var problem = Check(state);
        if (problem is not null || tracked is null) return new MapResult(null, [.. assumed], problem ?? "No tracked run.");

        var card = CardOf(state.Themes, assumed);
        assumed.AddRange(["Scratch marks are assumed to be none.", "Rat nests and prey marks are assumed none, and a marked cell's event is drawn from the 14 the game has.", "Tool uses left are assumed to be the defaults."]);
        var run = new Run(rules with { Days = state.MaxDay, RingSize = state.Size, ReplaceAfterConsume = true, Card = card }, new Rng(1)) { Day = state.Day, Position = state.Position, Chromatics = state.Chromatics };
        run.Replaced = tracked.SwapUsedToday;
        foreach (var id in (tracked.ToolsHeld ?? []).Union(state.Instruments).Order())
        {
            if (Tools.TryGetValue(id, out var tool)) run.Held.Add(tool);
            else assumed.Add($"Tool {id} is not known to the simulator, so it is left out.");
        }
        foreach (var task in state.Tasks ?? [])
        {
            if (ObjectiveOf(task.Id) is { } kind) run.Objective = new Objective(kind, task.Progress, task.Completed);
            else assumed.Add($"Objective {task.Id} is not known to the simulator, so it is left out.");
        }
        var rarityOnly = false;
        var speciesOnly = false;
        foreach (var cell in state.Cells.Where(cell => cell.Index != 0))
        {
            var beastKnown = cell.Beast != TartarusBeast.Unknown;
            rarityOnly |= !cell.Open && cell.Tier >= 0 && !beastKnown;
            speciesOnly |= !cell.Open && cell.Tier < 0 && beastKnown;
            run.Cells[cell.Index] = cell.Open
                ? new Cell(true, (Beast)(cell.Beast - 1), cell.Tier, false) { Markers = cell.Tusk ? Marker.Tusk : Marker.None }
                : new Cell(false, beastKnown ? (Beast)(cell.Beast - 1) : Beast.Mammoth, beastKnown ? Math.Min(cell.Tier, Tiers.Ultimate) : -1, cell.Tier >= 0 && beastKnown, tracked.SeenCells?.Contains(cell.Index) == true, beastKnown ? 0 : Math.Clamp(cell.Tier, 0, Tiers.Ultimate));
        }
        if (speciesOnly) assumed.Add("A locked cell with only its species shown is treated as any species.");
        if (rarityOnly) assumed.Add("A locked cell with only its rarity shown is treated as that rarity or better.");

        run.Pool.AddRange([EventKind.SharedFortune, EventKind.Archenemy, EventKind.TransferOfConvicts]);
        Usurp(run, tracked.Usurped - (state.Choice is { Kind: TartarusChoiceKind.Event } now && now.Options.Any(option => option.Param == tracked.Usurped) ? 1 : 0));
        var marked = state.Cells.Where(cell => cell.EventMarker && cell.Index != 0).ToArray();
        foreach (var cell in marked) run.Cells[cell.Index].Marked = true;
        if (marked.Any(cell => cell.Index == tracked.UsurpCell) && Events.TryGetValue(tracked.UsurpNext, out var usurpation)) run.Cells[tracked.UsurpCell].Event = usurpation;
        if (marked.Length > 0) assumed.Add("Event markers: the event behind a marked cell is unknown, so a random one is assumed when it is reached.");
        if (tracked.ChainArmedAt >= 0 && state.Choice?.Kind != TartarusChoiceKind.Event) run.ChainArmed = true;
        var refused = ApplyChoice(state.Choice!, state, run, assumed, out var unsupported);
        if (refused is not null || unsupported is not null) return new MapResult(null, [.. assumed.Distinct()], refused, unsupported);
        var disagreement = Devour(state, run, assumed);
        return new MapResult(disagreement is null ? run : null, [.. assumed.Distinct()], disagreement);
    }

    // ponytail: the game adds Usurpation II-IV (weight 0) only when the level before resolves, so the pool follows the highest one seen
    static void Usurp(Run run, int resolved)
    {
        if (resolved is < 252 or > 255) return;
        run.Pool.RemoveAll(kind => kind is EventKind.UsurpationI or EventKind.UsurpationII or EventKind.UsurpationIII or EventKind.UsurpationIV);
        if (resolved < 255) run.Pool.Add(Events[resolved + 1]);
    }

    static string? Check(TartarusState state)
    {
        if (state is not { Cells: not null, Instruments: not null }) return "The board has not been read.";
        if (state.Size is < 2 or > 64 || state.Cells.Length != state.Size) return "The board size and its cells do not match.";
        if (state.MaxDay is < 1 or > 60) return $"The run length of {state.MaxDay} days is not supported.";
        if (state.Day < 1 || state.Position < 0 || state.Position >= state.Size) return "The day or position is out of range.";
        if (state.Choice is not { Complete: true, Options: not null }) return "The choice is not fully read.";
        if (state.Cells.Select(cell => cell.Index).Distinct().Count() != state.Size || state.Cells.Any(cell => cell.Index < 0 || cell.Index >= state.Size)) return "The cell numbers are not 1 to the board size.";
        foreach (var cell in state.Cells)
        {
            if (!cell.Open) continue;
            if (cell.Beast is < TartarusBeast.Mammoth or > TartarusBeast.Rat && cell.Index != 0) return $"The beast in cell {cell.Index + 1} is unknown.";
            if (cell.Tier is < 0 or > Tiers.Chromatic && cell.Index != 0) return $"The tier of cell {cell.Index + 1} is unknown.";
        }
        return null;
    }

    static string? ApplyChoice(TartarusChoice choice, TartarusState state, Run run, List<string> assumed, out string? unsupported)
    {
        unsupported = null;
        switch (choice.Kind)
        {
            case TartarusChoiceKind.Doors:
                var doors = choice.Options.Where(option => option.Kind == TartarusOptionKind.Door).Select(option => option.Cell).ToArray();
                if (doors.Length == 0 || doors.Any(door => door < 0 || door >= state.Size)) return "The doors for today are not readable.";
                run.Phase = Sim.Phase.Patrol;
                run.Doors = doors;
                run.Ratway = doors.Length >= state.Size - 1;
                return null;
            case TartarusChoiceKind.Operation:
                run.Phase = Sim.Phase.Manage;
                if (choice.Options.Any(option => option.Kind == TartarusOptionKind.StopChain))
                {
                    run.Chaining = state.Position;
                    run.Consumed = true;
                }
                return run.Cells[state.Position] is { Open: true } && state.Position != 0 ? null : "The inmate on the current cell is not readable.";
            case TartarusChoiceKind.Office:
                run.Phase = Sim.Phase.Office;
                return null;
            case TartarusChoiceKind.Instrument:
                var tools = choice.Options.Where(option => option.Kind == TartarusOptionKind.Instrument).Select(option => (option.Param, Tool: ToolOf(option.Param))).ToArray();
                assumed.AddRange(tools.Where(pick => pick.Tool is null).Select(pick => $"Tool {pick.Param} is not known to the simulator, so it is left out of the ranking."));
                if (tools.All(pick => pick.Tool is null)) unsupported = "None of the offered tools is known to the simulator yet.";
                else
                {
                    var fresh = run.Day == 1 && state.Cells.All(cell => !cell.Open || cell.Index == 0);
                    run.Phase = fresh ? Sim.Phase.Patrol : Sim.Phase.Manage;
                    if (!fresh) run.Consumed = run.Replaced = true;
                    run.Queue.Add(new Offer(MoveKind.Instrument, [.. tools.Where(pick => pick.Tool is not null).Select(pick => (int)pick.Tool!.Value)]));
                    if (!fresh) assumed.Add("The part of the day left after the tool pick is assumed to be just ending it.");
                }
                return null;
            case TartarusChoiceKind.Reward:
                var offered = choice.Options.Where(option => option.Kind == TartarusOptionKind.Reward).Select(option => option.Param).ToArray();
                assumed.AddRange(offered.Where(id => id is < 1 or > 4).Select(id => $"Reward {id} is not known to the simulator, so it is left out of the ranking."));
                var rewards = offered.Where(id => id is >= 1 and <= 4).Select(id => id - 1).ToArray();
                if (rewards.Length == 0) unsupported = "None of the offered rewards is known to the simulator yet.";
                else
                {
                    run.Phase = Sim.Phase.Manage;
                    run.Consumed = run.Replaced = true;
                    run.Queue.Add(new Offer(MoveKind.Reward, rewards));
                    assumed.Add("The part of the day left after the reward is assumed to be just ending it.");
                }
                return null;
            case TartarusChoiceKind.Event:
                var confirm = choice.Options.FirstOrDefault(option => option.Kind == TartarusOptionKind.Confirm);
                if (confirm.Kind != TartarusOptionKind.Confirm || !Events.TryGetValue(confirm.Param, out var happening))
                {
                    unsupported = $"Event {confirm.Param} is not simulated yet.";
                    return null;
                }
                run.Phase = Sim.Phase.Event;
                run.PendingEvent = happening;
                run.EventCell = state.Position;
                return null;
            default:
                unsupported = $"A {choice.Kind} choice is not simulated yet.";
                return null;
        }
    }
    static string? Devour(TartarusState state, Run run, List<string> assumed)
    {
        if (state.Choice!.Kind != TartarusChoiceKind.Operation) return null;
        var hunters = Enumerable.Range(1, run.Cells.Length - 1).Where(cell => run.Cells[cell].Open && run.Meals(cell).Count > 0).ToHashSet();
        var marked = state.Cells.Where(cell => cell.DevourTarget).Select(cell => cell.Index).ToHashSet();
        hunters.SymmetricExceptWith(marked);
        if (hunters.Count > 0) return $"The game and the simulator disagree about cell {hunters.Min() + 1} being able to eat.";
        var offered = state.Choice.Options.Where(option => option.Kind == TartarusOptionKind.Consume).Select(option => option.Cell).ToHashSet();
        var simOffered = run.Legal().Where(move => move.Kind == MoveKind.Consume).Select(move => move.Cell).ToHashSet();
        if (offered.SetEquals(simOffered)) return null;
        if (offered.Count == 0 && run.Chaining < 0)
        {
            run.Consumed = true;
            assumed.Add("The game marks an inmate that can eat but offers no devour, so something already ate today.");
            return null;
        }
        var differ = offered.Union(simOffered).Except(offered.Intersect(simOffered)).Order().First();
        return $"The game and the simulator disagree about devouring cell {differ + 1}.";
    }
}
