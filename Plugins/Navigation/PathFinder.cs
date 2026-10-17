using System;
using System.Collections.Generic;
using System.Numerics;
using EmberHUD;

namespace NavigationPlugin;

internal static class PathFinder
{
    const int SnapRadius = 24;
    static readonly float Diagonal = MathF.Sqrt(2);

    [ThreadStatic] static float[]? costs;
    [ThreadStatic] static int[]? parents;

    public static Vector2[]? Find(Terrain terrain, Vector2 from, Vector2 to)
    {
        if (terrain?.Walkable is null || terrain.Cols <= 0 || terrain.Rows <= 0 || (long)terrain.Cols * terrain.Rows > int.MaxValue) return null;
        if (!float.IsFinite(terrain.CellSize) || terrain.CellSize <= 0 || !IsFinite(terrain.Origin) || !IsFinite(from) || !IsFinite(to)) return null;

        var target = CellOf(terrain, to);
        if (target.Col < 0 || target.Row < 0 || target.Col >= terrain.Cols || target.Row >= terrain.Rows) return null;
        var start = CellOf(terrain, from);
        start = (Math.Clamp(start.Col, 0, terrain.Cols - 1), Math.Clamp(start.Row, 0, terrain.Rows - 1));

        if (Snap(terrain, start, from) is not { } begin || Snap(terrain, target, to) is not { } end) return null;
        if (Search(terrain, begin, end) is not { } cells) return null;

        var corners = new List<(int Col, int Row)> { cells[0] };
        var anchor = 0;
        for (var i = 1; i < cells.Count; i++)
        {
            if (i + 1 < cells.Count && Clear(terrain, cells[anchor], cells[i + 1])) continue;
            corners.Add(cells[i]);
            anchor = i;
        }

        var path = new Vector2[corners.Count];
        for (var i = 0; i < path.Length; i++) path[i] = terrain.CellCenter(corners[i].Col, corners[i].Row);
        return path;
    }

    public static float Length(ReadOnlySpan<Vector2> path)
    {
        var total = 0f;
        for (var i = 1; i < path.Length; i++) total += Vector2.Distance(path[i - 1], path[i]);
        return total;
    }

    static bool IsFinite(Vector2 point) => float.IsFinite(point.X) && float.IsFinite(point.Y);

    static (int Col, int Row) CellOf(Terrain terrain, Vector2 point)
    {
        var col = MathF.Floor((point.X - terrain.Origin.X) / terrain.CellSize);
        var row = MathF.Floor((point.Y - terrain.Origin.Y) / terrain.CellSize);
        return ((int)Math.Clamp(col, -1e6f, 1e6f), (int)Math.Clamp(row, -1e6f, 1e6f));
    }

    static (int Col, int Row)? Snap(Terrain terrain, (int Col, int Row) cell, Vector2 point)
    {
        (int Col, int Row)? best = null;
        var bestDistance = float.MaxValue;
        for (var radius = 0; radius <= SnapRadius && best is null; radius++)
            for (var row = cell.Row - radius; row <= cell.Row + radius; row++)
                for (var col = cell.Col - radius; col <= cell.Col + radius; col++)
                {
                    if (Math.Max(Math.Abs(col - cell.Col), Math.Abs(row - cell.Row)) != radius || !terrain.IsWalkable(col, row)) continue;
                    var distance = Vector2.DistanceSquared(terrain.CellCenter(col, row), point);
                    if (distance >= bestDistance) continue;
                    bestDistance = distance;
                    best = (col, row);
                }
        return best;
    }

    static List<(int Col, int Row)>? Search(Terrain terrain, (int Col, int Row) start, (int Col, int Row) end)
    {
        var cols = terrain.Cols;
        var size = cols * terrain.Rows;
        if (costs is null || parents is null || costs.Length != size) (costs, parents) = (new float[size], new int[size]);
        Array.Fill(costs, float.MaxValue);
        var cost = costs;
        var parent = parents;

        var startIndex = start.Row * cols + start.Col;
        var endIndex = end.Row * cols + end.Col;
        cost[startIndex] = 0;
        parent[startIndex] = -1;
        var open = new PriorityQueue<int, float>();
        open.Enqueue(startIndex, Heuristic(start.Col, start.Row, end));

        while (open.TryDequeue(out var index, out var priority))
        {
            if (index == endIndex) break;
            var (col, row) = (index % cols, index / cols);
            if (priority > cost[index] + Heuristic(col, row, end) + 1e-3f) continue;

            for (var dRow = -1; dRow <= 1; dRow++)
                for (var dCol = -1; dCol <= 1; dCol++)
                {
                    if (dCol == 0 && dRow == 0) continue;
                    var (nextCol, nextRow) = (col + dCol, row + dRow);
                    if (!terrain.IsWalkable(nextCol, nextRow)) continue;
                    var diagonal = dCol != 0 && dRow != 0;
                    if (diagonal && (!terrain.IsWalkable(nextCol, row) || !terrain.IsWalkable(col, nextRow))) continue;
                    var next = nextRow * cols + nextCol;
                    var nextCost = cost[index] + (diagonal ? Diagonal : 1);
                    if (nextCost >= cost[next]) continue;
                    cost[next] = nextCost;
                    parent[next] = index;
                    open.Enqueue(next, nextCost + Heuristic(nextCol, nextRow, end));
                }
        }

        if (cost[endIndex] == float.MaxValue) return null;
        var cells = new List<(int Col, int Row)>();
        for (var index = endIndex; index >= 0; index = parent[index]) cells.Add((index % cols, index / cols));
        cells.Reverse();
        return cells;
    }

    static float Heuristic(int col, int row, (int Col, int Row) end)
    {
        var dx = Math.Abs(col - end.Col);
        var dy = Math.Abs(row - end.Row);
        return dx + dy + (Diagonal - 2) * Math.Min(dx, dy);
    }

    static bool Clear(Terrain terrain, (int Col, int Row) a, (int Col, int Row) b)
    {
        var (nx, ny) = (Math.Abs(b.Col - a.Col), Math.Abs(b.Row - a.Row));
        var (stepX, stepY) = (Math.Sign(b.Col - a.Col), Math.Sign(b.Row - a.Row));
        var (col, row) = a;
        for (int x = 0, y = 0; x < nx || y < ny;)
        {
            var side = (1 + 2 * x) * ny - (1 + 2 * y) * nx;
            if (side == 0)
            {
                if (!terrain.IsWalkable(col + stepX, row) || !terrain.IsWalkable(col, row + stepY)) return false;
                (col, row, x, y) = (col + stepX, row + stepY, x + 1, y + 1);
            }
            else if (side < 0)
                (col, x) = (col + stepX, x + 1);
            else
                (row, y) = (row + stepY, y + 1);
            if (!terrain.IsWalkable(col, row)) return false;
        }
        return true;
    }
}
