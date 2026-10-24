using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace Vorax.Sim;

public enum Stage { None, Instrument, Potion, Target }

public sealed record SolveSettings(int Rollouts = 200, int BudgetMilliseconds = 1500, double RerollMargin = 0.05)
{
    public IReadOnlyDictionary<int, int>? RealWeights { get; init; }
}

public sealed record Ranking(double[] Values, int Best, double RerollValue, bool RerollBest);

public static class Solver
{
    const int PotionRounds = 10;
    const int HandSize = 3;
    const int HandSamples = 5;
    const int FreshSamples = 16;
    const int MinRerollHands = 8;

    public static Ranking RankPotions(SimState state, IReadOnlyList<int> potionSimIds, SolveSettings settings, Func<SimState, double>? score, bool allowReroll, Rng rng, IReadOnlyList<KitDef?>? kits = null)
    {
        var run = new Run(settings, score, rng);
        var values = new double[potionSimIds.Count];
        bool IsKit(int i) => kits is not null && kits[i] is not null;
        var ordinary = Enumerable.Range(0, values.Length).Where(i => !IsKit(i)).ToList();

        var targets = ordinary.ToDictionary(i => i, i => run.ChooseTargets(state, potionSimIds[i]));
        var means = run.Mean(ordinary.Count, 0.45, k => run.PlayThenFinish(state, potionSimIds[ordinary[k]], targets[ordinary[k]]));
        for (var k = 0; k < ordinary.Count; k++) values[ordinary[k]] = means[k];

        for (var i = 0; i < values.Length; i++)
            if (IsKit(i)) values[i] = run.KitValue(state, kits![i]!.Value);

        var best = BestIndex(values);
        var reroll = 0.0;
        var canReroll = allowReroll && state.Rerolls > 0;
        var enoughHands = false;
        if (canReroll) (reroll, enoughHands) = run.RerollValue(state, Math.Max(HandSize, potionSimIds.Count));
        return new(values, best, reroll, canReroll && enoughHands && best >= 0 && reroll > values[best] + Math.Abs(values[best]) * settings.RerollMargin);
    }

    public static Ranking RankInstruments(SimState state, IReadOnlyList<int> instrumentSimIds, SolveSettings settings, Func<SimState, double>? score, Rng rng)
    {
        var run = new Run(settings, score, rng);
        var values = run.Mean(instrumentSimIds.Count, 1.0, k =>
        {
            var next = state.Clone();
            if (!next.Instruments.Contains(instrumentSimIds[k]))
            {
                next.Instruments.Add(instrumentSimIds[k]);
                next.AcquiredRound[instrumentSimIds[k]] = next.Round;
            }
            if (next.PendingInstrumentPicks > 0) next.PendingInstrumentPicks--;
            return run.Finish(next, false);
        });
        return new(values, BestIndex(values), 0, false);
    }

    public static IReadOnlyList<int[]> TargetOptions(SimState state, int potionSimId)
    {
        var occupied = Enumerable.Range(0, SimState.DishCount).Where(i => state.Dishes[i] is not null).ToList();
        var targeting = Potions.Get(potionSimId).Targeting;
        if (targeting == Targeting.None) return [[]];
        var singles = occupied.Select(i => new[] { i });
        if (targeting != Targeting.OneOrTwo) return [.. singles];
        return [.. singles, .. occupied.SelectMany((a, ai) => occupied.Skip(ai + 1).Select(b => new[] { a, b }))];
    }

    public static Ranking RankTargets(SimState state, int potionSimId, SolveSettings settings, Func<SimState, double>? score, Rng rng)
    {
        var options = TargetOptions(state, potionSimId);
        var run = new Run(settings, score, rng);
        var values = run.Mean(options.Count, 1.0, k => run.PlayThenFinish(state, potionSimId, options[k]));
        return new(values, BestIndex(values), 0, false);
    }

    public static double ValueOfTargets(SimState state, int potionSimId, IReadOnlyList<int> targets, SolveSettings settings, Func<SimState, double>? score, Rng rng)
    {
        var run = new Run(settings, score, rng);
        return run.Mean(1, 1.0, _ => run.PlayThenFinish(state, potionSimId, targets))[0];
    }

    public static IReadOnlyList<int> BestTargets(SimState state, int potionSimId, SolveSettings settings, Func<SimState, double>? score, Rng rng)
    {
        var options = TargetOptions(state, potionSimId);
        if (options.Count <= 1) return options.Count == 0 ? [] : options[0];
        return options[RankTargets(state, potionSimId, settings, score, rng).Best];
    }

    static int BestIndex(double[] values)
    {
        var best = -1;
        for (var i = 0; i < values.Length; i++)
            if (best < 0 || values[i] > values[best]) best = i;
        return best;
    }

    sealed class Run(SolveSettings settings, Func<SimState, double>? score, Rng rng)
    {
        readonly Stopwatch _clock = Stopwatch.StartNew();
        readonly IReadOnlyList<(int SimId, double Weight)> _pool = DrawModel.PotionPool(settings.RealWeights);

        double Score(SimState s) => score is null ? s.TotalActivity : score(s);

