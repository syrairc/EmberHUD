using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using EmberHUD;
using EmberHUD.Ui;
using Hexa.NET.ImGui;
using Tartarus.Sim;

namespace Tartarus;

public sealed class Main : Plugin<Settings>
{
    readonly TextStyle _arenaLabel = new() { Color = GameColors.HudText, ShadowEnabled = true, Scale = 1.7f };
    readonly TextStyle _arenaScore = new() { Color = GameColors.Gold, Face = FontFace.Bold, ShadowEnabled = true, Scale = 4f };
    readonly TextStyle _arenaWarn = new() { Color = GameColors.Gold, ShadowEnabled = true, Scale = 1.4f };
    readonly SolverWorker _worker = new();
    TartarusState? _state;
    RunTracker? _tracker;
    MapResult? _map;
    Move? _instant;
    (int Cell, string Name)[] _eventLabels = [];
    int _boardKey;
    int _generation;
    DecisionLog? _decisions;
    RunDataLog? _runData;
    int? _seenKey;
    int _rankedLogged;
    string _adviceSignature = "";

    internal InmateLogger Logger { get; } = new();

    internal Action<string>? Sink { get; set; }

    public Advice? Advice { get; private set; }

    public override void Tick()
    {
        Game.WantTartarus();
        if (Settings.LogRunData) Game.WantTartarusRaw();
        var state = Game.State.Tartarus;
        _state = state;
        if (Settings.LogRunData) (_runData ??= new(Path.Combine(Logger.DataDirectory, "runs"))).Write(state?.Raw);
        _tracker ??= new RunTracker(Logger.DataDirectory, Logger.Seconds);
        var tracked = _tracker.Update(state);
        LogSeen(state, tracked);
        Advice = state?.Choice is { Kind: not TartarusChoiceKind.Event } ? Advise(state, tracked) : null;
        LogAdvice();
        Logger.Tick();
    }

    void Record(string kind, Func<object?> data)
    {
        if (!Settings.LogDecisions) return;
        (_decisions ??= new(Sink)).Write(kind, data);
    }

    void LogSeen(TartarusState? state, TrackedRun tracked)
    {
        var key = state is null ? 0 : BoardKey(state, tracked, Settings.ToRules());
        if (key == _seenKey) return;
        _seenKey = key;
        Record("state", () => new { state, tracked.SwapUsedToday });
    }

    void LogAdvice()
    {
        var advice = Advice;
        var signature = advice is null ? "" : $"{advice.Summary}|{advice.Source}|{string.Join(',', advice.Options.Select(option => $"{option.OptionId}{(option.Glow ? 'g' : '-')}{(option.Top ? 't' : '-')}{option.Reason}"))}";
        if (signature == _adviceSignature) return;
        _adviceSignature = signature;
        Record("advice", () => new { generation = _generation, mode = Settings.Mode, advice });
    }

    Advice Advise(TartarusState state, TrackedRun tracked)
    {
        var rules = Settings.ToRules();
        var key = BoardKey(state, tracked, rules);
        if (_map is null || key != _boardKey)
        {
            _map = StateMapper.Map(state, tracked, rules);
            if (_map.Run is { } mapped) mapped.Chance = Registry.ByName(Settings.Mode)?.Chance ?? new();
            _boardKey = key;
            _instant = Instant(_map.Run);
            UpdateNotes(state, _map.Run);
            _eventLabels = KnownEvents(_map.Run);
            _generation++;
            _worker.Playouts = Settings.ClampedPlayouts;
            if (_map.Run is { } run) _worker.Submit(run, _generation);
            Record("map", () => new { generation = _generation, mode = Settings.Mode, playouts = Settings.ClampedPlayouts, rules, _map.Mismatch, _map.Unsupported, _map.Assumed, legal = _map.Run?.Legal(), instant = _instant });
        }
        var (ranked, generation) = _worker.Latest;
        if (ranked is not null && generation == _generation && _rankedLogged != generation)
        {
            _rankedLogged = generation;
            Record("ranked", () => new { generation, elapsedMs = _worker.Progress?.Elapsed.TotalMilliseconds, ranked = ranked.Take(10) });
        }
        return SimAdvisor.Advise(state, _map, generation == _generation ? ranked : null, Settings, _instant);
    }

    static Move? Instant(Run? run)
    {
        if (run is null) return null;
        try { return Policy.Choose(run, new Rng(1)); }
        catch (Exception error)
        {
            Log.Error(error, "tartarus instant pick");
            return null;
        }
    }

