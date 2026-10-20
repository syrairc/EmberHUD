using System;
using System.Drawing;
using System.Linq;
using System.Numerics;
using EmberHUD;
using EmberHUD.Ui;
using Hexa.NET.ImGui;

namespace Tarot;

public sealed class Main : Plugin<Settings>
{
    const float Pad = 8;
    const float Gap = 24;
    const float TileGap = 2;
    const int TilesPerRow = 4;
    const float SlotWidth = 100, CardCentre = 24.4f, CardTop = 178, ArrowGap = 6, ArrowSize = 70, ChoiceArrowGap = 10, ChoiceArrowSize = 0.28f, ChoiceCardHalfHeight = 0.845f;
    readonly TextStyle _style = TextStyle.Hud();
    readonly Overlay _overlay = new();
    int _unusedWidth, _unusedHeight;

    public override void Tick() => Game.WantTarot();

    public override void Render()
    {
        if (!Settings.ShowPanel || Game.State.Tarot is not { } state) return;
        var rowHeight = Draw.RowHeight(_style);
        var rows = Rows(state);
        var width = 0f;
        foreach (var (left, right, _) in rows) width = MathF.Max(width, Draw.MeasureText(left, _style).X + Gap + Draw.MeasureText(right, _style).X);
        var side = rowHeight - TileGap;
        var rowStart = state.Position / TilesPerRow * TilesPerRow;
        var ahead = state.Phase == TarotPhase.Board ? Math.Clamp(Settings.AheadTiles, 0, Math.Max(0, state.Tiles.Length - rowStart)) : 0;
        var tileRows = (ahead + TilesPerRow - 1) / TilesPerRow;
        var hasTaken = state.Phase == TarotPhase.Board && state.Taken.Length > 0;
        width = MathF.Max(width, TilesPerRow * (side + TileGap));
        var height = rowHeight * (rows.Count + (hasTaken ? 1 : 0) + tileRows);
        var size = new Vector2(width, height) + new Vector2(Pad * 2);
        _overlay.Locked = Settings.Lock;
        _overlay.MoveHeld = Settings.MoveKey.Key != Key.None && Input.IsDown(Settings.MoveKey.Key);
        var origin = new Vector2(Settings.PanelX, Settings.PanelY);
        var (hovered, _, active) = _overlay.Handle("tarot", ref origin, size, ref Settings.PanelX, ref Settings.PanelY, ref _unusedWidth, 60, 4000, ref _unusedHeight, 0, 0, Overlay.ResizeEdge.None);
        if (hovered || active) Overlay.ClickBlocker("tarot", origin, size);
        Draw.RectFilled(origin, origin + size, GameColors.LabelBackground, 4);
        if (_overlay.Movable) Overlay.DragHint(origin, origin + size, active, hovered, Overlay.ResizeEdge.None);
        if (Settings.HighlightCard && !(state.Phase == TarotPhase.Board && ArrowChoice(state))) ArrowBestCards(state);
        var at = origin + new Vector2(Pad);
        foreach (var (left, right, colour) in rows)
        {
            Draw.Row(at, width, left, "", _style);
            var rightStyle = _style.Clone();
            rightStyle.Color = colour;
            Draw.Text(right, new Vector2(at.X + width, at.Y), rightStyle, Align.Right);
            at.Y += rowHeight;
        }
        if (hasTaken)
        {
            Draw.Text("Taken", at, _style);
            var x = at.X + Draw.MeasureText("Taken", _style).X + TileGap * 3;
            foreach (var rarity in state.Taken)
            {
                Draw.RectFilled(new Vector2(x, at.Y), new Vector2(x + side, at.Y + side), Board.ChestColor(rarity));
                x += side + TileGap;
            }
            at.Y += rowHeight;
        }
        for (var i = 0; i < ahead; i++)
        {
            var tile = state.Tiles[rowStart + i];
            var min = new Vector2(at.X + i % TilesPerRow * (side + TileGap), at.Y + i / TilesPerRow * rowHeight);
            DrawTile(min, side, tile);
            if (rowStart + i == state.Position) Draw.Rect(min - new Vector2(1), min + new Vector2(side + 1), GameColors.Text, 2);
        }
    }

