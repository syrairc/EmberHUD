using System.Linq;
using System.Numerics;

namespace EmberAuras;

public static class GroupLayout
{
    public static Vector2[] Place(Group group, Vector2[] sizes)
    {
        var count = sizes.Length;
        var positions = new Vector2[count];
        if (count == 0) return positions;

        var vertical = group.Growth is Growth.Down or Growth.Up or Growth.OutwardVertical;
        var outward = group.Growth is Growth.OutwardHorizontal or Growth.OutwardVertical;
        var reversed = group.Growth is Growth.Left or Growth.Up;
        var align = outward ? GroupAlign.Center : group.Align;

        float Main(Vector2 v) => vertical ? v.Y : v.X;
        var total = sizes.Sum(Main) + group.Spacing * (count - 1);
        var anchor = vertical ? group.Y : group.X;
        var start = align switch
        {
            GroupAlign.Center => anchor - total / 2,
            GroupAlign.End => anchor - total,
            _ => anchor,
        };

        var order = outward
            ? Enumerable.Range(0, count).Where(i => i % 2 == 1).Reverse().Concat(Enumerable.Range(0, count).Where(i => i % 2 == 0))
            : reversed ? Enumerable.Range(0, count).Reverse() : Enumerable.Range(0, count);

        var cursor = start;
        foreach (var i in order)
        {
            positions[i] = vertical ? new Vector2(group.X, cursor) : new Vector2(cursor, group.Y);
            cursor += Main(sizes[i]) + group.Spacing;
        }
        return positions;
    }

    public static (Vector2 Min, Vector2 Max) Bounds(Vector2[] positions, Vector2[] sizes)
    {
        if (positions.Length == 0) return (Vector2.Zero, Vector2.Zero);
        var min = positions[0];
        var max = positions[0] + sizes[0];
        for (var i = 1; i < positions.Length; i++)
        {
            min = Vector2.Min(min, positions[i]);
            max = Vector2.Max(max, positions[i] + sizes[i]);
        }
        return (min, max);
    }
}