    int BoardKey(TartarusState state, TrackedRun tracked, Rules rules)
    {
        var key = new HashCode();
        key.Add(rules);
        key.Add(Settings.ClampedPlayouts);
        key.Add(Settings.Mode);
        key.Add(tracked.SwapUsedToday);
        key.Add(tracked.UsurpCell);
        key.Add(tracked.UsurpNext);
        key.Add(state.Position);
        key.Add(state.Size);
        key.Add(state.Day);
        key.Add(state.MaxDay);
        key.Add(state.Chromatics);
        key.Add(state.Choice?.Id);
        key.Add(state.Choice?.Kind);
        key.Add(state.Choice?.Complete);
        foreach (var cell in state.Cells ?? []) key.Add(cell);
        foreach (var instrument in state.Instruments ?? []) key.Add(instrument);
        foreach (var task in state.Tasks ?? []) key.Add(task);
        foreach (var option in state.Choice?.Options ?? []) key.Add(option);
        return key.ToHashCode();
    }

    public override void Dispose()
    {
        _worker.Dispose();
        _tracker?.Dispose();
        Logger.Dispose();
        base.Dispose();
    }

    public override void DrawSettings() => Layout.TabBar("tartarus",
        ("Panel", () => SettingsUI.DrawPanel(Settings)),
        ("Advice", () => SettingsUI.DrawAdvice(Settings)),
        ("Rules", () => SettingsUI.DrawRules(Settings)));


    internal static string[] TreasureLines(int rewardId, TreasurePreview preview) => rewardId switch
    {
        1 => [$"Opened {preview.Opened}   Unopened {preview.Unopened}"],
        2 => [$"{preview.Hungry} can consume"],
        3 => [$"Normal to Magic: {preview.Upgrades[0]}", $"Magic to Rare: {preview.Upgrades[1]}", $"Rare to Legendary: {preview.Upgrades[2]}"],
        4 => [$"{preview.Feedable} of {preview.Bare} without a target can get one"],
        _ => [],
    };

    static readonly (int Relic, string Name, int Percent, bool ChainOnly)[] ConsumeProcs = [(107, "Saw", 25, false), (106, "Whip", 35, false), (103, "Collar", 80, true)];

    static readonly string[] TierNames = ["Normal", "Magic", "Rare", "Legendary", "Ultimate"];

    internal sealed record CardNote(string[] Center, string[] Left, string[] Right)
    {
        public static readonly CardNote None = new([], [], []);
    }

    internal static string[] ChainLines(double[] reached) =>
        [.. reached.Take(5).Select((odds, i) => $"Chain {i + 1}{(i == 4 && reached.Length > 5 ? "+" : "")}: {odds:0%}")];

    internal static string[] ReplaceLines(double[] odds) =>
        [.. odds.Select((chance, tier) => (chance, tier)).Where(entry => entry.chance > 0).Select(entry => $"{TierNames[entry.tier]} {entry.chance:0.#%}")];

    internal static string[] ProcLines(IEnumerable<int> held, int eaterTier, bool chaining) =>
        eaterTier is < 0 or >= Tiers.Ultimate ? [] : [.. ConsumeProcs.Where(proc => held.Contains(proc.Relic)).Select(proc => $"{proc.Name} +1: {proc.Percent}%{(proc.ChainOnly && !chaining ? " chain" : "")}")];

    (TreasurePreview? Preview, Dictionary<int, double[]> Chain, double[] Replace, string[] Procs, Dictionary<int, int> Becomes) _notes = (null, [], [], [], []);

    void UpdateNotes(TartarusState state, Run? run)
    {
        _notes = (null, [], [], [], []);
        if (run is null || state.Choice is not { } choice) return;
        try
        {
            if (choice.Kind == TartarusChoiceKind.Reward) _notes = (run.PreviewTreasure(), [], [], [], []);
            if (choice.Kind != TartarusChoiceKind.Operation) return;
            var eater = run.Cells[run.Chaining >= 0 ? run.Chaining : run.Position];
            _notes = (null, run.ChainOddsByMeal(), run.ReplaceOdds(), ProcLines(state.Instruments ?? [], eater.Open ? eater.Tier : -1, run.Chaining >= 0), run.Legal().Where(move => move.Kind == MoveKind.Consume).ToDictionary(move => move.Cell, move => run.MealTier(move.Cell)));
        }
        catch (Exception error)
        {
            Log.Error(error, "tartarus card notes");
        }
    }

    internal static string AgainLine(double[] reached, int becomes) => becomes >= Tiers.Chromatic ? "Becomes Chromatic" : $"{(becomes >= 0 && becomes < TierNames.Length ? TierNames[becomes] + ", " : "")}{(reached.Length > 1 ? reached[1] : 0):0%} chain";

