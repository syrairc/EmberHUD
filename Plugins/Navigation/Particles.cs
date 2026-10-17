using System;
using System.Numerics;

namespace NavigationPlugin;

internal static class Particles
{
    public static int Positions(ReadOnlySpan<Vector2> path, float length, float spacing, float speed, double seconds, Span<Vector2> into)
    {
        var offset = speed * seconds;
        if (path.Length < 2 || !float.IsFinite(length) || !float.IsFinite(spacing) || !double.IsFinite(offset) || spacing <= 0 || length < spacing) return 0;
        var count = Math.Min((int)(length / spacing), into.Length);
        for (var i = 0; i < count; i++) into[i] = PointAt(path, Wrap(i * spacing + offset, length));
        return count;
    }

    public static float Wrap(double distance, float length) => (float)((distance % length + length) % length);

    public static Vector2 PointAt(ReadOnlySpan<Vector2> path, float distance)
    {
        for (var i = 1; i < path.Length; i++)
        {
            var segment = Vector2.Distance(path[i - 1], path[i]);
            if (distance <= segment && segment > 0) return Vector2.Lerp(path[i - 1], path[i], distance / segment);
            distance -= segment;
        }
        return path[^1];
    }
}
