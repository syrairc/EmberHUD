using System;
using System.Drawing;
using System.Numerics;
using Hexa.NET.ImGui;
using EmberHUD;
using EmberHUD.Ui;

namespace NavigationPlugin;

public sealed class Main : Plugin<Settings>
{
    const int TrailDots = 3;
    const float TangentSpan = 20;
    const float HaloWidth = 2.5f;
    const float HaloAlpha = 0.25f;
    const float UnreachableDim = 0.45f;

    readonly Overlay _panel = new();

    internal Paths Paths = new(new Random());
    internal Func<double> Seconds = () => Environment.TickCount64 / 1000.0;
    readonly Vector2[] _particles = new Vector2[256];

    public override void Initialize() => RegisterHotkey("Toggle paths", "Shows or hides the quest paths, legend and compass", () => Settings.General.Show = !Settings.General.Show);

    public override void Tick()
    {
        if (!Settings.General.Show) return;
        Paths.ObstacleRadius = Settings.General.AvoidMapObjects ? Settings.General.ObjectRadius : 0;
        Paths.Update(Game.State);
    }

    internal static bool LegendHidden(Settings settings, GameState state) =>
        !settings.Legend.Show || settings.Legend.HideBehindPanels && state.Ui.AnyFullScreen;

    internal static Vector2? PointAhead(PathState path, Vector2 player, float distance)
    {
        if (path.Points is not { Length: > 1 } points) return null;
        var segment = 0;
        var fraction = 0f;
        var nearest = float.MaxValue;
        for (var i = 0; i < points.Length - 1; i++)
        {
            var run = points[i + 1] - points[i];
            var t = run == Vector2.Zero ? 0 : Math.Clamp(Vector2.Dot(player - points[i], run) / run.LengthSquared(), 0, 1);
            var gap = Vector2.DistanceSquared(player, points[i] + run * t);
            if (gap >= nearest) continue;
            (nearest, segment, fraction) = (gap, i, t);
        }

        var at = points[segment] + (points[segment + 1] - points[segment]) * fraction;
        var remaining = distance;
        for (var i = segment; i < points.Length - 1; i++)
        {
            var run = points[i + 1] - at;
            var length = run.Length();
            if (length > 0 && remaining <= length) return at + run / length * remaining;
            remaining -= length;
            at = points[i + 1];
        }

        return at;
    }

    internal static Vector2? CompassDirection(PathState path, Vector2 player, float lookahead, float radius)
    {
        if (!float.IsFinite(player.X) || !float.IsFinite(player.Y) || !float.IsFinite(path.Target.X) || !float.IsFinite(path.Target.Y) || !float.IsFinite(lookahead) || !float.IsFinite(radius)) return null;
        if (Vector2.Distance(player, path.Target) <= radius) return null;
        var toward = (PointAhead(path, player, lookahead) ?? path.Target) - player;
        return toward.LengthSquared() > 1e-6f ? Vector2.Normalize(toward) : null;
    }

    public override void Render()
    {
        var state = Game.State;
        var general = Settings.General;
        if (!general.Show || Paths.Current.Count == 0) return;
        var covered = state.Ui.AnyFullScreen;
        DrawPaths(state, covered);
        if (!covered && Settings.Compass.Show && state.Camera is not null && state.Player is { } player) DrawCompass(player.Position);
        if (!LegendHidden(Settings, state)) DrawLegend();
    }

    void DrawPaths(GameState state, bool coveredByPanel)
    {
        var general = Settings.General;
        var seconds = Seconds();
        var height = state.Player?.Position.Z ?? 0;
        foreach (var path in Paths.Current)
        {
            if (path.Points is not { Length: > 1 } points) continue;
            if (!coveredByPanel && general.Ground && state.Camera is not null) DrawGround(path, points, seconds);
            if (general.Minimap && Map.View is { } map) DrawMinimap(path, points, seconds, map, height);
        }
    }

    void DrawCompass(Vector3 player)
    {
        var compass = Settings.Compass;
        var from = new Vector2(player.X, player.Y);
        Span<Vector2> arrow = stackalloc Vector2[4];
        foreach (var path in Paths.Current)
        {
            if (CompassDirection(path, from, compass.Lookahead, compass.Radius) is not { } direction) continue;
            var count = Shapes.Arrow(from + direction * compass.Radius, direction, compass.Size, arrow);
            if (count > 2) Ground.PolygonFilled(arrow[..count], Faded(path.Colour, compass.Opacity * (path.Points is null ? UnreachableDim : 1)));
        }
    }

