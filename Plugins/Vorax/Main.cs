using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using EmberHUD;
using EmberHUD.Ui;
using Vorax.Sim;

namespace Vorax;

public sealed class Main : Plugin<Settings>
{
    const float Pad = 8;
    const float Width = 560;
    const string GamePanelName = "S13GamePlayMain_C";
    readonly TextStyle _style = TextStyle.Hud();
    readonly Overlay _overlay = new();
    readonly Overlay _barOverlay = new();
    int _unusedWidth, _unusedHeight;
    readonly Calibration _calibration = new(Log.Info);
    readonly RunTracker _tracker = new();
    readonly RunLog _runLog = new(Log.Info);
    Worker? _worker;
    (int, int, int) _workerKey;

    internal Advice? AdviceOverride;

    internal Worker? CurrentWorker => _worker;

    internal Strategy? StrategyFor(string? mode) => mode == Settings.SimulatorMode ? null : Registry.ByName(mode);

    public override void Tick()
    {
        Game.WantVorax();
        if (!Settings.Enabled) return;
        if (Game.State.Vorax is not { } state)
        {
            _runLog.Observe(null, null);
            return;
        }

        var key = (Settings.ClampedRollouts, Settings.ClampedBudget, Settings.ClampedMargin);
        if (_worker is null || key != _workerKey)
        {
            if (_worker is { } old) Task.Run(old.Dispose);
            _workerKey = key;
            _worker = new(new Engine(new(key.Item1, key.Item2, key.Item3 / 100.0), new Rng((ulong)Environment.TickCount64)));
            _runLog.Config = $"rollouts={key.Item1} budgetMs={key.Item2} marginPct={key.Item3}";
        }

        _worker.Submit(state, StrategyFor(Settings.Mode), _tracker.Update(state));
        _runLog.Observe(state, _worker.Matches(state) ? _worker.Latest : null);
        if (Settings.Debug) _calibration.Observe(state);
    }

    public override void Dispose() => _worker?.Dispose();

    public override void Render()
    {
        if (!Settings.Enabled || Game.State.Vorax is not { } state) return;
        var advice = AdviceOverride ?? (_worker is { } worker && worker.Matches(state) ? worker.Latest : null);
        if (Settings.ShowHighlights) DrawMarks(state, advice);
        if (Settings.ShowBar) DrawBar(state, advice);
        if (Settings.ShowPanel) DrawPanel(state, advice);
    }

    void DrawMarks(VoraxState state, Advice? advice)
    {
        var parts = Game.State.Ui.Panels.FirstOrDefault(panel => panel.Name == GamePanelName).Parts ?? [];
        foreach (var mark in Highlight.Marks(state, advice, parts))
        {
            var colour = mark.Strategy ? Settings.StrategyColour : Settings.SimulatorColour;
            Draw.GlowRect(mark.Min, mark.Max, colour, 3);
            var label = _style.Clone();
            label.Color = colour;
            Draw.Text(mark.Label, new Vector2(mark.Min.X, mark.Min.Y - Draw.RowHeight(_style)), label);
        }
    }

