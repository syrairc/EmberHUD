using System;
using System.Drawing;
using System.Numerics;
using EmberHUD;

namespace ActivationMedium;

static class Hexagon
{
    // pointy-top regular hexagon filling the bounds, the shape of the skill bar icons
    internal static Vector2[] Points(Vector2 min, Vector2 max)
    {
        var centre = (min.X + max.X) / 2;
        var quarter = (max.Y - min.Y) / 4;
        return [new(centre, min.Y), new(max.X, min.Y + quarter), new(max.X, max.Y - quarter), new(centre, max.Y), new(min.X, max.Y - quarter), new(min.X, min.Y + quarter)];
    }

    internal static void Glow(Vector2 min, Vector2 max, Color color, float thickness, float spread)
    {
        var rings = (int)MathF.Ceiling(spread / 2);
        for (var ring = rings; ring >= 1; ring--)
        {
            var grow = new Vector2(thickness / 2 + ring * 2);
            var alpha = (int)(color.A * 0.5f * (1 - ring / (float)(rings + 1)));
            Draw.Polyline(Points(min - grow, max + grow), Color.FromArgb(alpha, color), 2, true);
        }
        Draw.Polyline(Points(min, max), color, thickness, true);
    }
}
