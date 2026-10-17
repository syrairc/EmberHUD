using System;
using System.Numerics;

namespace NavigationPlugin;

internal static class Shapes
{
    public static int Chevron(Vector2 centre, Vector2 direction, float size, Span<Vector2> into)
    {
        if (!Valid(centre, direction) || !float.IsFinite(size) || size <= 0 || into.Length < 3) return 0;
        var side = new Vector2(-direction.Y, direction.X) * size;
        var half = direction * size / 2;
        into[0] = centre + half;
        into[1] = centre - half + side;
        into[2] = centre - half - side;
        return 3;
    }

    public static int Streak(Vector2 head, Vector2 direction, float length, float width, Span<Vector2> into)
    {
        if (!Valid(head, direction) || !float.IsFinite(length) || !float.IsFinite(width) || length <= 0 || width <= 0 || into.Length < 3) return 0;
        var side = new Vector2(-direction.Y, direction.X) * width / 2;
        into[0] = head + side;
        into[1] = head - side;
        into[2] = head - direction * length;
        return 3;
    }

    public static int Arrow(Vector2 centre, Vector2 direction, float size, Span<Vector2> into)
    {
        if (!Valid(centre, direction) || !float.IsFinite(size) || size <= 0 || into.Length < 4) return 0;
        var side = new Vector2(-direction.Y, direction.X) * size * 0.3f;
        into[0] = centre + direction * size / 2;
        into[1] = centre - direction * size * 0.2f + side;
        into[2] = centre - direction * size / 2;
        into[3] = centre - direction * size * 0.2f - side;
        return 4;
    }

    static bool Valid(Vector2 at, Vector2 direction) =>
        float.IsFinite(at.X) && float.IsFinite(at.Y) && float.IsFinite(direction.X) && float.IsFinite(direction.Y) && direction.LengthSquared() > 1e-12f;
}
