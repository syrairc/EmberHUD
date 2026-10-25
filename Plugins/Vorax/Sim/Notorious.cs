using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Vorax.Sim;

public readonly record struct NotoriousOption(int Card, int[] Targets);

public readonly record struct NotoriousEntry(NotoriousOption? Option, double Goal, double Reached, double Quick)
{
    public bool IsReroll => Option is null;
}

public sealed record NotoriousResult(IReadOnlyList<NotoriousEntry> Ranking, NotoriousEntry Best);

// The Notorious fork method (MIT, see LICENSE-voraxsim.txt): shortlist the options by the expected board after this round, then play each shortlisted option out
// to round 13 on the same seeded futures with a greedy value-model policy, and rank them by mean Activity capped at 950,000
public sealed class Notorious
{
    const ulong RootSeed = 24301;
    const int Top = 4;
    const int QuickSamples = 8;
    const int PotionPolicySamples = 2;
    const int InstrumentPolicySamples = 4;
    const int BoxDepth = 2;
    const int MaxSteps = 300;

    readonly int _futures;
    readonly DrawProfile _draw;

    public Notorious(int futures = 0, DrawProfile? draw = null)
    {
        _futures = futures > 0 ? futures : Environment.ProcessorCount >= 8 ? 64 : 32;
        _draw = draw ?? DrawModel.Standard;
    }

    sealed class Node(SimState sim, int[] hand, KitDef? box, Rng rng)
    {
        public SimState Sim { get; } = sim;
        public int[] Hand { get; } = hand;
        public KitDef? Box { get; } = box;
        public Rng Rng { get; } = rng;

        public bool InstrumentPhase => !Sim.Over && Sim.PendingInstrumentPicks > 0;
        public bool RoundEndPhase => !Sim.Over && Sim.PendingInstrumentPicks == 0 && Sim.Round > Round.PotionRounds;
    }

    public static ulong Mix(ulong a, ulong b)
    {
        var z = a ^ (b + 0x9E3779B97F4A7C15UL);
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }

    public static IReadOnlyList<NotoriousOption> Options(SimState s, IReadOnlyList<int> hand, int onlyCard = -1)
    {
        var occupied = Enumerable.Range(0, SimState.DishCount).Where(i => s.Dishes[i] is not null).ToList();
        var options = new List<NotoriousOption>();
        for (var card = 0; card < hand.Count; card++)
        {
            if (onlyCard >= 0 && card != onlyCard) continue;
            var targeting = DrawModel.IsKit(hand[card]) ? Targeting.None : Potions.Get(hand[card]).Targeting;
            if (targeting == Targeting.None)
            {
                options.Add(new(card, []));
                continue;
            }
            foreach (var a in occupied) options.Add(new(card, [a]));
            if (targeting != Targeting.OneOrTwo) continue;
            foreach (var a in occupied)
                foreach (var b in occupied)
                    if (a < b) options.Add(new(card, [a, b]));
        }
        return options;
    }

    public NotoriousResult? RankPotions(SimState state, IReadOnlyList<int> hand, bool allowReroll = true, int onlyCard = -1)
    {
        var root = new Node(state, [.. hand], null, new Rng(RootSeed));
        var quick = Options(state, hand, onlyCard)
            .Select(o => (Option: o, Value: QuickValue(root, o, QuickSamples, RootSeed, false, 0)))
            .OrderByDescending(x => x.Value).ToList();

        var shortlist = new List<(NotoriousOption Option, double Value)>();
        foreach (var x in quick)
            if (!shortlist.Any(y => y.Option.Card == x.Option.Card)) shortlist.Add(x);
        foreach (var x in quick)
            if (shortlist.Count < Top && !shortlist.Contains(x)) shortlist.Add(x);

        var candidates = shortlist.Select(x => (Option: (NotoriousOption?)x.Option, x.Value)).ToList();
        if (allowReroll && onlyCard < 0 && state.Rerolls > 0) candidates.Add((null, state.TotalActivity));

        if (candidates.Count == 0) return null;
        var tallies = Futures(candidates.Count, 9000, (c, seed) => PlayPotionOption(root, candidates[c].Option, seed));
        var entries = candidates.Select((c, i) => new NotoriousEntry(c.Option, tallies[i].Goal, tallies[i].Reached, c.Value)).OrderByDescending(e => e.Goal).ToList();
        entries = SkipNoEffect(state, hand, entries);

        var best = entries[0];
        if (best.IsReroll)
        {
            var kit = entries.FirstOrDefault(e => e.Option is { } o && DrawModel.IsKit(hand[o.Card]));
            if (kit.Option is not null) best = kit;
        }
        return new(entries, best);
    }

