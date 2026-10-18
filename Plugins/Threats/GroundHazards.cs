using System;
using System.Drawing;
using System.Numerics;
using EmberHUD;

namespace Threats;

public static class GroundHazards
{
    const int Segments = 48;

    static bool Valid(in GroundHazard hazard) =>
        float.IsFinite(hazard.Centre.X) && float.IsFinite(hazard.Centre.Y) && float.IsFinite(hazard.Radius) && hazard.Radius > 0;

    public static bool Inside(in GroundHazard hazard, Vector2 player) =>
        Valid(hazard) && float.IsFinite(player.X) && float.IsFinite(player.Y) && Vector2.Distance(hazard.Centre, player) <= hazard.Radius;

    public static bool Draw(in GroundHazard hazard, Settings settings, Vector2 player)
    {
        var inside = Inside(hazard, player);
        if (!Valid(hazard) || Game.State.Camera is null) return inside;
        Span<Vector2> ring = stackalloc Vector2[Segments];
        Geometry.RegularPolygon(hazard.Centre, hazard.Radius, ring);
        var colour = inside ? settings.DangerColour : settings.AreaColour;
        var opacity = float.IsFinite(settings.FillOpacity) ? Math.Clamp(settings.FillOpacity, 0f, 1f) : 0f;
        Ground.PolygonFilled(ring, Color.FromArgb((int)Math.Clamp(colour.A * opacity, 0f, 255f), colour));
        Ground.Polyline(ring, colour, 3, true);
        return inside;
    }
}
