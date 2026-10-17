using System.Numerics;
using EmberHUD;

namespace NameplatesPlugin;

static class Anchor
{
    internal static ScreenRect? Rect(Settings settings, in Entity entity, PlateStyle style, Vector2? projected)
    {
        if (settings.AnchorToGameBar && entity.Kind == EntityKind.Monster && entity.HasGameBar)
        {
            if (entity.GameBar is not { } bar) return null;
            var grow = style.DrawBar ? new Vector2(style.Bar.BorderWidth) : Vector2.Zero;
            var shift = new Vector2(0, style.OffsetY);
            return new ScreenRect(bar.Min - grow - shift, bar.Max + grow - shift);
        }
        return projected is { } point ? Projected(style, point) : null;
    }

    internal static ScreenRect Projected(PlateStyle style, Vector2 point)
    {
        var centre = new Vector2(point.X, point.Y - style.OffsetY);
        var half = new Vector2(style.Bar.Width, style.Bar.Height) / 2;
        return new ScreenRect(centre - half, centre + half);
    }
}