    public NotoriousResult RankInstruments(SimState state, IReadOnlyList<int> offers)
    {
        var root = new Node(state, [.. offers], null, new Rng(RootSeed));
        var tallies = Futures(offers.Count, 7000, (c, seed) =>
        {
            var rng = new Rng(seed);
            return Rollout(PickInstrument(new Node(state, root.Hand, null, rng), c, rng), seed, InstrumentPolicySamples);
        });
        var entries = offers.Select((_, i) => new NotoriousEntry(new(i, []), tallies[i].Goal, tallies[i].Reached, 0)).OrderByDescending(e => e.Goal).ToList();
        return new(entries, entries[0]);
    }

    // the fork keeps a card out of first place when it changes nothing and another card is within 1%
    List<NotoriousEntry> SkipNoEffect(SimState state, IReadOnlyList<int> hand, List<NotoriousEntry> entries)
    {
        bool NoEffect(NotoriousEntry e)
        {
            if (e.Option is not { } o || DrawModel.IsKit(hand[o.Card])) return false;
            var copy = state.Clone();
            Potions.Apply(copy, hand[o.Card], o.Targets, new Rng(RootSeed));
            return copy.Dishes.SequenceEqual(state.Dishes);
        }

        if (!NoEffect(entries[0])) return entries;
        var better = entries.FirstOrDefault(e => !e.IsReroll && !NoEffect(e) && e.Goal >= entries[0].Goal * 0.99);
        return better.Option is null ? entries : [better, .. entries.Where(e => !e.Equals(better))];
    }

    (double Goal, double Reached)[] Futures(int candidates, ulong salt, Func<int, ulong, (double Goal, bool Reached)> play)
    {
        var goal = new double[candidates];
        var reached = new double[candidates];
        var gate = new object();
        Parallel.For(0, _futures, h =>
        {
            var seed = Mix(RootSeed, salt + (ulong)h);
            var g = new double[candidates];
            var r = new double[candidates];
            for (var c = 0; c < candidates; c++)
            {
                var result = play(c, seed);
                g[c] = result.Goal;
                r[c] = result.Reached ? 1 : 0;
            }
            lock (gate)
                for (var c = 0; c < candidates; c++)
                {
                    goal[c] += g[c];
                    reached[c] += r[c];
                }
        });
        return [.. Enumerable.Range(0, candidates).Select(c => (goal[c] / _futures, reached[c] / _futures))];
    }

    (double Goal, bool Reached) PlayPotionOption(Node root, NotoriousOption? option, ulong seed)
    {
        var rng = new Rng(seed);
        var node = new Node(root.Sim, root.Hand, null, rng);
        node = option is { } o ? Apply(node, o) : Refresh(node);
        return Rollout(node, seed, PotionPolicySamples);
    }

    (double Goal, bool Reached) Rollout(Node node, ulong seed, int policySamples)
    {
        for (var step = 0; step < MaxSteps && !node.Sim.Over; step++)
        {
            if (node.RoundEndPhase)
            {
                node = Advance(node);
                continue;
            }
            if (node.InstrumentPhase)
            {
                node = PickInstrument(node, BestInstrument(node), node.Rng);
                continue;
            }

            var stepSeed = Mix(seed, (ulong)step);
            var stay = NotoriousModel.Value(node.Sim);
            var (best, value) = Choose(node, stepSeed, policySamples);
            if (node.Box is null && node.Sim.Rerolls > 0 && (best is null || value <= stay))
            {
                node = Refresh(node);
                continue;
            }
            node = best is null ? Advance(node) : Apply(node, best.Value);
        }
        var total = node.Sim.TotalActivity;
        return (Math.Min(total, NotoriousModel.Cap), total >= NotoriousModel.Cap);
    }

