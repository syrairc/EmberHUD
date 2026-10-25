using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using EmberHUD;
using EmberHUD.Ui;

namespace Prices;

public enum ProfitTone { Plain, Positive, Negative }

public sealed record ProfitRow(string Label, string Value, ProfitTone Tone, bool Fe, bool Divider = false);

internal sealed class ProfitOverlay
{
    const float PadX = 12;
    const float PadY = 8;
    const float LabelGap = 16;
    const float IconGap = 2;
    const float DividerHeight = 8;
    const string Separator = "  |  ";

    readonly Overlay _full = new();
    readonly Overlay _compact = new();
    ProfitView? _view;
    bool _built;
    ProfitRow[] _fullRows = [];
    ProfitRow[] _compactRows = [];
    int _unusedWidth, _unusedHeight;

    internal static ProfitRow[] FullRows(ProfitView? view) =>
    [
        new("Session", view is null ? "-" : $"{Duration(view.Time)}, {view.Runs} runs", ProfitTone.Plain, false),
        new("", "", ProfitTone.Plain, false, true),
        Signed("This map", view?.ThisMap, view?.ThisMapUnpriced ?? false),
        Signed("Last map", view?.LastMap, view?.Unpriced ?? false),
        Signed("Profit / map", view?.PerMap, view?.Unpriced ?? false),
        Signed("Profit / hr", view?.PerHour, view?.Unpriced ?? false),
        Signed("Session profit", view?.Profit, view?.Unpriced ?? false),
        new("", "", ProfitTone.Plain, false, true),
        Wealth("Total wealth", view?.Wealth),
        Wealth("FE on hand", view?.Fe)
    ];

    internal static ProfitRow[] CompactRows(ProfitView? view) =>
    [
        Signed("/hr ", view?.PerHour, view?.Unpriced ?? false),
        Signed("/map ", view?.PerMap, view?.Unpriced ?? false),
        Wealth("Wealth ", view?.Wealth)
    ];

    internal static string Duration(TimeSpan time) =>
        time.TotalHours >= 1 ? $"{(int)time.TotalHours}h {time.Minutes}m" : $"{time.Minutes}m";

    static ProfitRow Signed(string label, float? value, bool unpriced)
    {
        var text = ProfitText.Signed(value, unpriced);
        var plain = value is null || text.TrimEnd('*') == "0";
        return new(label, text, plain ? ProfitTone.Plain : value < 0 ? ProfitTone.Negative : ProfitTone.Positive, true);
    }

    static ProfitRow Wealth(string label, float? value) =>
        new(label, value is { } number ? ProfitText.Plain(number) : "-", ProfitTone.Plain, true);

    public void Draw(Settings settings, ProfitView? view, Sprite icon, bool hasIcon)
    {
        if (!_built || view != _view)
        {
            (_view, _built) = (view, true);
            (_fullRows, _compactRows) = (FullRows(view), CompactRows(view));
        }
        if (settings.ProfitFull) DrawFull(settings, icon, hasIcon);
        if (settings.ProfitCompact) DrawCompact(settings, icon, hasIcon);
    }

    void DrawFull(Settings settings, Sprite icon, bool hasIcon)
    {
        var style = settings.ProfitFullStyle;
        var text = TextOnly(style);
        var height = EmberHUD.Draw.RowHeight(text);
        var glyph = EmberHUD.Draw.MeasureText("Ag", text).Y;
        var iconWidth = hasIcon ? glyph + IconGap : 0;
        float widest = 0, total = 0;
        foreach (var row in _fullRows)
        {
            if (row.Divider)
            {
                total += DividerHeight;
                continue;
            }
            total += height;
            var width = EmberHUD.Draw.MeasureText(row.Label, text).X + LabelGap + EmberHUD.Draw.MeasureText(row.Value, text).X + (row.Fe ? iconWidth : 0);
            widest = Math.Max(widest, width);
        }
        var size = new Vector2(widest + PadX * 2, total + PadY * 2);
        var origin = Place(_full, settings, "prices-profit-full", size, ref settings.ProfitFullX, ref settings.ProfitFullY);
        Panel(origin, size, style);
        var y = origin.Y + PadY;
        var right = origin.X + size.X - PadX;
        foreach (var row in _fullRows)
        {
            if (row.Divider)
            {
                var line = y + DividerHeight / 2;
                EmberHUD.Draw.Line(new Vector2(origin.X + PadX, line), new Vector2(right, line), System.Drawing.Color.FromArgb(70, style.Color));
                y += DividerHeight;
                continue;
            }
            text.Color = style.Color;
            EmberHUD.Draw.Text(row.Label, new Vector2(origin.X + PadX, y), text);
            text.Color = ToneColor(row.Tone, style);
            EmberHUD.Draw.Text(row.Value, new Vector2(right - (row.Fe ? iconWidth : 0), y), text, Align.Right);
            if (row.Fe && hasIcon) EmberHUD.Draw.Image(icon, new Vector2(right - glyph, y), new Vector2(right, y + glyph));
            y += height;
        }
    }

