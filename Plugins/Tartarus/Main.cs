using System;
using System.Linq;
using System.Numerics;
using EmberHUD;
using EmberHUD.Ui;
using Hexa.NET.ImGui;
using Tartarus.Sim;

namespace Tartarus;

public sealed class Main : Plugin<Settings>
{
    readonly TextStyle _style = TextStyle.Hud();
    readonly SolverWorker _worker = new();
    TartarusState? _state;
    RunTracker? _tracker;
    MapResult? _map;
    Move? _instant;
    int _boardKey;
    int _generation;

    internal InmateLogger Logger { get; } = new();

    public Advice? Advice { get; private set; }

    public override void Tick()
    {
        Game.WantTartarus();
        var state = Game.State.Tartarus;
        _state = state;
        _tracker ??= new RunTracker(Logger.DataDirectory, Logger.Seconds);
        var tracked = _tracker.Update(state);
        Advice = state?.Choice is { Kind: not TartarusChoiceKind.Event } ? Advise(state, tracked) : null;
        Logger.Tick();
    }

    Advice Advise(TartarusState state, TrackedRun tracked)
    {
        var rules = Settings.ToRules();
        var key = BoardKey(state, tracked, rules);
        if (_map is null || key != _boardKey)
        {
            _map = StateMapper.Map(state, tracked, rules);
            _boardKey = key;
            _instant = Instant(_map.Run);
            _generation++;
            _worker.Playouts = Settings.ClampedPlayouts;
            if (_map.Run is { } run) _worker.Submit(run, _generation);
        }
        var (ranked, generation) = _worker.Latest;
        return SimAdvisor.Advise(state, _map, generation == _generation ? ranked : null, Settings, _instant);
    }