    (NotoriousOption? Best, double Value) Choose(Node node, ulong seed, int samples)
    {
        NotoriousOption? best = null;
        var bestValue = double.NegativeInfinity;
        foreach (var o in Options(node.Sim, node.Hand))
        {
            var v = QuickValue(node, o, samples, seed, true, 0);
            if (v <= bestValue) continue;
            (best, bestValue) = (o, v);
        }
        return (best, bestValue);
    }

    int BestInstrument(Node node)
    {
        var best = 0;
        var bestValue = double.NegativeInfinity;
        for (var i = 0; i < node.Hand.Length; i++)
        {
            var v = NotoriousModel.Value(PickInstrument(node, i, new Rng(RootSeed)).Sim);
            if (v <= bestValue) continue;
            (best, bestValue) = (i, v);
        }
        return best;
    }

    // mean over a few seeded samples of the board after the option and the rest of its round; an opened kit takes the best of its fresh hand
    double QuickValue(Node node, NotoriousOption option, int samples, ulong seed, bool useValue, int depth)
    {
        var count = depth == 0 ? samples : Math.Max(2, samples >> 2);
        double sum = 0;
        for (var i = 0; i < count; i++)
        {
            var sampleSeed = Mix(seed, (ulong)(i + 1 + depth * 101));
            var applied = Apply(new Node(node.Sim, node.Hand, node.Box, new Rng(sampleSeed)), option);
            if (applied.Box is not null && depth < BoxDepth)
            {
                var best = double.NegativeInfinity;
                foreach (var inner in Options(applied.Sim, applied.Hand)) best = Math.Max(best, QuickValue(applied, inner, samples, sampleSeed, useValue, depth + 1));
                sum += double.IsNegativeInfinity(best) ? Score(applied.Sim, useValue) : best;
            }
            else sum += Score(applied.Sim, useValue);
        }
        return sum / count;
    }

    static double Score(SimState s, bool useValue) => useValue ? NotoriousModel.Value(s) : s.TotalActivity;

    Node Apply(Node node, NotoriousOption option)
    {
        var rng = node.Rng;
        var id = node.Hand[option.Card];
        if (DrawModel.KitOf(id) is { } kit && node.Box is null) return new(node.Sim, DrawModel.DrawHand(rng, kit.Amount, kit, _draw), kit, rng);
        return Deal(Round.Play(node.Sim, id, option.Targets, rng), rng);
    }

    Node Advance(Node node) => Deal(Round.Play(node.Sim, -1, [], node.Rng), node.Rng);

    Node Refresh(Node node)
    {
        var sim = node.Sim.Clone();
        sim.Rerolls--;
        return new(sim, DrawModel.DrawHand(node.Rng, 3, null, _draw), null, node.Rng);
    }

    Node Deal(SimState sim, Rng rng)
    {
        if (sim.Over || sim.Round > Round.PotionRounds && sim.PendingInstrumentPicks == 0) return new(sim, [], null, rng);
        return new(sim, sim.PendingInstrumentPicks > 0 ? DrawModel.DrawInstruments(rng, sim.Instruments) : DrawModel.DrawHand(rng, 3, null, _draw), null, rng);
    }

    Node PickInstrument(Node node, int card, Rng rng)
    {
        var sim = node.Sim.Clone();
        var id = node.Hand[card];
        if (!sim.Instruments.Contains(id))
        {
            sim.Instruments.Add(id);
            sim.AcquiredRound[id] = sim.Round;
        }
        if (sim.PendingInstrumentPicks > 0) sim.PendingInstrumentPicks--;
        if (sim.PendingInstrumentPicks > 0 && sim.Instruments.Count < Round.MaxInstruments) return new(sim, DrawModel.DrawInstruments(rng, sim.Instruments), null, rng);
        sim.PendingInstrumentPicks = 0;
        return new(sim, sim.Round > Round.PotionRounds ? [] : DrawModel.DrawHand(rng, 3, null, _draw), null, rng);
    }
}
