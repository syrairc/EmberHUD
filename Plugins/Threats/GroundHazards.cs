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

    const double TimerLimit = 600;

    public static bool Draw(in GroundHazard hazard, Settings settings, Vector2 player, FightEntity? fighter = null)
    {
        var inside = Inside(hazard, player);
        if (!Valid(hazard) || Game.State.Camera is not { } camera) return inside;
        Shape(hazard, settings, inside);
        if (settings.UseFightData && settings.HazardTimers && FightData.HazardRemaining(fighter) is { } remaining && remaining > 0 && remaining < TimerLimit && float.IsFinite(settings.HazardTimerSize))
            Telegraph.Label($"{remaining:0.0}", hazard.Centre, settings.HazardTimerSize, camera);
        return inside;
    }

    static void Shape(in GroundHazard hazard, Settings settings, bool inside)
    {
        Span<Vector2> ring = stackalloc Vector2[Segments];
        Geometry.RegularPolygon(hazard.Centre, hazard.Radius, ring);
        var group = inside ? Groups.HazardsDanger : Groups.Hazards;
        var fill = Groups.Fill(inside ? settings.DangerColour : settings.AreaColour, settings);
        if (hazard.InnerRadius <= 0)
        {
            Ground.PolygonFilled(ring, fill, group: group);
            return;
        }

        Span<Vector2> hole = stackalloc Vector2[Segments];
        Geometry.RegularPolygon(hazard.Centre, hazard.InnerRadius, hole);
        Band(ring, hole, fill, group);
    }

    internal static void Band(ReadOnlySpan<Vector2> outer, ReadOnlySpan<Vector2> inner, Color fill, string group)
    {
        Span<Vector2> quad = stackalloc Vector2[4];
        for (var i = 0; i < outer.Length; i++)
        {
            var next = (i + 1) % outer.Length;
            quad[0] = outer[i];
            quad[1] = outer[next];
            quad[2] = inner[next];
            quad[3] = inner[i];
            Ground.PolygonFilled(quad, fill, group: group);
        }
    }
}