    static NamedRect[] Parts(string prefix) =>
        [.. (Game.State.Ui.Panels.FirstOrDefault(panel => panel.Name == "S9Taro_C").Parts ?? []).Where(part => part.Name.StartsWith(prefix)).OrderBy(part => part.Min.X)];

    static bool ArrowChoice(TarotState state)
    {
        var cards = Parts("PopPanel/ScrollBox[");
        if (cards.Length == 0) return false;
        var options = Board.Options(state.Playing);
        if (options.Length != cards.Length || Board.BestMove(state, new TarotCard(0, state.Playing)) is not { } move) return true;
        var card = cards[Array.IndexOf(options, move.Steps)];
        var width = card.Max.X - card.Min.X;
        var top = (card.Min.Y + card.Max.Y) / 2 - width * ChoiceCardHalfHeight;
        Draw.ArrowDown(new Vector2(card.Min.X + width / 2, top - ChoiceArrowGap), width * ChoiceArrowSize, move.Score > 0 ? GameColors.Positive : GameColors.Gold);
        return true;
    }

    static void ArrowBestCards(TarotState state)
    {
        if (state.Phase != TarotPhase.Board) return;
        var cards = Parts("HandCardPanel[");
        if (Board.ShownCards(state.Hand, cards.Length) is not { } shown) return;
        foreach (var best in Board.BestCards(state))
        {
            var slot = Array.IndexOf(shown, best);
            if (slot < 0) continue;
            var score = Board.BestMove(state, state.Hand[best])!.Value.Score;
            var scale = (cards[slot].Max.X - cards[slot].Min.X) / SlotWidth;
            var tip = cards[slot].Min + new Vector2(CardCentre, -CardTop - ArrowGap) * scale;
            Draw.ArrowDown(tip, ArrowSize * scale, score > 0 ? GameColors.Positive : GameColors.Gold);
        }
    }

    static void DrawTile(Vector2 min, float side, TarotTile tile)
    {
        var kind = Board.Kind(tile);
        var max = min + new Vector2(side);
        if (kind == TileKind.Empty) { Draw.Rect(min, max, GameColors.TextDim); return; }
        Draw.RectFilled(min, max, kind switch
        {
            TileKind.Chest => Board.ChestColor(tile.Chest),
            TileKind.Trap => GameColors.Negative,
            TileKind.Heal => GameColors.Positive,
            TileKind.ChestUp => GameColors.Gold,
            _ => GameColors.TextDim,
        });
    }

    System.Collections.Generic.List<(string Left, string Right, Color Colour)> Rows(TarotState state)
    {
        var rows = new System.Collections.Generic.List<(string, string, Color)>();
        switch (state.Phase)
        {
            case TarotPhase.Collect: rows.Add(("Tarot", $"trials {state.Progress}", GameColors.Text)); break;
            case TarotPhase.Final: rows.Add(("Tarot", $"challenges left {state.ChallengesLeft}", GameColors.Text)); break;
            default:
                rows.Add(("Tarot", $"HP {state.Hp}  tile {state.Position + 1}/{state.Tiles.Length}  round {state.Round}", GameColors.Text));
                foreach (var card in state.Hand) rows.Add((Board.CardLabel(card.CfgId), Outcome(state, card), OutcomeColour(state, card)));
                break;
        }
        return rows;
    }

    static string Outcome(TarotState state, TarotCard card)
    {
        if (Board.BestMove(state, card) is not { } move || move.Landing == state.Position) return "";
        if (Board.Steps(card.CfgId) is null && move.Score == 0) return "";
        var pick = Board.Steps(card.CfgId) is null ? $"{(move.Steps > 0 ? "Advance" : "Retreat")} {Math.Abs(move.Steps)} " : "";
        return $"{pick}-> {move.Landing + 1} {Board.Kind(state.Tiles[move.Landing])}";
    }

    static Color OutcomeColour(TarotState state, TarotCard card)
    {
        if (Board.BestMove(state, card) is not { } move || move.Landing == state.Position) return GameColors.TextDim;
        var landing = move.Landing;
        var tile = state.Tiles[landing];
        return Board.Kind(tile) switch
        {
            TileKind.Trap => GameColors.Negative,
            TileKind.Heal => GameColors.Positive,
            TileKind.Chest => Board.ChestColor(tile.Chest),
            _ => GameColors.Text,
        };
    }
}
