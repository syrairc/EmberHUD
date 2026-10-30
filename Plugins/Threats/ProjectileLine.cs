using System;
using System.Collections.Generic;
using System.Drawing;
using System.Numerics;
using EmberHUD;

namespace Threats;

public static class ProjectileLine
{
    const float Slack = 20;
    const float Dash = 80;
    const float Gap = 80;

    static bool Valid(in Projectile projectile) =>
        float.IsFinite(projectile.Position.X) && float.IsFinite(projectile.Position.Y)
        && float.IsFinite(projectile.Direction.X) && float.IsFinite(projectile.Direction.Y)
        && float.IsFinite(projectile.Radius) && float.IsFinite(projectile.Direction.Length()) && projectile.Direction.Length() > 0.5f;

    static float Length(Settings settings) => float.IsFinite(settings.LineLength) ? Math.Max(settings.LineLength, 0) : 0;

    static float HalfWidth(in Projectile projectile, Settings settings) => MathF.Abs(projectile.Radius) * (float.IsFinite(settings.RadiusScale) ? Math.Max(settings.RadiusScale, 0) : 0);

    static bool Flight(in Projectile projectile, Settings settings, FightEntity? fighter, out Vector2 start, out Vector2 end)
    {
        start = projectile.Position;
        if (!settings.UseFightData) fighter = null;
        if (FightData.Segment(projectile, fighter, settings) is { } beam)
        {
            (start, end) = beam;
            return true;
        }
        var fightLength = FightData.LineLength(projectile, fighter, settings);
        end = start + Vector2.Normalize(projectile.Direction) * (fightLength ?? Length(settings));
        return fightLength != 0;
    }

    static bool Orbiting(in Projectile projectile, out Vector2 centre, out float radius)
    {
        centre = projectile.OrbitCentre ?? default;
        radius = Vector2.Distance(projectile.Position, centre);
        return projectile.OrbitCentre is not null && float.IsFinite(centre.X) && float.IsFinite(centre.Y) && float.IsFinite(radius) && radius > 0;
    }

    public static bool Inside(in Projectile projectile, Settings settings, Vector2 player, FightEntity? fighter = null)
    {
        if (!Valid(projectile) || !float.IsFinite(player.X) || !float.IsFinite(player.Y)) return false;
        if (Orbiting(projectile, out var centre, out var radius)) return MathF.Abs(Vector2.Distance(player, centre) - radius) <= HalfWidth(projectile, settings) + Slack;
        return Flight(projectile, settings, fighter, out var start, out var end) && Geometry.DistanceToSegment(start, end, player) <= HalfWidth(projectile, settings) + Slack;
    }

    public static bool Draw(in Projectile projectile, Settings settings, Vector2 player, double seconds, HashSet<(int, int)>? rings = null, FightEntity? fighter = null)
    {
        if (!Valid(projectile)) return false;
        var inside = Inside(projectile, settings, player, fighter);
        if (Game.State.Camera is null) return inside;
        var homing = settings.UseFightData && settings.HighlightHoming && FightData.Homing(fighter);
        var colour = inside ? settings.DangerColour : homing ? settings.HomingColour : settings.LineColour;
        var lines = inside ? Groups.LinesDanger : homing ? Groups.LinesHoming : Groups.Lines;
        var halfWidth = HalfWidth(projectile, settings);
        if (Orbiting(projectile, out var centre, out var radius))
        {
            if (rings is null || rings.Add((projectile.OwnerId, (int)(radius / Slack))))
            {
                var ringWidth = float.IsFinite(settings.LineWidth) ? Math.Clamp(settings.LineWidth, 0.5f, 64f) : 1f;
                if (settings.Style == LineStyle.Solid)
                {
                    Span<Vector2> ring = stackalloc Vector2[48];
                    Geometry.RegularPolygon(centre, radius, ring);
                    Ground.Polyline(ring, colour, ringWidth, true, group: lines);
                }
                else Ground.Circle(centre, radius, colour, ringWidth);
                if (settings.ShowHitWidth && halfWidth > 0)
                {
                    Ground.Circle(centre, radius + halfWidth, Groups.Band(colour));
                    if (radius > halfWidth) Ground.Circle(centre, radius - halfWidth, Groups.Band(colour));
                }
            }
            return inside;
        }
        if (!Flight(projectile, settings, fighter, out var start, out var end)) return inside;
        var span = Vector2.Distance(start, end);
        if (settings.ShowHitWidth && halfWidth > 0 && span > 0)
        {
            Span<Vector2> band = stackalloc Vector2[4];
            Geometry.OrientedRect((start + end) / 2, Vector2.Normalize(end - start), halfWidth * 2, span, band);
            Ground.PolygonFilled(band, Groups.Band(colour), group: inside ? Groups.BandsDanger : homing ? Groups.BandsHoming : Groups.Bands);
        }

        var width = float.IsFinite(settings.LineWidth) ? Math.Clamp(settings.LineWidth, 0.5f, 64f) : 1f;
        switch (settings.Style)
        {
            case LineStyle.Gradient:
                Ground.GradientLine(start, end, colour, Color.FromArgb(0, colour), width);
                break;
            case LineStyle.Animated:
                var phase = double.IsFinite(seconds) && float.IsFinite(settings.AnimationSpeed) ? (float)((seconds * settings.AnimationSpeed) % (Dash + Gap)) : 0f;
                Ground.DashedLine(start, end, colour, width, Dash, Gap, phase);
                break;
            default:
                Ground.Line(start, end, colour, width, group: lines);
                break;
        }

        return inside;
    }
}
