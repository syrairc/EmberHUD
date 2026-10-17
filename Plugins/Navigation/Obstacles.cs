using System;
using System.Collections;
using System.Collections.Generic;
using System.Numerics;
using EmberHUD;

namespace NavigationPlugin;

internal static class Obstacles
{
    public static Terrain Stamp(Terrain terrain, IReadOnlyList<Vector2> centres, float radius)
    {
        if (centres.Count == 0 || !(radius > 0) || !float.IsFinite(radius) || terrain.Walkable is null) return terrain;
        if (!float.IsFinite(terrain.CellSize) || terrain.CellSize <= 0 || !float.IsFinite(terrain.Origin.X) || !float.IsFinite(terrain.Origin.Y)) return terrain;

        var walkable = (BitArray)terrain.Walkable.Clone();
        var size = terrain.CellSize;
        foreach (var centre in centres)
        {
            if (!float.IsFinite(centre.X) || !float.IsFinite(centre.Y)) continue;
            var minCol = Cell((centre.X - radius - terrain.Origin.X) / size - 0.5f, terrain.Cols, false);
            var maxCol = Cell((centre.X + radius - terrain.Origin.X) / size - 0.5f, terrain.Cols, true);
            var minRow = Cell((centre.Y - radius - terrain.Origin.Y) / size - 0.5f, terrain.Rows, false);
            var maxRow = Cell((centre.Y + radius - terrain.Origin.Y) / size - 0.5f, terrain.Rows, true);
            for (var row = minRow; row <= maxRow; row++)
                for (var col = minCol; col <= maxCol; col++)
                {
                    var index = row * terrain.Cols + col;
                    if (index >= walkable.Length || Vector2.Distance(terrain.CellCenter(col, row), centre) > radius) continue;
                    walkable[index] = false;
                }
        }
        return terrain with { Walkable = walkable };
    }

    static int Cell(float position, int count, bool up) => (int)Math.Clamp(up ? MathF.Ceiling(position) : MathF.Floor(position), 0, Math.Max(count - 1, 0));
}