    CardNote Notes(TartarusOption option, bool picking)
    {
        if (_notes.Preview is { } preview) return new(TreasureLines(option.Param, preview), [], []);
        if (option.Kind == TartarusOptionKind.StopChain) return picking ? new(["Stop chain"], [], []) : CardNote.None;
        if (option.Kind == TartarusOptionKind.Consume && picking) return _notes.Chain.TryGetValue(option.Cell, out var reached) ? new([AgainLine(reached, _notes.Becomes.GetValueOrDefault(option.Cell, -1))], [], []) : CardNote.None;
        if (option.Kind == TartarusOptionKind.Consume) return new([], ChainLines(Run.BestChain(_notes.Chain)), _notes.Procs);
        if (option.Kind != TartarusOptionKind.Replace || picking) return CardNote.None;
        var lines = ReplaceLines(_notes.Replace);
        var half = (lines.Length + 1) / 2;
        return new([], lines[..half], [.. lines[half..], "(est.)"]);
    }

    static (int Cell, string Name)[] KnownEvents(Run? run) => run is null ? [] :
        [.. Enumerable.Range(0, run.Cells.Length).Where(cell => run.Cells[cell].Event is not null).Select(cell => (cell, Names.Event(StateMapper.IdOf(run.Cells[cell].Event!.Value))))];

    void DrawEventLabels()
    {
        if (_eventLabels.Length == 0 || Game.State.Ui.Panels.FirstOrDefault(panel => panel.Name == "S16BoardMain_C").Parts is not { Length: > 0 } parts) return;
        foreach (var (cell, name) in _eventLabels)
            foreach (var part in parts.Where(part => part.Name == $"S16BoardCell_{cell + 1}"))
                Draw.Text(name, new Vector2((part.Min.X + part.Max.X) / 2, part.Max.Y - 34), GameColors.Gold, Align.Center, 22);
    }

    static (Vector2 Min, Vector2 Max)? Arena()
    {
        if (Game.State.Ui.Panels.FirstOrDefault(panel => panel.Name == "S16BoardMain_C").Parts is not { Length: > 0 } parts) return null;
        float Edge(int cell, Func<NamedRect, float> side) => parts.FirstOrDefault(part => part.Name == $"S16BoardCell_{cell}") is { Name: not null } part ? side(part) : float.NaN;
        var min = new Vector2(Edge(16, part => part.Max.X), Edge(4, part => part.Max.Y));
        var max = new Vector2(Edge(7, part => part.Min.X), Edge(11, part => part.Min.Y));
        return max.X - min.X > 200 && max.Y - min.Y > 100 ? (min, max) : null;
    }

    void DrawArena(Advice advice, Vector2 min, Vector2 max)
    {
        const float BarWidth = 480, BarHeight = 8;
        var centre = (min.X + max.X) / 2;
        var y = min.Y + (max.Y - min.Y) * 0.22f;
        var best = advice.Options.Where(option => option.Reason.Length > 0).OrderByDescending(option => option.Top).ThenByDescending(option => option.Value.Low + option.Value.High).FirstOrDefault();
        y += Draw.Text("Predicted score", new Vector2(centre, y), _arenaLabel, Align.Center).Y;
        var score = best is null ? "..." : ((best.Value.Low + best.Value.High) / 2).ToString("0.00", CultureInfo.InvariantCulture);
        y += Draw.Text(score, new Vector2(centre, y), _arenaScore, Align.Center).Y + 14;
        if (_worker.Progress is { } progress)
        {
            if (progress.Finished) y += Draw.Text($"{progress.Total} sims", new Vector2(centre, y), _arenaLabel, Align.Center).Y + 8;
            else
            {
                Draw.Bar(new Vector2(centre - BarWidth / 2, y + 6), new Vector2(centre + BarWidth / 2, y + 6 + BarHeight), (float)progress.Done / Math.Max(1, progress.Total), GameColors.Gold, System.Drawing.Color.FromArgb(90, GameColors.TextDim));
                y += BarHeight + 20;
            }
        }
        var core = advice.Summary.Split('\n')[^1];
        if (core is not ("thinking" or "only option") && !core.EndsWith(" best")) Draw.Text(core, new Vector2(centre, y), _arenaWarn, Align.Center);
    }

