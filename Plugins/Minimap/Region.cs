using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Numerics;
using EmberHUD;

namespace Minimap;

public sealed record Region(int AreaId, Terrain? Source, ExploredTrail? Trail, BitArray Explored, (int Col, int Row)? PlayerCell, Color RegionFill, Color ExploredFill, byte[] Pixels, int Width, int Height, Vector2[] Corners, Polyline[] ExploredOutline)
{
    public const float BrushReach = 0.65f;

    public static readonly Region Empty = new(-1, null, null, new BitArray(0), null, default, default, [], 0, 0, [], []);

    public bool Matches(int areaId, Terrain? terrain) => AreaId == areaId && ReferenceEquals(Source, terrain);

    public bool Stale(ExploredTrail? trail, (int Col, int Row)? playerCell, Color regionFill, Color exploredFill) =>
        regionFill.ToArgb() != RegionFill.ToArgb() || exploredFill.ToArgb() != ExploredFill.ToArgb()
        || (trail is null) != (Trail is null)
        || trail is not null && (trail.Count != Trail!.Count || !trail.Radius.Equals(Trail.Radius) || playerCell != PlayerCell);

    public Polyline[] Unexplored(Polyline[] lines)
    {
        if (Trail is null || Source is not { } terrain || Explored.Length != terrain.Cols * terrain.Rows || !(terrain.CellSize > 0)) return lines;

        var kept = new List<Polyline>();
        var points = new List<Vector2>();
        var corner = new List<bool>();
        var run = new List<Vector2>();
        foreach (var line in lines)
        {
            points.Clear();
            corner.Clear();
            var count = line.Points.Length;
            var segments = line.Closed ? count : count - 1;
            if (segments <= 0) continue;

            for (var i = 0; i < segments; i++)
            {
                var start = line.Points[i];
                var end = line.Points[(i + 1) % count];
                var pieces = Math.Max(1, (int)MathF.Ceiling(Vector2.Distance(start, end) / (terrain.CellSize / 2)));
                for (var j = 0; j < pieces; j++)
                {
                    points.Add(Vector2.Lerp(start, end, (float)j / pieces));
                    corner.Add(j == 0);
                }
            }
            if (!line.Closed)
            {
                points.Add(line.Points[^1]);
                corner.Add(true);
            }

            var steps = line.Closed ? points.Count : points.Count - 1;
            var dropped = -1;
            for (var i = 0; i < steps; i++)
                if (ExploredAt(terrain, (points[i] + points[(i + 1) % points.Count]) / 2)) dropped = i;
            if (dropped < 0)
            {
                kept.Add(line);
                continue;
            }

            var offset = line.Closed ? dropped : -1;
            var runStart = -1;
            for (var k = 1; k <= steps + 1; k++)
            {
                var i = (offset + k) % steps;
                var open = k <= steps && !ExploredAt(terrain, (points[i] + points[(i + 1) % points.Count]) / 2);
                if (open && runStart < 0) runStart = k;
                if (open || runStart < 0) continue;
                run.Clear();
                for (var m = runStart; m < k; m++)
                {
                    var at = (offset + m) % steps;
                    if (m == runStart || corner[at]) run.Add(points[at]);
                }
                run.Add(points[((offset + k - 1) % steps + 1) % points.Count]);
                kept.Add(new Polyline([.. run], false));
                runStart = -1;
            }
        }
        return [.. kept];
    }

    bool ExploredAt(Terrain terrain, Vector2 at) =>
        CellOf(terrain, at) is { } cell && Explored[Math.Clamp(cell.Row, 0, terrain.Rows - 1) * terrain.Cols + Math.Clamp(cell.Col, 0, terrain.Cols - 1)];

    public static (int Col, int Row)? CellOf(Terrain terrain, Vector2? at)
    {
        if (at is not { } point || !float.IsFinite(point.X) || !float.IsFinite(point.Y) || !(terrain.CellSize > 0)) return null;
        return ((int)MathF.Floor((point.X - terrain.Origin.X) / terrain.CellSize), (int)MathF.Floor((point.Y - terrain.Origin.Y) / terrain.CellSize));
    }

    public static Region Blank(int areaId, Terrain terrain, ExploredTrail? trail, Vector2? player, Color regionFill, Color exploredFill) =>
        new(areaId, terrain, trail, new BitArray(0), CellOf(terrain, player), regionFill, exploredFill, [], 0, 0, [], []);