    static Move? Instant(Run? run)
    {
        if (run is null) return null;
        try { return Strategy.Choose(run, new Rng(1)); }
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
        key.Add(tracked.SwapUsedToday);
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

    readonly Overlay _overlay = new();
    int _unusedWidth, _unusedHeight;

    static bool GlowCards(TartarusChoice choice, OptionAdvice[] advice)
    {
        var instrument = choice.Kind == TartarusChoiceKind.Instrument;
        var reward = choice.Kind == TartarusChoiceKind.Reward;
        var popup = instrument || reward;
        var doors = choice.Kind == TartarusChoiceKind.Doors;
        if (!popup && !doors && choice.Kind is not (TartarusChoiceKind.Operation or TartarusChoiceKind.Office)) return false;
        var pageName = instrument ? "S16BoardRelicPop_C" : reward ? "S16BoardColoredPop_C" : "S16BoardMain_C";
        if (Game.State.Ui.Panels.FirstOrDefault(panel => panel.Name == pageName).Parts is not { Length: > 0 } parts) return false;
        var onCard = Enumerable.Range(0, choice.Options.Length).Where(i => popup || doors || choice.Options[i].Kind is TartarusOptionKind.Consume or TartarusOptionKind.Replace or TartarusOptionKind.End or TartarusOptionKind.Instigate or TartarusOptionKind.Bestow);
        if (doors) onCard = onCard.OrderBy(i => choice.Options[i].Param).ThenBy(i => choice.Options[i].Cell);
        var cardsShown = parts.Any(part => part.Name.StartsWith("List_S16Board_Bottom_"));
        var picking = !popup && !cardsShown;
        var roomPick = picking && doors;
        if (picking) onCard = onCard.Where(i => choice.Options[i].Kind is TartarusOptionKind.Consume or TartarusOptionKind.Door);
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
        foreach (var i in onCard)
        {
            var name = popup ? $"S16Board{(instrument ? "Relic" : "Colored")}OptCell_{i + 1}" : picking ? $"S16BoardCell_{choice.Options[i].Cell + 1}" : $"{(doors ? "List_S16Board_Bottom_Routes" : "List_S16Board_Bottom_Actions")}[{card++}]";
            foreach (var part in parts)
            {
                if (part.Name != name) continue;
                if (advice[i].Glow) Draw.GlowRect(part.Min, part.Max, advice[i].Top ? GameColors.Positive : GameColors.Gold);
                if (advice[i].Reason.Length > 0 && (!roomPick || advice[i].Glow)) Draw.Text(advice[i].Reason, new Vector2((part.Min.X + part.Max.X) / 2, part.Max.Y + 18), advice[i].Glow ? (advice[i].Top ? GameColors.Positive : GameColors.Gold) : GameColors.TextDim, Align.Center | Align.Middle);
            }
        }
        return picking || cardsShown || popup;
    }

    public override void Render()
    {
        if (!Settings.ShowPanel || Advice is not { } advice || _state is not { Cells: not null, Choice: { Options: not null } choice } state || advice.Options.Length != choice.Options.Length) return;
        var summary = advice.Summary.Split('\n');
        var rows = new (string Left, string Right, bool Glow, bool Top)[choice.Options.Length];
        for (var i = 0; i < rows.Length; i++)
        {
            var option = choice.Options[i];
            var entry = advice.Options[i];
            rows[i] = (Names.Option(option, state, Settings.ReplaceIsExecute), entry.Reason, entry.Glow, entry.Top);
        }

        var cards = Settings.HighlightCards && GlowCards(choice, advice.Options);
        const float Pad = 8;
        const float Gap = 24;
        var width = 0f;
        foreach (var (left, right, _, _) in rows) width = MathF.Max(width, Draw.MeasureText(left, _style).X + Gap + Draw.MeasureText(right, _style).X);
        foreach (var line in summary) width = MathF.Max(width, Draw.MeasureText(line, _style).X);
        var progress = _worker.Progress;
        var progressText = progress is { } p ? $"{p.Done}/{p.Total} sims  {p.Elapsed.TotalSeconds:0.0}s{(p.Finished ? "" : $"  ETA {p.Eta.TotalSeconds:0.0}s")}" : "";
        width = MathF.Max(width, Draw.MeasureText(progressText, _style).X);
        var rowHeight = Draw.RowHeight(_style);
        _overlay.Locked = Settings.Lock;
        _overlay.MoveHeld = Settings.MoveKey.Key != Key.None && Input.IsDown(Settings.MoveKey.Key);
        var origin = new Vector2(Settings.PanelX, Settings.PanelY);
        const float BarHeight = 4;
        var size = new Vector2(width + Pad * 2, rowHeight * (rows.Length + summary.Length + (progress is null ? 0 : 1)) + (progress is null ? 0 : BarHeight + 2) + Pad * 2);
        var (hovered, _, active) = _overlay.Handle("tartarus", ref origin, size, ref Settings.PanelX, ref Settings.PanelY, ref _unusedWidth, 60, 4000, ref _unusedHeight, 0, 0, Overlay.ResizeEdge.None);
        if (hovered || active) Overlay.ClickBlocker("tartarus", origin, size);
        Draw.RectFilled(origin, origin + size, GameColors.LabelBackground, 4);
        if (_overlay.Movable) Overlay.DragHint(origin, origin + size, active, hovered, Overlay.ResizeEdge.None);
        var y = origin.Y + Pad;
        foreach (var (left, right, glow, top) in rows)
        {
            var at = new Vector2(origin.X + Pad, y);
            if (glow && !cards) Draw.GlowRect(at - new Vector2(3, 1), at + new Vector2(width + 3, rowHeight - 1), top ? GameColors.Positive : GameColors.Gold);
            Draw.Row(at, width, left, right, _style);
            y += rowHeight;
        }
        var footer = _style.Clone();
        for (var i = 0; i < summary.Length; i++)
        {
            var warn = i < summary.Length - 1 || summary[i].StartsWith("mismatch") || summary[i].StartsWith("unsure");
            footer.Color = warn ? GameColors.Gold : GameColors.TextDim;
            Draw.Text(summary[i], new Vector2(origin.X + Pad, y), footer);
            y += rowHeight;
        }
        if (progress is not { } shown) return;
        footer.Color = GameColors.TextDim;
        Draw.Text(progressText, new Vector2(origin.X + Pad, y), footer);
        y += rowHeight + 2;
        var barStart = new Vector2(origin.X + Pad, y);
        Draw.RectFilled(barStart, barStart + new Vector2(width, BarHeight), System.Drawing.Color.FromArgb(60, GameColors.TextDim), 2);
        Draw.RectFilled(barStart, barStart + new Vector2(width * shown.Done / Math.Max(1, shown.Total), BarHeight), shown.Finished ? GameColors.Positive : GameColors.Gold, 2);
    }
}
