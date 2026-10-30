using System;
using System.Drawing;
using System.Numerics;
using EmberHUD;
using Hexa.NET.ImGui;

namespace Threats;

public static class Telegraph
{
    const int Segments = 48;

    public static bool Active(Cast cast) => cast.State <= 1 && cast.SecondsLeft > 0;

    public static int Area(Cast cast, float radius, Span<Vector2> into)
    {
        if (into.Length < Segments) return 0;
        Geometry.RegularPolygon(cast.Target, radius, into[..Segments]);
        return Segments;
    }

    public static bool Inside(Cast cast, Settings settings, Vector2 player)
    {
        Span<Vector2> area = stackalloc Vector2[Segments];
        return Geometry.Contains(area[..Area(cast, settings.PlaceholderRadius, area)], player);
    }

    public static bool Draw(Entity caster, Settings settings, Vector2 player)
    {
        if (caster.Cast is not { } cast || !Active(cast) || settings.TooShort(cast)) return false;
        var inside = Inside(cast, settings, player);
        if (Game.State.Camera is not { } camera) return inside;
        var origin = new Vector2(caster.Position.X, caster.Position.Y);
        Span<Vector2> area = stackalloc Vector2[Segments];
        area = area[..Area(cast, settings.PlaceholderRadius, area)];
        if (area.Length == 0) return inside;
        var outline = inside ? settings.DangerColour : settings.TelegraphColour;

        var reach = 0f;
        foreach (var vertex in area) reach = MathF.Max(reach, Vector2.Distance(origin, vertex));
        var progress = float.IsFinite(cast.Progress) ? Math.Clamp(cast.Progress, 0f, 1f) : 0f;
        if (progress > 0 && float.IsFinite(reach) && float.IsFinite(origin.X) && float.IsFinite(origin.Y))
        {
            Span<Vector2> disc = stackalloc Vector2[Segments];
            Geometry.RegularPolygon(origin, progress * reach, disc);
            Span<Vector2> clipped = stackalloc Vector2[Segments * 2];
            var count = Geometry.ClipConvex(area, disc, clipped);
            if (count >= 3) Ground.PolygonFilled(clipped[..count], Groups.Fill(inside ? settings.DangerColour : settings.TelegraphFillColour, settings), group: inside ? Groups.TelegraphFillDanger : Groups.TelegraphFill);
        }

        var travelled = 0f;
        for (var i = 0; i < area.Length; i++)
        {
            var next = area[(i + 1) % area.Length];
            Ground.DashedLine(area[i], next, outline, 3, 30, 30, -travelled);
            travelled += Vector2.Distance(area[i], next);
        }

        if (settings.ShowCountdown) Countdown(cast, settings, camera);
        return inside;
    }

    static void Countdown(Cast cast, Settings settings, Camera camera)
    {
        Label($"{cast.SecondsLeft:0.0}", cast.Target, settings.CountdownSize, camera);
    }

    internal static void Label(string text, Vector2 at, float size, Camera camera)
    {
        if (Ground.Text(text, at, size, camera.Rotation.Y + 90, Color.White)) return;
        var screen = camera.ToScreen(new Vector3(at, Game.State.Player?.Position.Z ?? 0), ImGui.GetIO().DisplaySize);
        if (screen is { } point) EmberHUD.Draw.Text(text, point, Color.White, Align.Center | Align.Middle);
    }
}
