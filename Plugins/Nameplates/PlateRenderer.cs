using System;
using System.Collections.Generic;
using System.Numerics;
using EmberHUD;
using EmberHUD.Ui;

namespace NameplatesPlugin;

public static class PlateRenderer
{
    const float TextGap = 2;
    const int MaxCachedTemplates = 128;

    static readonly Dictionary<string, Template> Templates = new();

    public static bool DrawPlate(in PlateData data, PlateStyle style, ScreenRect rect, Vector2 viewport)
    {
        var bar = style.Bar;
        var min = rect.Min;
        var max = rect.Max;
        if (!(max.X >= 0 && max.Y >= 0 && min.X <= viewport.X && min.Y <= viewport.Y)) return false;

        var hasBar = data.MaxLife > 0 && !double.IsNaN(data.Life);
        if (hasBar && style.DrawBar) DrawBar(bar, min, max, (float)Math.Clamp(data.Life / data.MaxLife, 0, 1));
        DrawLine(style.Above, data, min, max, new Vector2(0, min.Y - TextGap), Align.Bottom);
        if (hasBar) DrawLine(style.OnBar, data, min, max, new Vector2(0, (min.Y + max.Y) / 2), Align.Middle);
        DrawLine(style.Below, data, min, max, new Vector2(0, max.Y + TextGap), Align.TopLeft);
        return true;
    }

    public static void DrawCast(CastBarSettings cast, in PlateData data, float progress, ScreenRect plate)
    {
        var centre = (plate.Min.X + plate.Max.X) / 2;
        var min = new Vector2(centre - cast.Bar.Width / 2, plate.Max.Y + cast.OffsetY);
        var max = new Vector2(centre + cast.Bar.Width / 2, min.Y + cast.Bar.Height);
        DrawBar(cast.Bar, min, max, Math.Clamp(progress, 0, 1));
        DrawLine(cast.Text, data, min, max, new Vector2(0, (min.Y + max.Y) / 2), Align.Middle);
    }

    public static void DrawBossBarHp(TextLine line, TextRow row, in PlateData data, ScreenRect bar)
    {
        var (y, vertical) = row switch
        {
            TextRow.Above => (bar.Min.Y - TextGap, Align.Bottom),
            TextRow.Below => (bar.Max.Y + TextGap, Align.TopLeft),
            _ => ((bar.Min.Y + bar.Max.Y) / 2, Align.Middle),
        };
        DrawLine(line, data, bar.Min, bar.Max, new Vector2(0, y), vertical);
    }

    static void DrawBar(BarStyle bar, Vector2 min, Vector2 max, float fraction)
    {
        Draw.RectFilled(min, max, bar.Back);
        if (fraction > 0)
        {
            var fillMax = new Vector2(min.X + (max.X - min.X) * fraction, max.Y);
            if (BarTextures.Get(bar.Texture) is { } sprite)
                Draw.Image(sprite with { Uv1 = new Vector2(sprite.Uv0.X + (sprite.Uv1.X - sprite.Uv0.X) * fraction, sprite.Uv1.Y) }, min, fillMax, bar.Fill);
            else
                Draw.RectFilled(min, fillMax, bar.Fill);
        }
        if (bar.BorderWidth > 0) Draw.Rect(min, max, bar.Border, bar.BorderWidth);
    }

    static void DrawLine(TextLine line, in PlateData data, Vector2 min, Vector2 max, Vector2 at, Align vertical)
    {
        if (!line.Enabled) return;
        var text = TemplateFor(line.Template).Render(data);
        if (text.Length == 0) return;
        var horizontal = line.Align switch { HAlign.Right => Align.Right, HAlign.Center => Align.Center, _ => Align.TopLeft };
        at.X = line.Align switch { HAlign.Right => max.X, HAlign.Center => (min.X + max.X) / 2, _ => min.X };
        Draw.Text(text, at, line.Style, horizontal | vertical);
    }

    static Template TemplateFor(string text)
    {
        if (Templates.TryGetValue(text, out var cached)) return cached;
        if (Templates.Count >= MaxCachedTemplates) Templates.Clear();
        return Templates[text] = Template.Parse(text);
    }
}