    void DrawCompact(Settings settings, Sprite icon, bool hasIcon)
    {
        var style = settings.ProfitCompactStyle;
        var text = TextOnly(style);
        var height = EmberHUD.Draw.MeasureText("Ag", text).Y;
        var iconWidth = hasIcon ? height + IconGap : 0;
        var gap = EmberHUD.Draw.MeasureText(Separator, text).X;
        float width = 0;
        for (var i = 0; i < _compactRows.Length; i++)
        {
            var row = _compactRows[i];
            width += EmberHUD.Draw.MeasureText(row.Label, text).X + EmberHUD.Draw.MeasureText(row.Value, text).X + (row.Fe ? iconWidth : 0) + (i > 0 ? gap : 0);
        }
        var size = new Vector2(width + PadX * 2, height + PadY * 2);
        var origin = Place(_compact, settings, "prices-profit-compact", size, ref settings.ProfitCompactX, ref settings.ProfitCompactY);
        Panel(origin, size, style);
        var at = new Vector2(origin.X + PadX, origin.Y + PadY);
        for (var i = 0; i < _compactRows.Length; i++)
        {
            var row = _compactRows[i];
            text.Color = style.Color;
            if (i > 0) at.X += EmberHUD.Draw.Text(Separator, at, text).X;
            at.X += EmberHUD.Draw.Text(row.Label + " ", at, text).X;
            text.Color = ToneColor(row.Tone, style);
            at.X += EmberHUD.Draw.Text(row.Value, at, text).X;
            if (!row.Fe || !hasIcon) continue;
            at.X += IconGap;
            EmberHUD.Draw.Image(icon, at, at + new Vector2(height));
            at.X += height;
        }
    }

    static TextStyle TextOnly(TextStyle style)
    {
        var text = style.Clone();
        (text.BgEnabled, text.BorderEnabled) = (false, false);
        return text;
    }

    static System.Drawing.Color ToneColor(ProfitTone tone, TextStyle style) =>
        tone == ProfitTone.Positive ? GameColors.Positive : tone == ProfitTone.Negative ? GameColors.Negative : style.Color;

    static void Panel(Vector2 origin, Vector2 size, TextStyle style)
    {
        if (style.BgEnabled) EmberHUD.Draw.RectFilled(origin, origin + size, style.BgColor, style.BorderRounding);
        if (style.BorderEnabled) EmberHUD.Draw.Rect(origin, origin + size, style.BorderColor, style.BorderThickness, style.BorderRounding);
    }

    Vector2 Place(Overlay overlay, Settings settings, string id, Vector2 size, ref int x, ref int y)
    {
        overlay.Locked = settings.ProfitLock;
        overlay.MoveHeld = settings.ProfitMoveKey.Key != Key.None && Input.IsDown(settings.ProfitMoveKey.Key);
        var origin = new Vector2(x, y);
        var (hovered, _, active) = overlay.Handle(id, ref origin, size, ref x, ref y, ref _unusedWidth, 0, 4000, ref _unusedHeight, 0, 0, Overlay.ResizeEdge.None);
        if (hovered || active) Overlay.ClickBlocker(id, origin, size);
        if (overlay.Movable) Overlay.DragHint(origin, origin + size, active, hovered, Overlay.ResizeEdge.None);
        return origin;
    }
}
