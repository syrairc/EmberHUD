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

    static Vector2 End(in Projectile projectile, Settings settings) => projectile.Position + Vector2.Normalize(projectile.Direction) * Length(settings);

    static bool Orbiting(in Projectile projectile, out Vector2 centre, out float radius)
    {
        centre = projectile.OrbitCentre ?? default;
        radius = Vector2.Distance(projectile.Position, centre);
        return projectile.OrbitCentre is not null && float.IsFinite(centre.X) && float.IsFinite(centre.Y) && float.IsFinite(radius) && radius > 0;
    }

    public static bool Inside(in Projectile projectile, Settings settings, Vector2 player)
    {
        if (!Valid(projectile) || !float.IsFinite(player.X) || !float.IsFinite(player.Y)) return false;
        if (Orbiting(projectile, out var centre, out var radius)) return MathF.Abs(Vector2.Distance(player, centre) - radius) <= HalfWidth(projectile, settings) + Slack;
        return Geometry.DistanceToSegment(projectile.Position, End(projectile, settings), player) <= HalfWidth(projectile, settings) + Slack;
    }

    public static bool Draw(in Projectile projectile, Settings settings, Vector2 player, double seconds, HashSet<(int, int)>? rings = null)
    {
        if (!Valid(projectile)) return false;
        var inside = Inside(projectile, settings, player);
        if (Game.State.Camera is null) return inside;
        var colour = inside ? settings.DangerColour : settings.LineColour;
        var halfWidth = HalfWidth(projectile, settings);
        if (Orbiting(projectile, out var centre, out var radius))
        {
            if (rings is null || rings.Add((projectile.OwnerId, (int)(radius / Slack))))
            {
                var ringWidth = float.IsFinite(settings.LineWidth) ? Math.Clamp(settings.LineWidth, 0.5f, 64f) : 1f;
                Ground.Circle(centre, radius, colour, ringWidth);
                if (settings.ShowHitWidth && halfWidth > 0)
                {
                    Ground.Circle(centre, radius + halfWidth, Color.FromArgb(colour.A / 4, colour));
                    if (radius > halfWidth) Ground.Circle(centre, radius - halfWidth, Color.FromArgb(colour.A / 4, colour));
                }
            }
            return inside;
        }
        var end = End(projectile, settings);
        if (settings.ShowHitWidth && halfWidth > 0)
        {
            Span<Vector2> band = stackalloc Vector2[4];
            Geometry.OrientedRect((projectile.Position + end) / 2, Vector2.Normalize(projectile.Direction), halfWidth * 2, Length(settings), band);
            Ground.PolygonFilled(band, Color.FromArgb(colour.A / 4, colour));
        }

        var width = float.IsFinite(settings.LineWidth) ? Math.Clamp(settings.LineWidth, 0.5f, 64f) : 1f;
        switch (settings.Style)
        {
            case LineStyle.Gradient:
                Ground.GradientLine(projectile.Position, end, colour, Color.FromArgb(0, colour), width);
                break;
            case LineStyle.Animated:
                var phase = double.IsFinite(seconds) && float.IsFinite(settings.AnimationSpeed) ? (float)((seconds * settings.AnimationSpeed) % (Dash + Gap)) : 0f;
                Ground.DashedLine(projectile.Position, end, colour, width, Dash, Gap, phase);
                break;
            default:
                Ground.Line(projectile.Position, end, colour, width);
                break;
        }

        return inside;
    }
}