        bool Past(double fraction) => _clock.ElapsedMilliseconds >= settings.BudgetMilliseconds * fraction;

        // every option gets one sample per pass so a budget stop leaves them evenly sampled
        public double[] Mean(int options, double budgetFraction, Func<int, double> sample)
        {
            var sums = new double[options];
            var passes = 0;
            while (options > 0 && passes < Math.Max(1, settings.Rollouts))
            {
                for (var k = 0; k < options; k++) sums[k] += sample(k);
                passes++;
                if (Past(budgetFraction)) break;
            }
            return [.. sums.Select(sum => passes == 0 ? 0 : sum / passes)];
        }

        public double PlayThenFinish(SimState state, int potionSimId, IReadOnlyList<int> targets) =>
            Finish(Round.Play(state, potionSimId, targets, rng), state.Round >= Round.Rounds);

        public double Finish(SimState s, bool done)
        {
            while (!done)
            {
                while (s.PendingInstrumentPicks > 0) TakeInstrument(s);
                done = s.Round >= Round.Rounds;
                s = s.Round > PotionRounds ? Round.Play(s, -1, [], rng) : PlayHand(s);
            }
            return Score(s);
        }

        void TakeInstrument(SimState s)
        {
            s.PendingInstrumentPicks--;
            if (s.Instruments.Count >= Round.MaxInstruments) return;
            var offers = DrawModel.DrawInstruments(rng, s.Instruments);
            if (offers.Length == 0) return;
            var id = offers.MaxBy(DrawModel.InstrumentStrength);
            s.Instruments.Add(id);
            s.AcquiredRound[id] = s.Round;
        }

        SimState PlayHand(SimState s)
        {
            var bestPotion = -1;
            IReadOnlyList<int> bestTargets = [];
            var bestValue = double.NegativeInfinity;
            foreach (var potion in DrawModel.Draw(_pool, HandSize, rng))
            {
                var (targets, value) = ChooseTargetsWithValue(s, potion);
                if (value <= bestValue) continue;
                (bestPotion, bestTargets, bestValue) = (potion, targets, value);
            }
            return Round.Play(s, bestPotion, bestTargets, rng);
        }

        public int[] ChooseTargets(SimState s, int potion) => ChooseTargetsWithValue(s, potion).Targets;

        (int[] Targets, double Value) ChooseTargetsWithValue(SimState s, int potion)
        {
            var targeting = Potions.Get(potion).Targeting;
            var singles = Enumerable.Range(0, SimState.DishCount).Where(i => s.Dishes[i] is not null)
                .Select(i => (Targets: new[] { i }, Value: Immediate(s, potion, [i]))).OrderByDescending(x => x.Value).ToList();
            if (targeting == Targeting.None || singles.Count == 0) return ([], Immediate(s, potion, []));
            var best = singles[0];
            if (targeting == Targeting.OneOrTwo && singles.Count >= 2)
            {
                var pair = new[] { singles[0].Targets[0], singles[1].Targets[0] };
                var pairValue = Immediate(s, potion, pair);
                if (pairValue > best.Value) best = (pair, pairValue);
            }
            return best;
        }

        double Immediate(SimState s, int potion, int[] targets)
        {
            var copy = s.Clone();
            Potions.Apply(copy, potion, targets, rng);
            return Score(copy);
        }

        // pick the winner with a cheap pass, then value it with fresh samples so the max over noisy means does not bias the result up
        double BestOfHand(SimState s, IReadOnlyList<int> hand)
        {
            if (hand.Count == 0) return 0;
            var winner = hand[0];
            var winnerTargets = ChooseTargets(s, winner);
            var bestCheap = double.NegativeInfinity;
            foreach (var potion in hand)
            {
                var targets = ChooseTargets(s, potion);
                var sum = 0.0;
                for (var k = 0; k < HandSamples; k++) sum += PlayThenFinish(s, potion, targets);
                if (sum <= bestCheap) continue;
                (bestCheap, winner, winnerTargets) = (sum, potion, targets);
            }
            var fresh = 0.0;
            for (var k = 0; k < FreshSamples; k++) fresh += PlayThenFinish(s, winner, winnerTargets);
            return fresh / FreshSamples;
        }

        public double KitValue(SimState s, KitDef kit) => MeanOverHands(s, DrawModel.KitPool(kit, _pool), kit.Amount, Math.Max(4, settings.Rollouts / 10), 0.65).Mean;

        public (double Value, bool EnoughHands) RerollValue(SimState s, int handSize)
        {
            var after = s.Clone();
            after.Rerolls--;
            var planned = Math.Max(4, settings.Rollouts / 5);
            var (mean, hands) = MeanOverHands(after, _pool, handSize, planned, 1.0);
            return (mean, hands >= Math.Min(MinRerollHands, planned));
        }

        (double Mean, int Hands) MeanOverHands(SimState s, IReadOnlyList<(int SimId, double Weight)> pool, int handSize, int hands, double budgetFraction)
        {
            var sum = 0.0;
            var done = 0;
            while (done < hands)
            {
                sum += BestOfHand(s, DrawModel.Draw(pool, handSize, rng));
                done++;
                if (Past(budgetFraction)) break;
            }
            return (sum / done, done);
        }
    }
}
