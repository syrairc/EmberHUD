using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace Tartarus.Sim;

public sealed record Ranked(Move Move, double Value, double Spread, double GapSe = 0, bool Quick = false);

public static class Solver
{
    const int StepLimit = 5000;

    public static Ranked[] Rank(Run start, int playouts, Rng rng, CancellationToken cancel = default, Action<int>? progress = null)
    {
        var moves = start.Legal();
        if (moves.Count <= 1) return [.. moves.Select(move => new Ranked(move, 0, 0, 0, true))];
        var pick = Policy.Choose(start, new Rng(1));
        var values = moves.Select(_ => new List<int>()).ToArray();
        var best = new double[moves.Count];
        while (values[0].Count < playouts && !cancel.IsCancellationRequested)
        {
            var seed = ((ulong)rng.Next() << 32) | rng.Next();
            for (var i = 0; i < moves.Count; i++)
            {
                var end = Playout(start, moves[i], seed);
                values[i].Add(end.Chromatics);
                best[i] += end.Chromatics > 0 ? Tiers.Chromatic : end.Cells.Where(cell => cell.Open).Select(cell => cell.Tier).DefaultIfEmpty(0).Max();
            }
            progress?.Invoke(values[0].Count);
        }
        var order = Enumerable.Range(0, moves.Count).OrderByDescending(i => values[i].Count == 0 ? 0 : values[i].Average()).ThenByDescending(i => best[i]).ToArray();
        return [.. order.Select(i => Summarize(moves[i], values[i], values[order[0]], i == order[0], moves[i] == pick))];
    }

    static Ranked Summarize(Move move, List<int> values, List<int> top, bool leads, bool strategy)
    {
        if (values.Count == 0) return new Ranked(move, 0, 0, leads ? 0 : double.PositiveInfinity, strategy);
        var mean = values.Average();
        var variance = Math.Max(0, values.Average(value => (double)value * value) - mean * mean);
        return new Ranked(move, mean, Math.Sqrt(variance / values.Count), leads ? 0 : PairedSe(top, values), strategy);
    }

    static double PairedSe(List<int> top, List<int> values)
    {
        if (values.Count < 2) return double.PositiveInfinity;
        double sum = 0, squares = 0;
        for (var i = 0; i < values.Count; i++)
        {
            var gap = top[i] - values[i];
            sum += gap;
            squares += gap * gap;
        }
        var mean = sum / values.Count;
        return Math.Sqrt(Math.Max(0, squares / values.Count - mean * mean) / values.Count);
    }

    static Run Playout(Run start, Move first, ulong seed)
    {
        var run = start.Reroll(new Rng(seed));
        var play = new Rng(seed ^ 0x9E3779B97F4A7C15UL);
        run.Apply(first);
        for (var step = 0; step < StepLimit && !run.Over; step++)
        {
            if (run.Legal().Count == 0) break;
            run.Apply(Policy.Choose(run, play));
        }
        return run;
    }
}