    static void DrawNotes(CardNote note, NamedRect card, bool popup, bool picking)
    {
        const float Size = 24, Side = 22, Top = 18;
        var height = Draw.MeasureText("Ag", Size).Y + 2;
        var y = picking ? card.Min.Y + 4 : card.Max.Y - (popup ? 140 : 8) - height * note.Center.Length;
        foreach (var line in note.Center)
        {
            Draw.Text(line, new Vector2((card.Min.X + card.Max.X) / 2, y), GameColors.Gold, Align.Center, Size);
            y += height;
        }
        for (var i = 0; i < note.Left.Length; i++) Draw.Text(note.Left[i], new Vector2(card.Min.X + Side, card.Min.Y + Top + i * height), GameColors.Gold, Align.TopLeft, Size);
        for (var i = 0; i < note.Right.Length; i++) Draw.Text(note.Right[i], new Vector2(card.Max.X - Side, card.Min.Y + Top + i * height), GameColors.Gold, Align.Right, Size);
    }

    // ponytail: the card frame's corner caps cut in about 9% of its width, eyeballed
    // ponytail: a route card's frame top sits about 12 of 208 px above Img_S16Board_RouteCell_Bg, eyeballed
    const float RouteRaise = 0.06f;

    const float CardCorner = 0.09f;

    // ponytail: the inmate portrait's shield as fractions of Img_S16Board_Cell_MonsterPic, eyeballed
    static readonly Vector2[] ShieldShape = [new(0.08f, 0), new(0.92f, 0), new(1, 0.07f), new(1, 0.8f), new(0.5f, 1), new(0, 0.8f), new(0, 0.07f)];

    const float ShieldInset = 0.08f;

    static Vector2[] Shield(NamedRect portrait)
    {
        var inset = (portrait.Max.X - portrait.Min.X) * ShieldInset;
        var min = portrait.Min + new Vector2(inset);
        var size = portrait.Max - portrait.Min - new Vector2(inset * 2);
        return [.. ShieldShape.Select(at => min + size * at)];
    }

    // ponytail: where a card frame's bars sit inside its image, as fractions (top / bottom of the height, the corner cuts of the width), eyeballed live
    readonly record struct FrameShape(float Top, float TopCut, float Bottom, float BottomCut);

    static readonly FrameShape TreasureFrame = new(0.029f, 0.075f, 0.032f, 0.09f);

    // ponytail: the instrument frame traced from a live screenshot of Img_S16Board_RelicOptCell_Bg (616 x 940): sides and corner cap diagonals, flat across the cap tops and bottoms
    static readonly Vector2[] RelicFrame =
    [
        new(0, 0.068f), new(0.109f, 0.012f), new(0.891f, 0.012f), new(1, 0.068f),
        new(1, 0.931f), new(0.9f, 0.985f), new(0.1f, 0.985f), new(0, 0.931f),
    ];

    static Vector2[] Traced(NamedRect frame, Vector2[] shape) => [.. shape.Select(at => frame.Min + (frame.Max - frame.Min) * at)];

    static Vector2[] CardFrame(NamedRect frame, FrameShape shape)
    {
        var (min, max) = (frame.Min, frame.Max);
        var (width, height) = (max.X - min.X, max.Y - min.Y);
        var (top, bottom) = (min.Y + height * shape.Top, max.Y - height * shape.Bottom);
        var (topCut, bottomCut) = (width * shape.TopCut, width * shape.BottomCut);
        return
        [
            new(min.X, top + topCut), new(min.X + topCut, top), new(max.X - topCut, top), new(max.X, top + topCut),
            new(max.X, bottom - bottomCut), new(max.X - bottomCut, bottom), new(min.X + bottomCut, bottom), new(min.X, bottom - bottomCut),
        ];
    }