    void DrawBar(VoraxState state, Advice? advice)
    {
        if (state.Offered.Length == 0 || state.Phase == VoraxPhase.Other) return;
        var style = Settings.BarText;
        var rowHeight = Draw.RowHeight(style);
        var barHeight = Math.Max(4f, rowHeight * 0.3f);
        var size = new Vector2(Settings.BarWidth, Pad * 2 + rowHeight + 4 + barHeight);
        _barOverlay.Locked = Settings.Lock;
        _barOverlay.MoveHeld = Settings.MoveKey.Key != Key.None && Input.IsDown(Settings.MoveKey.Key);
        var origin = new Vector2(Settings.BarX, Settings.BarY);
        var (hovered, _, active) = _barOverlay.Handle("voraxbar", ref origin, size, ref Settings.BarX, ref Settings.BarY, ref Settings.BarWidth, 200, 1200);
        if (hovered || active) Overlay.ClickBlocker("voraxbar", origin, size);
        Draw.RectFilled(origin, origin + size, Settings.BarBackground, 4);
        if (_barOverlay.Movable) Overlay.DragHint(origin, origin + size, active, hovered, Overlay.ResizeEdge.Right);

        var right = BarScore(advice);
        var rightWidth = Draw.MeasureText(right, style).X;
        Draw.Text(PickText(state, advice, size.X - Pad * 2 - rightWidth - 16, style), origin + new Vector2(Pad), style);
        Draw.Text(right, origin + new Vector2(size.X - Pad, Pad), style, Align.Right);

        var progress = _worker?.Progress;
        var fraction = progress is { Active: true } ? (float)progress.Fraction : advice is null ? 0f : 1f;
        var top = origin.Y + size.Y - Pad - barHeight;
        Draw.Bar(new Vector2(origin.X + Pad, top), new Vector2(origin.X + size.X - Pad, top + barHeight), fraction, Settings.BarFill, Settings.BarTrack);
    }

    internal static string BarScore(Advice? advice) => advice?.Expected is { } expected ? $"Est. {expected:N0}" : "Est. -";

    internal static string PickText(VoraxState state, Advice? advice, float maxWidth, TextStyle style)
    {
        var text = PickText(state, advice);
        if (Draw.MeasureText(text, style).X <= maxWidth) return text;
        while (text.Length > 1 && Draw.MeasureText(text + "...", style).X > maxWidth) text = text[..^1];
        return text + "...";
    }

    internal static string PickText(VoraxState state, Advice? advice)
    {
        if (advice is null) return "thinking...";
        if (advice.Pick.IsReroll) return "Reroll";
        var indexes = advice.Pick.Indexes ?? [];
        return indexes.Length == 0 ? "-" : string.Join(", ", indexes.Select(i => PickName(state, advice.Stage, i)));
    }

    void DrawPanel(VoraxState state, Advice? advice)
    {
        var rows = PanelRows(state, advice);
        if (rows.Count == 0) return;
        var rowHeight = Draw.RowHeight(_style);
        var size = new Vector2(Width, rowHeight * rows.Count) + new Vector2(Pad * 2);
        _overlay.Locked = Settings.Lock;
        _overlay.MoveHeld = Settings.MoveKey.Key != Key.None && Input.IsDown(Settings.MoveKey.Key);
        var origin = new Vector2(Settings.PanelX, Settings.PanelY);
        var (hovered, _, active) = _overlay.Handle("vorax", ref origin, size, ref Settings.PanelX, ref Settings.PanelY, ref _unusedWidth, 60, 4000, ref _unusedHeight, 0, 0, Overlay.ResizeEdge.None);
        if (hovered || active) Overlay.ClickBlocker("vorax", origin, size);
        Draw.RectFilled(origin, origin + size, GameColors.LabelBackground, 4);
        if (_overlay.Movable) Overlay.DragHint(origin, origin + size, active, hovered, Overlay.ResizeEdge.None);
        var at = origin + new Vector2(Pad);
        foreach (var (left, right, colour) in rows)
        {
            Draw.Row(at, Width, left, "", _style);
            var rightStyle = _style.Clone();
            rightStyle.Color = colour;
            Draw.Text(right, new Vector2(at.X + Width, at.Y), rightStyle, Align.Right);
            at.Y += rowHeight;
        }
    }

    internal List<(string Left, string Right, Color Colour)> PanelRows(VoraxState state, Advice? advice)
    {
        var rows = AdviceRows(state, advice);
        if (Settings.Debug) rows.AddRange(Rows(state, Width, _style));
        return rows;
    }

    internal static string SourceLabel(Advice advice) => advice.Source switch
    {
        AdviceSource.Strategy => $"Strategy: {advice.StrategyName}",
        AdviceSource.SimulatorFallback => $"Simulator (fallback: {advice.Error})",
        _ => "Simulator",
    };

