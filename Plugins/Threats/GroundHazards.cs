using System;
using System.Drawing;
using System.Numerics;
using EmberHUD;

namespace Threats;

public static class GroundHazards
{
    const int Segments = 48;

    static bool Valid(in GroundHazard hazard) =>
        float.IsFinite(hazard.Centre.X) && float.IsFinite(hazard.Centre.Y) && float.IsFinite(hazard.Radius) && hazard.Radius > 0 && float.IsFinite(hazard.InnerRadius) && hazard.InnerRadius >= 0;

    public static bool Inside(in GroundHazard hazard, Vector2 player)
    {
        if (!Valid(hazard) || !float.IsFinite(player.X) || !float.IsFinite(player.Y)) return false;
        var distance = Vector2.Distance(hazard.Centre, player);
        return distance <= hazard.Radius && distance >= hazard.InnerRadius;
    }

    public static bool Draw(in GroundHazard hazard, Settings settings, Vector2 player)
    {
        var inside = Inside(hazard, player);
        if (!Valid(hazard) || Game.State.Camera is null) return inside;
        Span<Vector2> ring = stackalloc Vector2[Segments];
        Geometry.RegularPolygon(hazard.Centre, hazard.Radius, ring);
        var colour = inside ? settings.DangerColour : settings.AreaColour;
        var opacity = float.IsFinite(settings.FillOpacity) ? Math.Clamp(settings.FillOpacity, 0f, 1f) : 0f;
        var fill = Color.FromArgb((int)Math.Clamp(colour.A * opacity, 0f, 255f), colour);
        if (hazard.InnerRadius <= 0)
        {
            Ground.PolygonFilled(ring, fill);
            Ground.Polyline(ring, colour, 3, true);
            return inside;
        }

        Span<Vector2> hole = stackalloc Vector2[Segments];
        Geometry.RegularPolygon(hazard.Centre, hazard.InnerRadius, hole);
        Span<Vector2> quad = stackalloc Vector2[4];
        for (var i = 0; i < Segments; i++)
        {
            var next = (i + 1) % Segments;
            quad[0] = ring[i];
            quad[1] = ring[next];
            quad[2] = hole[next];
            quad[3] = hole[i];
            Ground.PolygonFilled(quad, fill);
        }

        Ground.Polyline(ring, colour, 3, true);
        Ground.Polyline(hole, colour, 3, true);
        return inside;
    }
}
