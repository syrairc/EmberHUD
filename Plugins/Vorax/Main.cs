using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Numerics;
using EmberHUD;
using EmberHUD.Ui;

namespace Vorax;

public sealed class Main : Plugin<Settings>
{
    const float Pad = 8;
    const float Width = 560;
    readonly TextStyle _style = TextStyle.Hud();
    readonly Overlay _overlay = new();
    int _unusedWidth, _unusedHeight;

    public override void Tick() => Game.WantVorax();

    public override void Render()
    {
        if (!Settings.Enabled || !Settings.ShowPanel || Game.State.Vorax is not { } state) return;
        var rowHeight = Draw.RowHeight(_style);
        var rows = Rows(state, Width, _style);
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
