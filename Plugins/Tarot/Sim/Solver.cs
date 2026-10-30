using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace Tarot;

public readonly record struct ActionResult(Move Move, double Mean, double DeadRate, double AvgChests, int Samples);

public sealed record SolverOptions(Weights Weights, int Rollouts, ulong Seed, bool DeathLosesLoot, int PlayCap)
{
    public static readonly SolverOptions Default = new(Weights.Rarity, 150, 1, true, 200);
}

public static class Solver
{
    const ulong SeedStep = 0x9E3779B97F4A7C15;

    static double Value(SimState s, SolverOptions o)
    {
        if (s.Dead && o.DeathLosesLoot) return 0;
        var sum = 0.0;
        foreach (var rarity in s.Taken) sum += o.Weights.Values[rarity];
        return sum;
    }

    public static double Rollout(SimState start, Move first, Rng rng, SolverOptions o, out bool dead, out int chests)
    {
        var s = start.Clone();
        Sim.Play(s, first, rng);
        for (var plays = 1; !s.Ended && plays < o.PlayCap; plays++)
            Sim.Play(s, o.DeathLosesLoot && s.Hp <= 2 && EveryMoveKills(s) ? Move.EndRun : Policy.Choose(s, o.Weights, rng), rng);
        dead = s.Dead;
        chests = s.Taken.Count;
        return Value(s, o);
    }

    static bool EveryMoveKills(SimState s)
    {
        var any = false;
        foreach (var move in Sim.Legal(s))
        {
            if (move.End) continue;
            any = true;
            var trial = s.Clone();
            Sim.Play(trial, move, new Rng(1));
            if (!trial.Dead) return false;
        }
        return any;
    }

    static bool Terminal(SimState s) => s.Ended || s.Hp <= 0 || s.TileCfg.Length == 0 || s.Position >= s.TileCfg.Length - 1;

    public static ActionResult[] Evaluate(SimState s, SolverOptions o, CancellationToken token) =>
        Run(s, Sim.Legal(s).ToList(), o, token);

    public static ActionResult[] Evaluate(SimState s, IReadOnlyList<Move> moves, SolverOptions o, CancellationToken token) =>
        Run(s, moves, o, token);

    static ActionResult[] Run(SimState s, IReadOnlyList<Move> moves, SolverOptions o, CancellationToken token)
    {
        if (Terminal(s) && moves.Count > 0) moves = [Move.EndRun];
        var results = new List<ActionResult>();
        var seen = new HashSet<(int, int, bool)>();
        for (var index = 0; index < moves.Count; index++)
        {
            var move = moves[index];
            var card = move.HandIndex >= 0 && move.HandIndex < s.Hand.Count ? s.Hand[move.HandIndex] : -1;
            if (!seen.Add((card, move.Steps, move.End))) continue;
            results.Add(Sample(s, move, index, o, token));
        }
        return [.. results.OrderByDescending(r => r.Samples > 0).ThenByDescending(r => r.Mean).ThenBy(r => r.DeadRate)];
    }

    static ActionResult Sample(SimState s, Move move, int index, SolverOptions o, CancellationToken token)
    {
        var rng = new Rng(o.Seed ^ ((ulong)(index + 1) * SeedStep));
        var rollouts = move.End ? 1 : o.Rollouts;
        double sum = 0, dead = 0, chests = 0;
        var n = 0;
        while (n < rollouts && (n == 0 && move.End || !token.IsCancellationRequested))
        {
            sum += Rollout(s, move, rng, o, out var died, out var count);
            if (died) dead++;
            chests += count;
            n++;
        }
        return n == 0 ? new ActionResult(move, 0, 0, 0, 0) : new ActionResult(move, sum / n, dead / n, chests / n, n);
    }
}