    void DrawLegend()
    {
        var legend = Settings.Legend;
        var rows = Legend.Rows(Paths.Current);
        var rowHeight = Draw.RowHeight(legend.Text);

        _panel.Locked = legend.Lock;
        _panel.MoveHeld = legend.MoveKey.Key != Key.None && Input.IsDown(legend.MoveKey.Key);
        var origin = new Vector2(legend.X, legend.Y);
        var size = new Vector2(legend.Width, rowHeight * rows.Count);
        var (hovered, edge, active) = _panel.Handle("navigation", ref origin, size, ref legend.X, ref legend.Y, ref legend.Width, LegendTab.MinWidth, LegendTab.MaxWidth);
        if (hovered || active) Overlay.ClickBlocker("navigation", origin, size);

        var at = origin;
        foreach (var row in rows) at.Y += Draw.Row(at, size.X, row.Name, row.Distance, legend.Text, row.Colour, row.Dimmed);
        if (_panel.Movable) Overlay.DragHint(origin, origin + size, active, hovered, edge);
    }

    void DrawGround(PathState path, Vector2[] points, double seconds)
    {
        var style = Settings.GroundStyle;
        var colour = style.ColourFor(path);
        if (style.BaseLine > 0) Ground.Polyline(points, Faded(colour, style.Opacity * 0.4f), style.BaseLine);
        if (Settings.GroundShape == GroundShape.Dot)
        {
            Dots(path, points, style, seconds, (at, scale, alpha) => Ground.Dot(at, style.Size * scale, Faded(colour, style.Opacity * alpha)));
            return;
        }

        var length = style.Trail > 0 ? style.Trail : style.Size * 4;
        Span<Vector2> polygon = stackalloc Vector2[3];
        var count = Particles.Positions(points, path.Length, style.Spacing, style.Speed, seconds, _particles);
        for (var i = 0; i < count; i++)
        {
            var head = _particles[i];
            var behind = Particles.PointAt(points, Math.Max(Particles.Wrap(i * style.Spacing + style.Speed * seconds, path.Length) - TangentSpan, 0));
            var direction = head - behind;
            if (direction == Vector2.Zero) continue;
            direction = Vector2.Normalize(direction);
            if (Settings.GroundShape == GroundShape.Chevron)
            {
                if (Shapes.Chevron(head, direction, style.Size, polygon) == 3) Ground.PolygonFilled(polygon, Faded(colour, style.Opacity));
                continue;
            }

            if (Shapes.Streak(head, direction, length, style.Size * HaloWidth, polygon) == 3) Ground.PolygonFilled(polygon, Faded(colour, style.Opacity * HaloAlpha));
            if (Shapes.Streak(head, direction, length, style.Size, polygon) == 3) Ground.PolygonFilled(polygon, Faded(colour, style.Opacity));
        }
    }

    void DrawMinimap(PathState path, Vector2[] points, double seconds, MapView map, float height)
    {
        var style = Settings.MinimapStyle;
        var viewport = ImGui.GetIO().DisplaySize;
        using var layer = Draw.Layer(DrawLayer.Middle);
        if (style.BaseLine > 0)
        {
            var screen = new Vector2[points.Length];
            for (var i = 0; i < points.Length; i++) screen[i] = map.ToScreen(new Vector3(points[i], height));
            Draw.Polyline(screen, Faded(style.ColourFor(path), style.Opacity * 0.4f), style.BaseLine);
        }
        Dots(path, points, style, seconds, (world, scale, alpha) =>
        {
            var at = map.ToScreen(new Vector3(world, height));
            var radius = style.Size * scale;
            if (at.X < -radius || at.Y < -radius || at.X > viewport.X + radius || at.Y > viewport.Y + radius) return;
            Draw.CircleFilled(at, radius, Faded(style.ColourFor(path), style.Opacity * alpha), 12);
        });
    }

    void Dots(PathState path, Vector2[] points, SurfaceStyle style, double seconds, Action<Vector2, float, float> dot)
    {
        var count = Particles.Positions(points, path.Length, style.Spacing, style.Speed, seconds, _particles);
        for (var i = 0; i < count; i++)
        {
            dot(_particles[i], 1, 1);
            if (style.Trail <= 0) continue;
            var head = Particles.Wrap(i * style.Spacing + style.Speed * seconds, path.Length);
            for (var step = 1; step <= TrailDots; step++)
            {
                var behind = head - style.Trail * step / TrailDots;
                if (behind < 0) break;
                var fade = 1 - (float)step / (TrailDots + 1);
                dot(Particles.PointAt(points, behind), fade, fade * 0.6f);
            }
        }
    }

    static Color Faded(Color colour, float opacity) => Color.FromArgb((int)Math.Clamp(colour.A * opacity, 0, 255), colour);
}
