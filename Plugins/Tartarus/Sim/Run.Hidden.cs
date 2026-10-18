namespace Tartarus.Sim;

public sealed partial class Run
{
    internal Run Fork(Rng seed)
    {
        var copy = Clone();
        copy.rng = seed;
        return copy;
    }

    internal Run Reroll(Rng seed)
    {
        var copy = Fork(seed);
        for (var cell = 1; cell < copy.Cells.Length; cell++)
        {
            var hidden = copy.Cells[cell];
            if (hidden.Open || hidden.Markers.HasFlag(Marker.Nest)) continue;
            if (!hidden.Known && !hidden.Markers.HasFlag(Marker.Scratch)) copy.Cells[cell] = hidden with { Tier = -1 };
        }
        return copy;
    }
}
