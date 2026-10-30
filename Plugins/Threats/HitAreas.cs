using System;
using System.Collections.Generic;
using System.Numerics;
using EmberHUD;

namespace Threats;

public static class HitAreas
{
    const int MaxVertices = 64;
    const double Limit = 1e7;
    const double TimerLimit = 600;

    public static bool Eligible(FightEntity fighter, HashSet<int> hazardIds) =>
        fighter.IsHostile && fighter.Shape is { } shape && ((shape.Kind & 0xFF) is 1 or 2 or 4 or 5) &&
        fighter.Archetype is FightArchetype.Anchor or FightArchetype.Unknown && !hazardIds.Contains(fighter.Id) &&
        double.IsFinite(fighter.Position.X) && double.IsFinite(fighter.Position.Y) && Math.Abs(fighter.Position.X) < Limit && Math.Abs(fighter.Position.Y) < Limit;

    public static bool Draw(FightEntity fighter, Settings settings, Vector2 player)
    {
        var inside = fighter.Covers(player);
        if (Game.State.Camera is not { } camera) return inside;
        Span<Vector2> outline = stackalloc Vector2[MaxVertices];
        outline = outline[..fighter.Outline(outline)];
        if (outline.Length < 3) return inside;

        var group = inside ? Groups.HazardsDanger : Groups.Hazards;
        var fill = Groups.Fill(inside ? settings.DangerColour : settings.AreaColour, settings);
        if (fighter.Shape is { Inner: > 0, Radius: > 0 } ring && (ring.Kind & 0xFF) == 5) Ring(outline, Math.Min(1f, (float)ring.Inner / ring.Radius), fill, group);
        else Ground.PolygonFilled(outline, fill, group: group);

        if (settings.HazardTimers && fighter.Attack is { Delay: > 0 and < TimerLimit } attack && float.IsFinite(settings.HazardTimerSize))
            Telegraph.Label($"{attack.Delay:0.0}", new Vector2(fighter.Position.X, fighter.Position.Y), settings.HazardTimerSize, camera);
        return inside;
    }

    static void Ring(ReadOnlySpan<Vector2> outer, float ratio, System.Drawing.Color fill, string group)
    {
        var centre = Vector2.Zero;
        foreach (var vertex in outer) centre += vertex;
        centre /= outer.Length;
        Span<Vector2> hole = stackalloc Vector2[outer.Length];
        for (var i = 0; i < outer.Length; i++) hole[i] = centre + (outer[i] - centre) * ratio;
        GroundHazards.Band(outer, hole, fill, group);
    }
}
