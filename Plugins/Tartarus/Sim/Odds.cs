using System;

namespace Tartarus.Sim;

// numbers are the reference sim's DEFAULT_PARAMS (door and Replace weights), not the player-measured shares in rates.json
public static class Odds
{
    public static double[] DoorTierWeights { get; } = [40, 38, 12, 8, 2];

    public static double[] ReplaceTierWeights { get; } = [52, 32, 9, 6, 1.5];

    public static double[,] BeastByTier { get; } = UniformBeasts();

    public static int Draw(Rng rng, ReadOnlySpan<double> weights)
    {
        if (weights.IsEmpty) return 0;
        var total = 0.0;
        foreach (var weight in weights) total += weight;
        if (total <= 0) return rng.Next(weights.Length);
        var roll = rng.NextDouble() * total;
        var last = 0;
        for (var i = 0; i < weights.Length; i++)
        {
            if (weights[i] <= 0) continue;
            last = i;
            roll -= weights[i];
            if (roll < 0) return i;
        }
        return last;
    }

    public static Beast DrawBeast(Rng rng, int tier, double[]? pinned = null)
    {
        if (pinned is not null) return (Beast)Draw(rng, pinned);
        Span<double> weights = stackalloc double[4];
        for (var beast = 0; beast < 4; beast++) weights[beast] = BeastByTier[Math.Clamp(tier, 0, 5), beast];
        return (Beast)Draw(rng, weights);
    }

    static double[,] UniformBeasts()
    {
        var table = new double[6, 4];
        for (var tier = 0; tier < 6; tier++)
            for (var beast = 0; beast < 4; beast++)
                table[tier, beast] = 25;
        return table;
    }
}