    public static Region Build(int areaId, Terrain terrain, ExploredTrail? trail, Vector2? player, Color regionFill, Color exploredFill, Region? previous, byte[]? reuse = null)
    {
        var size = terrain.CellSize;
        if (!float.IsFinite(size) || size <= 0 || terrain.Cols <= 0 || terrain.Rows <= 0) return Blank(areaId, terrain, trail, player, regionFill, exploredFill);

        var playerCell = CellOf(terrain, player);
        var everything = trail is null;
        var explored = Stamp(terrain, trail, player, previous);
        var pixels = Texels(terrain, explored, everything, regionFill, exploredFill, reuse, out var width);
        var right = terrain.Origin.X + terrain.Cols * size;
        var bottom = terrain.Origin.Y + terrain.Rows * size;
        Vector2[] corners = [terrain.Origin, new(right, terrain.Origin.Y), new(right, bottom), new(terrain.Origin.X, bottom)];
        Polyline[] outline = everything ? [] : terrain.Outline((col, row) => terrain.IsWalkable(col, row) && explored[row * terrain.Cols + col]);
        return new Region(areaId, terrain, trail, explored, playerCell, regionFill, exploredFill, pixels, width, terrain.Rows, corners, outline);
    }

    static BitArray Stamp(Terrain terrain, ExploredTrail? trail, Vector2? player, Region? previous)
    {
        var cells = terrain.Cols * terrain.Rows;
        if (trail is null || !float.IsFinite(trail.Radius) || trail.Radius <= 0) return new BitArray(cells);

        var keep = previous is { Trail: { } old } && ReferenceEquals(previous.Source, terrain) && previous.Explored.Length == cells && old.Radius.Equals(trail.Radius) && old.Count <= trail.Count;
        var explored = keep ? new BitArray(previous!.Explored) : new BitArray(cells);
        var reach = trail.Radius * BrushReach;
        for (var i = keep ? previous!.Trail!.Count : 0; i < trail.Count; i++) Disc(explored, terrain, trail.Buffer[i], reach);
        if (player is { } at) Disc(explored, terrain, at, reach);
        return explored;
    }

    static void Disc(BitArray explored, Terrain terrain, Vector2 at, float radius)
    {
        if (!float.IsFinite(at.X) || !float.IsFinite(at.Y)) return;
        var size = terrain.CellSize;
        var rowFrom = Clamp((at.Y - radius - terrain.Origin.Y) / size, terrain.Rows);
        var rowTo = Clamp((at.Y + radius - terrain.Origin.Y) / size, terrain.Rows);
        var colFrom = Clamp((at.X - radius - terrain.Origin.X) / size, terrain.Cols);
        var colTo = Clamp((at.X + radius - terrain.Origin.X) / size, terrain.Cols);
        var reach = radius * radius;
        for (var row = rowFrom; row <= rowTo; row++)
            for (var col = colFrom; col <= colTo; col++)
                if (Vector2.DistanceSquared(terrain.CellCenter(col, row), at) <= reach) explored[row * terrain.Cols + col] = true;
    }

    static int Clamp(float cell, int count) => (int)Math.Clamp(MathF.Floor(cell), 0, count - 1);

    static byte[] Texels(Terrain terrain, BitArray explored, bool everything, Color regionFill, Color exploredFill, byte[]? reuse, out int width)
    {
        width = terrain.Cols;
        var length = width * terrain.Rows * 4;
        var pixels = reuse is not null && reuse.Length == length ? reuse : new byte[length];
        var clear = Color.FromArgb(0, regionFill);
        for (var row = 0; row < terrain.Rows; row++)
        {
            var line = row * width * 4;
            for (var x = 0; x < width; x++) Put(pixels, line + x * 4, clear);
            for (var col = 0; col < terrain.Cols; col++)
            {
                if (!terrain.IsWalkable(col, row)) continue;
                var colour = everything || explored[row * terrain.Cols + col] ? exploredFill : regionFill;
                Put(pixels, line + col * 4, colour);
            }
        }
        return pixels;
    }

    static void Put(byte[] pixels, int at, Color colour)
    {
        pixels[at] = colour.R;
        pixels[at + 1] = colour.G;
        pixels[at + 2] = colour.B;
        pixels[at + 3] = colour.A;
    }
}

public sealed record Walls(int AreaId, Terrain? Source, Vector2 Min, Vector2 Max, Polyline[] RegionOutline)
{
    public static readonly Walls Empty = new(-1, null, default, default, []);

    public bool Matches(int areaId, Terrain? terrain) => AreaId == areaId && ReferenceEquals(Source, terrain);

    public static Walls Build(int areaId, Terrain? terrain)
    {
        if (terrain is null) return new Walls(areaId, null, default, default, []);

        var outline = terrain.Outline();
        var any = false;
        var min = default(Vector2);
        var max = default(Vector2);
        foreach (var line in outline)
            foreach (var point in line.Points)
            {
                min = any ? Vector2.Min(min, point) : point;
                max = any ? Vector2.Max(max, point) : point;
                any = true;
            }
        return new Walls(areaId, terrain, min, max, outline);
    }
}
