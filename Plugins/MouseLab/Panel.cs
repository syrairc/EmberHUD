using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Numerics;
using EmberHUD;
using EmberHUD.Ui;

namespace MouseLab;

public sealed class Panel
{
    const float Pad = 8;
    const float ColumnGap = 14;
    static readonly string[] MoveColumns = ["Move", "ms", "Mean", "Peak", "Err", "Back", "Over", "Jitter", "Rough", "Stall"];

    readonly TextStyle _style = TextStyle.Hud();
    readonly Overlay _overlay = new();
    int _unusedWidth, _unusedHeight;

    readonly record struct Cell(string Text, Color Colour);

    public static (Vector2 From, Vector2 To, double Speed)[] Segments(IReadOnlyList<PathSample> trail)
    {
        var points = new List<PathSample>();
        foreach (var sample in trail)
            if (points.Count == 0 || points[^1].X != sample.X || points[^1].Y != sample.Y) points.Add(sample);
        var segments = new (Vector2, Vector2, double)[Math.Max(0, points.Count - 1)];
        for (var i = 0; i < segments.Length; i++)
        {
            var (a, b) = (points[i], points[i + 1]);
            var distance = Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y));
            segments[i] = (new Vector2((float)a.X, (float)a.Y), new Vector2((float)b.X, (float)b.Y), distance / Math.Max(b.TimeMs - a.TimeMs, 1e-3));
        }
        return segments;
    }

    public static void DrawTarget(Step step) => Draw.Rect(step.Target.Min, step.Target.Max, GameColors.Gold, 2);

    public static void DrawTrail(RunResult result)
    {
        var segments = result.Moves.SelectMany(move => Segments(move.Trail)).ToArray();
        if (segments.Length == 0) return;
        var peak = Math.Max(segments.Max(segment => segment.Speed), 1e-3);
        foreach (var (from, to, speed) in segments)
        {
            if (Send.ScreenToClient((int)from.X, (int)from.Y) is not var (fromX, fromY) || Send.ScreenToClient((int)to.X, (int)to.Y) is not var (toX, toY)) return;
            var heat = (float)(speed / peak);
            Draw.Line(new Vector2(fromX, fromY), new Vector2(toX, toY), Color.FromArgb(220, (int)(60 + 195 * heat), (int)(220 - 160 * heat), 80), 2);
        }
    }

    public void Show(Settings settings, IReadOnlyList<RunResult> results, string? running, string? note)
    {
        var lines = new List<Cell>();
        var tables = new List<List<Cell[]>>();
        Describe(results, running, note, lines, tables);

        var rowHeight = Draw.RowHeight(_style);
        var widths = tables.Select(Widths).ToList();
        var width = lines.Count == 0 ? 0 : lines.Max(line => Draw.MeasureText(line.Text, _style).X);
        width = Math.Max(width, widths.Count == 0 ? 0 : widths.Max(table => table.Sum()));
        var height = rowHeight * (lines.Count + tables.Sum(table => table.Count));
        var size = new Vector2(width, height) + new Vector2(Pad * 2);

        _overlay.Locked = settings.Lock;
        _overlay.MoveHeld = settings.MoveKey.Key != Key.None && Input.IsDown(settings.MoveKey.Key);
        var origin = new Vector2(settings.PanelX, settings.PanelY);
        var (hovered, _, active) = _overlay.Handle("mouselab", ref origin, size, ref settings.PanelX, ref settings.PanelY, ref _unusedWidth, 60, 4000, ref _unusedHeight, 0, 0, Overlay.ResizeEdge.None);
        if (hovered || active) Overlay.ClickBlocker("mouselab", origin, size);
        Draw.RectFilled(origin, origin + size, GameColors.LabelBackground, 4);
        if (_overlay.Movable) Overlay.DragHint(origin, origin + size, active, hovered, Overlay.ResizeEdge.None);

        var at = origin + new Vector2(Pad);
        foreach (var line in lines)
        {
            Put(line, at, Align.TopLeft);
            at.Y += rowHeight;
        }
        for (var t = 0; t < tables.Count; t++)
        {
            foreach (var row in tables[t])
            {
                var x = at.X;
                for (var c = 0; c < row.Length; c++)
                {
                    x += widths[t][c];
                    Put(row[c], new Vector2(c == 0 ? x - widths[t][c] : x - ColumnGap, at.Y), c == 0 ? Align.TopLeft : Align.Right);
                }
                at.Y += rowHeight;
            }
        }
    }

    void Put(Cell cell, Vector2 at, Align align)
    {
        var style = _style.Clone();
        style.Color = cell.Colour;
        Draw.Text(cell.Text, at, style, align);
    }

    float[] Widths(List<Cell[]> table) =>
        [.. Enumerable.Range(0, table[0].Length).Select(column => table.Max(row => Draw.MeasureText(row[column].Text, _style).X) + ColumnGap)];

    static void Describe(IReadOnlyList<RunResult> results, string? running, string? note, List<Cell> lines, List<List<Cell[]>> tables)
    {
        if (running is not null) lines.Add(new Cell($"Mouse lab - running {running}, press the run key again to cancel", GameColors.Gold));
        else if (results.Count > 0) lines.Add(new Cell($"Mouse lab - {(results.Count > 1 ? "all presets" : results[^1].Preset)}", GameColors.Text));
        if (note is not null) lines.Add(new Cell(note, GameColors.TextDim));
        foreach (var result in results)
        {
            if (result.Refused is not null) lines.Add(new Cell($"{result.Preset}: refused - {result.Refused}", GameColors.Negative));
            else if (result.Cancelled) lines.Add(new Cell($"{result.Preset}: cancelled after {result.Moves.Count} moves", GameColors.Gold));
            if (result.Note is not null) lines.Add(new Cell($"{result.Preset}: {result.Note}", GameColors.Negative));
        }
        foreach (var skipped in results.SelectMany(result => result.Skipped).Distinct()) lines.Add(new Cell($"Skipped - {skipped}", GameColors.TextDim));
        if (results.LastOrDefault(result => result.Moves.Count > 0) is { } last) tables.Add(MoveTable(last));
        if (results.Count > 1) tables.Add(Comparison(results));
    }

    static Color Ok(bool ok) => ok ? GameColors.Positive : GameColors.Negative;

    static List<Cell[]> MoveTable(RunResult result)
    {
        var rows = new List<Cell[]> { MoveColumns.Select(name => new Cell(name, GameColors.TextDim)).ToArray() };
        foreach (var move in result.Moves)
        {
            var (s, v) = (move.Scores, move.Verdict);
            rows.Add([
                new Cell($"{move.Scenario} {move.Label}", GameColors.Text),
                new Cell($"{s.DurationMs:0}", GameColors.Text),
                new Cell($"{s.MeanSpeed:0.00}", GameColors.Text),
                new Cell($"{s.PeakSpeed:0.00}", GameColors.Text),
                new Cell($"{s.FinalError:0.0}", Ok(v.FinalErrorOk)),
                new Cell($"{s.MaxBacktrack:0.0}", Ok(v.BacktrackOk)),
                new Cell($"{s.Overshoot:0.0}", Ok(v.OvershootOk)),
                new Cell($"{s.HeadingJitter:0.00}", GameColors.Text),
                new Cell($"{s.SpeedRoughness:0.00}", GameColors.Text),
                new Cell($"{s.LongestStallMs:0}", Ok(v.StallOk)),
            ]);
        }
        return rows;
    }

    static List<Cell[]> Comparison(IReadOnlyList<RunResult> results)
    {
        string Mean(RunResult result, Func<PathScores, double> pick, string format) => result.Moves.Count == 0 ? "-" : result.Moves.Average(move => pick(move.Scores)).ToString(format);
        string Worst(RunResult result, Func<PathScores, double> pick, string format) => result.Moves.Count == 0 ? "-" : result.Moves.Max(move => pick(move.Scores)).ToString(format);
        var passed = results.Select(result => result.Moves.Count(move => move.Verdict.All)).ToArray();

        Cell[] Row(string name, Func<RunResult, int, Cell> cell) => [new Cell(name, GameColors.TextDim), .. results.Select((result, index) => cell(result, index))];
        Cell Plain(string text) => new(text, GameColors.Text);

        return
        [
            Row("Preset", (result, _) => new Cell(result.Preset, GameColors.TextDim)),
            Row("Passed", (result, index) => new Cell($"{passed[index]}/{result.Moves.Count}", Ok(passed[index] == result.Moves.Count && result.Moves.Count > 0))),
            Row("Mean ms", (result, _) => Plain(Mean(result, s => s.DurationMs, "0"))),
            Row("Mean speed", (result, _) => Plain(Mean(result, s => s.MeanSpeed, "0.00"))),
            Row("Peak speed", (result, _) => Plain(Worst(result, s => s.PeakSpeed, "0.00"))),
            Row("Mean error", (result, _) => Plain(Mean(result, s => s.FinalError, "0.0"))),
            Row("Worst back", (result, _) => Plain(Worst(result, s => s.MaxBacktrack, "0.0"))),
            Row("Worst over", (result, _) => Plain(Worst(result, s => s.Overshoot, "0.0"))),
            Row("Worst stall", (result, _) => Plain(Worst(result, s => s.LongestStallMs, "0"))),
        ];
    }
}