    internal static List<(string Left, string Right, Color Colour)> AdviceRows(VoraxState state, Advice? advice)
    {
        var rows = new List<(string, string, Color)>();
        if (advice is null)
        {
            if (state.Phase == VoraxPhase.Other) rows.Add(("Advice", "none for this screen", GameColors.TextDim));
            else if (state.Offered.Length > 0) rows.Add(("Advice", "thinking...", GameColors.TextDim));
            return rows;
        }

        var indexes = advice.Pick.Indexes ?? [];
        rows.Add(("Stage", advice.Stage.ToString(), GameColors.Text));
        rows.Add(("Source", SourceLabel(advice), GameColors.Text));
        if (advice.Pick.IsReroll) rows.Add(("Pick: reroll", advice.Pick.Reason, GameColors.Gold));
        else if (indexes.Length > 0) rows.Add(($"Pick: {string.Join(", ", indexes.Select(i => PickName(state, advice.Stage, i)))}", advice.Pick.Reason, GameColors.Gold));
        if (advice.Values is { } values && advice.Stage != Stage.Target)
            for (var i = 0; i < Math.Min(values.Length, state.Offered.Length); i++)
                rows.Add(($"  {CardName(state.Offered[i])}", $"{values[i]:0}", GameColors.Text));
        if (advice.RerollValue is { } reroll) rows.Add(("Reroll", $"{reroll:0}", GameColors.Text));
        if (advice.Error is { } error && advice.Source != AdviceSource.SimulatorFallback) rows.Add(("Error", error, GameColors.Negative));
        return rows;
    }

    static string PickName(VoraxState state, Stage stage, int index)
    {
        if (stage == Stage.Target) return state.Monsters.Where(m => m.Slot == index).Select(m => m.Name).FirstOrDefault(name => name != "") ?? $"slot {index}";
        return index >= 0 && index < state.Offered.Length ? CardName(state.Offered[index]) : $"#{index}";
    }

    static string CardName(VoraxCard card) => card.Name != "" ? card.Name : $"card {card.Slot}";

    internal static List<(string Left, string Right, Color Colour)> Rows(VoraxState state, float width, TextStyle style)
    {
        var rows = new List<(string, string, Color)>
        {
            ("Phase", state.Phase.ToString(), GameColors.Text),
            ("Round", $"{state.Round} / {state.Rounds}", GameColors.Text),
            ("Activity", $"{state.Activity} / next {state.NextRewardActivity}", GameColors.Gold),
            ("Rewards", $"{state.RewardsTaken} / {state.RewardsTotal}", GameColors.Text),
            ("Rerolls", state.Rerolls.ToString(), GameColors.Text),
            ("Monster slots", state.MonsterSlots.ToString(), GameColors.Text),
            ("Reward bonus", $"+{state.RewardBonusPercent}%", GameColors.Text),
            ("Chests", string.Join(" ", state.Chests), GameColors.TextDim),
            ("Monsters", state.Monsters.Length.ToString(), GameColors.Gold),
        };
        rows.AddRange(state.Monsters.Select(m => ((string, string, Color))($"  {m.Slot}  {m.Name} (id {m.Id}, species {m.Species}, rarity {m.Rarity} {m.RarityName})", $"{m.Count} x {m.Activity} = {m.Total}", GameColors.Text)));
        AddCards(rows, "Offered", state.Offered, width, style);
        AddCards(rows, "Instruments", state.Instruments, width, style);
        AddCards(rows, "Protocols", state.Protocols, width, style);
        return rows;
    }

    static void AddCards(List<(string, string, Color)> rows, string title, VoraxCard[] cards, float width, TextStyle style)
    {
        rows.Add((title, cards.Length.ToString(), GameColors.Gold));
        foreach (var card in cards)
        {
            rows.Add(($"  {card.Slot}  {card.Name} (id {card.Id}, rarity {card.Rarity})", $"{card.Kind} / {card.TypeName}", GameColors.Text));
            foreach (var line in Text.Wrap(card.Description, width - 24, style)) rows.Add(("      " + line, "", GameColors.TextDim));
        }
    }
}
