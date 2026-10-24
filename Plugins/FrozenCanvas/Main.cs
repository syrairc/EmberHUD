using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Numerics;
using EmberHUD;
using EmberHUD.Ui;

namespace FrozenCanvas;

public sealed class Main : Plugin<Settings>
{
    const float Pad = 8;
    const float Width = 460;
    readonly TextStyle _style = TextStyle.Hud();
    readonly Overlay _overlay = new();
    int _unusedWidth, _unusedHeight;

    public override void Tick() => Game.WantFrozenCanvas();

    public override void Render()
    {
        if (!Settings.Enabled || !Settings.ShowPanel || Game.State.FrozenCanvas is not { } state) return;
        var rowHeight = Draw.RowHeight(_style);
        var rows = Rows(state, Width, _style);
        var size = new Vector2(Width, rowHeight * rows.Count) + new Vector2(Pad * 2);
        _overlay.Locked = Settings.Lock;
        _overlay.MoveHeld = Settings.MoveKey.Key != Key.None && Input.IsDown(Settings.MoveKey.Key);
        var origin = new Vector2(Settings.PanelX, Settings.PanelY);
        var (hovered, _, active) = _overlay.Handle("frozencanvas", ref origin, size, ref Settings.PanelX, ref Settings.PanelY, ref _unusedWidth, 60, 4000, ref _unusedHeight, 0, 0, Overlay.ResizeEdge.None);
        if (hovered || active) Overlay.ClickBlocker("frozencanvas", origin, size);
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

    internal static List<(string Left, string Right, Color Colour)> Rows(FrozenCanvasState state, float width, TextStyle style)
    {
        var rows = new List<(string, string, Color)>
        {
            ("Phase", $"{state.Phase} (difficulty {state.Difficulty})", GameColors.Text),
            ("Cells", $"{state.PlotsDone} / {state.PlotsTotal} (target {state.PlotsTarget})", GameColors.Gold),
            ("Challenges", $"{state.ChallengesLeft}/{state.ChallengesTotal}", GameColors.Text),
            ("Rewards", string.Join(" ", state.Rewards.Select(r => $"{r.ItemId} x{r.Count}")), GameColors.TextDim),
            ("Servants", state.Plots.Count(p => p.Servant).ToString(), GameColors.Text),
        };
        foreach (var player in state.Plots.Where(p => p.Player))
            rows.Add(("Player slot", $"{player.Slot} ({player.X}, {player.Y})", GameColors.Text));
        if (state.Objective is { } objective)
            rows.Add(($"Objective: {objective.Description}", $"done {objective.Done} / {objective.SecondsLeft:0}s left", GameColors.Gold));
        if (state.Draw is { } draw)
        {
            rows.Add(("Screen", draw.Screen.ToString(), GameColors.Text));
            rows.Add(("Placed", $"{draw.Placed} / {draw.Total}", GameColors.Text));
            rows.Add(("Max level", draw.MaxLevel.ToString(), GameColors.Text));
            rows.Add(("Mission", draw.Mission, GameColors.TextDim));
            rows.Add(("Papers", $"{draw.Papers.Length}, picked {string.Join(" ", draw.Papers.Where(p => p.Selected).Select(p => p.Name))}", GameColors.Text));
        }
        return rows;
    }
}