    static bool GlowCards(TartarusChoice choice, OptionAdvice[] advice, Func<TartarusOption, bool, CardNote> notes)
    {
        var instrument = choice.Kind == TartarusChoiceKind.Instrument;
        var reward = choice.Kind == TartarusChoiceKind.Reward;
        var popup = instrument || reward;
        var doors = choice.Kind == TartarusChoiceKind.Doors;
        if (!popup && !doors && choice.Kind is not (TartarusChoiceKind.Operation or TartarusChoiceKind.Office)) return false;
        var pageName = instrument ? "S16BoardRelicPop_C" : reward ? "S16BoardColoredPop_C" : "S16BoardMain_C";
        if (Game.State.Ui.Panels.FirstOrDefault(panel => panel.Name == pageName).Parts is not { Length: > 0 } parts) return false;
        var onCard = Enumerable.Range(0, choice.Options.Length).Where(i => popup || doors || choice.Options[i].Kind is TartarusOptionKind.Consume or TartarusOptionKind.Replace or TartarusOptionKind.End or TartarusOptionKind.StopChain or TartarusOptionKind.Instigate or TartarusOptionKind.Bestow);
        if (doors) onCard = onCard.OrderBy(i => choice.Options[i].Param).ThenBy(i => choice.Options[i].Cell);
        var cardsShown = parts.Any(part => part.Name.StartsWith("List_S16Board_Bottom_"));
        var picking = !popup && !cardsShown;
        var roomPick = picking && doors;
        if (picking) onCard = onCard.Where(i => choice.Options[i].Kind is TartarusOptionKind.Consume or TartarusOptionKind.Door or TartarusOptionKind.StopChain);
        else if (!popup && !doors)
        {
            var consumes = onCard.Where(i => choice.Options[i].Kind == TartarusOptionKind.Consume).ToList();
            if (consumes.Count > 1)
            {
                var shown = consumes.OrderByDescending(i => advice[i].Top).ThenByDescending(i => advice[i].Glow).ThenByDescending(i => advice[i].Value.Low + advice[i].Value.High).First();
                onCard = onCard.Where(i => choice.Options[i].Kind != TartarusOptionKind.Consume || i == shown).ToList();
            }
        }
        var card = 0;
        var pulse = (float)(0.5 + 0.5 * Math.Sin(Environment.TickCount64 / 1000.0 * 2 * Math.PI * 1.2));
        foreach (var i in onCard)
        {
            var name = popup ? $"S16Board{(instrument ? "Relic" : "Colored")}OptCell_{i + 1}" : picking ? choice.Options[i].Kind == TartarusOptionKind.StopChain ? "Btn_S16Board_Bottom_Confirm" : $"S16BoardCell_{choice.Options[i].Cell + 1}" : $"{(doors ? "List_S16Board_Bottom_Routes" : "List_S16Board_Bottom_Actions")}[{card++}]";
            foreach (var part in parts)
            {
                if (part.Name != name) continue;
                var frameName = instrument ? $"{name}/Img_S16Board_RelicOptCell_Bg" : reward ? $"{name}/Img_S16Board_ColoredOptCell_Bg_1" : name.StartsWith("List_S16Board_Bottom_Actions") ? $"{name}/Img_S16Board_ActionCell_Bg" : name.StartsWith("List_S16Board_Bottom_Routes") ? $"{name}/Img_S16Board_RouteCell_Bg" : picking && choice.Options[i].Kind == TartarusOptionKind.Consume ? $"{name}/Img_S16Board_Cell_MonsterPic" : null;
                var frame = frameName is null ? default : parts.FirstOrDefault(other => other.Name == frameName);
                var edge = frame.Name is null ? part : doors && !picking ? frame with { Min = frame.Min - new Vector2(0, (frame.Max.Y - frame.Min.Y) * RouteRaise) } : frame;
                DrawNotes(notes(choice.Options[i], picking), popup || picking ? part : edge, popup, picking);
                var glow = advice[i].Top ? GameColors.Positive : GameColors.Gold;
                if (advice[i].Glow && picking && frame.Name is not null) Draw.GlowPolygon(Shield(edge), glow, 2 + pulse, 4 + pulse * 6);
                else if (advice[i].Glow && popup && frame.Name is not null) Draw.GlowPolygon(instrument ? Traced(edge, RelicFrame) : CardFrame(edge, TreasureFrame), glow);
                else if (advice[i].Glow) Draw.GlowRect(edge.Min, edge.Max, glow, corner: popup && frame.Name is not null ? (edge.Max.X - edge.Min.X) * CardCorner : 0);
                if (advice[i].Reason.Length > 0 && (!roomPick || advice[i].Glow)) Draw.Text(advice[i].Reason, new Vector2((edge.Min.X + edge.Max.X) / 2, edge.Max.Y + 18), advice[i].Glow ? (advice[i].Top ? GameColors.Positive : GameColors.Gold) : GameColors.TextDim, Align.Center | Align.Middle);
            }
        }
        return picking || cardsShown || popup;
    }

    public override void Render()
    {
        if (!Settings.ShowPanel || Advice is not { } advice || _state is not { Cells: not null, Choice: { Options: not null } choice } || advice.Options.Length != choice.Options.Length) return;
        var alone = Game.State.Ui.Panels is [{ Name: "S16BoardMain_C" }];
        if (Settings.HighlightCards && (alone || choice.Kind is TartarusChoiceKind.Instrument or TartarusChoiceKind.Reward)) GlowCards(choice, advice.Options, Notes);
        if (!alone) return;
        if (Settings.HighlightCards) DrawEventLabels();
        if (Arena() is { } arena) DrawArena(advice, arena.Min, arena.Max);
    }
}
