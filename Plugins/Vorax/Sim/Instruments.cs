using System;
using System.Collections.Generic;
using System.Linq;

namespace Vorax.Sim;

// Port of the community Vorax simulator instrument handlers (MIT, see LICENSE-voraxsim.txt), aligned with the Notorious fork: reactive ones answer each event inline, the rest run once at round end in id order
public static class Instruments
{
    delegate void Handler(SimState s, Rng rng);

    public const int Butterfly = 25;

    static readonly HashSet<int> EventDrivenIds = [2, 5, 6, 7, 8, 12, 16, 22];

    static readonly Dictionary<int, Handler> EndHandlers = BuildEnd();

    public static bool Known(int simId) => EndHandlers.ContainsKey(simId) || EventDrivenIds.Contains(simId);

    public static bool EventDriven(int simId) => EventDrivenIds.Contains(simId);

    internal static void EndOfRound(SimState state, int simId, Rng rng)
    {
        if (EndHandlers.TryGetValue(simId, out var handler)) handler(state, rng);
    }

    internal static void React(SimState s, int simId, SimEvent e, Rng rng)
    {
        switch (simId)
        {
            case 2 when e.Kind == EventKind.Add && BySpecies(s, Species.Pests).Count >= 2:
                foreach (var i in NonEmpty(s)) Boost(s, i, 0, 45);
                break;
            case 5 when e.Kind == EventKind.Remove && BySpecies(s, Species.BoneSentry).Count >= 2:
                BoostRandom(s, rng, 0, 180);
                break;
            case 6 when e.Kind == EventKind.Remove && e.Species != Species.BoneSentry:
                BoostRandom(s, rng, 0, 150);
                break;
            case 7 when e.Kind == EventKind.Mutate && e.ToSpecies == Species.Aemberon:
                foreach (var i in NonEmpty(s)) Boost(s, i, 35);
                break;
            case 8 when e.Kind == EventKind.Mutate && BySpecies(s, Species.Aemberon).Count >= 2:
                foreach (var i in NonEmpty(s)) Boost(s, i, 20);
                break;
            case 12 when e.Kind == EventKind.Add && s.Dishes[e.Dish] is not null && rng.NextDouble() < 0.75:
                s.Mutate(e.Dish, Species.Aemberon, null, rng);
                if (s.Dishes[e.Dish] is not null) Boost(s, e.Dish, 80);
                break;
            case 16 when e.Kind == EventKind.AddOverflow:
                var lowest = Lowest(s);
                if (lowest >= 0) Boost(s, lowest, 25, 25);
                break;
            case 22 when e.Kind == EventKind.Remove:
                var highest = Highest(s);
                if (highest >= 0) Boost(s, highest, 20, 20);
                break;
        }
    }

    static Dictionary<int, Handler> BuildEnd() => new()
    {
        [1] = (s, rng) =>
        {
            var pests = BySpecies(s, Species.Pests).Count;
            for (var rep = 0; rep < pests; rep++)
            {
                var all = NonEmpty(s);
                foreach (var i in rng.Sample(all, Math.Min(pests, all.Count))) Boost(s, i, 0, 8);
            }
        },
        [3] = (s, rng) =>
        {
            if (BySpecies(s, Species.Agito).Count < 2) return;
            var strong = NonEmpty(s).Where(i => s.Dishes[i]!.Value.Rarity >= Rarity.Rare).ToList();
            if (strong.Count > 0) Boost(s, rng.Pick(strong), 80);
        },
        [4] = (s, rng) =>
        {
            if (!BySpecies(s, Species.Agito).Any(i => s.Dishes[i]!.Value.Rarity >= Rarity.Rare)) return;
            Boost(s, rng.Pick(NonEmpty(s)), 80);
        },
        [9] = (s, rng) =>
        {
            if (BySpecies(s, Species.Pests).Count < 3) return;
            var added = s.Add(Monster.Fresh(Species.Pests, SimState.RandomRarity(rng)));
            if (added >= 0) Boost(s, added, 0, 100);
        },
        [10] = (s, rng) =>
        {
            var magic = NonEmpty(s).Where(i => s.Dishes[i]!.Value.Rarity == Rarity.Magic).ToList();
            if (magic.Count >= 2) s.Fuse(rng.Sample(magic, 2), Species.Agito, Rarity.Rare);
        },
        [11] = (s, rng) =>
        {
            var sentries = BySpecies(s, Species.BoneSentry);
            if (sentries.Count < 2) return;
            var others = NonEmpty(s).Where(i => s.Dishes[i]!.Value.Species != Species.BoneSentry).ToList();
            if (others.Count == 0) return;
            var removed = s.Remove(others.MinBy(i => s.Dishes[i]!.Value.Total));
            var target = rng.Pick(sentries);
            if (removed is { } m && s.Dishes[target] is not null) Boost(s, target, m.Activity, m.Quantity);
        },
        [13] = (s, _) =>
        {
            if (NonEmpty(s).Count(i => s.Dishes[i]!.Value.Rarity == Rarity.Magic) < 3) return;
            Boost(s, Highest(s), 0, 100);
        },
        [14] = (s, _) =>
        {
            foreach (var rarity in new[] { Rarity.Normal, Rarity.Magic, Rarity.Rare })
            {
                var same = NonEmpty(s).Where(i => s.Dishes[i]!.Value.Rarity == rarity).ToList();
                while (same.Count >= 3)
                {
                    var three = same.Take(3).ToList();
                    s.Fuse(three, s.Dishes[three[0]]!.Value.Species, rarity + 1);
                    same = NonEmpty(s).Where(i => s.Dishes[i]!.Value.Rarity == rarity).ToList();
                }
            }
        },
        [15] = (s, _) =>
        {
            foreach (var i in NonEmpty(s).Where(i => s.Dishes[i]!.Value is { Quantity: > 150, Activity: > 150 })) Boost(s, i, 15, 15);
        },
        [17] = (s, _) =>
        {
            foreach (var i in NonEmpty(s).Where(i => s.Dishes[i]!.Value.Quantity > 275)) Boost(s, i, 20);
        },
        [18] = (s, _) =>
        {
            foreach (var i in NonEmpty(s).Where(i => s.Dishes[i]!.Value.Activity > 255)) Boost(s, i, 0, 30);
        },
        [19] = (s, rng) =>
        {
            if (!EveryNthRound(s, 19, 2)) return;
            var byRarity = Enum.GetValues<Rarity>().Select(rarity => NonEmpty(s).Where(i => s.Dishes[i]!.Value.Rarity == rarity).ToList()).ToList();
            foreach (var candidates in byRarity)
                if (candidates.Count > 0) s.Awaken(rng.Pick(candidates));
        },
        [20] = (s, rng) =>
        {
            if (!EveryNthRound(s, 20, 3)) return;
            foreach (var i in NonEmpty(s)) s.Mutate(i, null, null, rng);
        },
        [21] = (s, _) =>
        {
            var rarities = NonEmpty(s).Select(i => s.Dishes[i]!.Value.Rarity).ToHashSet();
            if (!rarities.Contains(Rarity.Normal) || !rarities.Contains(Rarity.Magic) || !(rarities.Contains(Rarity.Rare) || rarities.Contains(Rarity.Boss))) return;
            foreach (var i in NonEmpty(s)) Boost(s, i, 20);
        },
        [23] = (s, rng) =>
        {
            var all = NonEmpty(s);
            if (all.Count is >= 2 and <= 5) s.Add(Monster.Fresh(rng.Pick(SimState.AllSpecies), SimState.RandomRarity(rng)));
            else if (all.Count == SimState.DishCount)
            {
                var highest = Highest(s);
                var lowest = Lowest(s);
                if (highest != lowest) s.Devour(highest, lowest);
            }
        },
        [24] = (s, _) =>
        {
            if (NonEmpty(s).Count(i => s.Dishes[i]!.Value.Rarity == Rarity.Normal) < 3) return;
            foreach (var i in NonEmpty(s)) Boost(s, i, 0, 30);
        },
        [Butterfly] = (s, rng) =>
        {
            if (!EveryNthRound(s, Butterfly, 3)) return;
            var pests = BySpecies(s, Species.Pests);
            if (pests.Count >= 2) s.Fuse(rng.Sample(pests, 2), Species.Pests, SimState.RandomRarity(rng));
        },
    };

    static bool EveryNthRound(SimState s, int simId, int n)
    {
        var elapsed = s.Round - s.AcquiredRound.GetValueOrDefault(simId, 1);
        return elapsed > 0 && elapsed % n == 0;
    }

    static List<int> NonEmpty(SimState s) => Enumerable.Range(0, SimState.DishCount).Where(i => s.Dishes[i] is not null).ToList();

    static List<int> BySpecies(SimState s, Species species) => NonEmpty(s).Where(i => s.Dishes[i]!.Value.Species == species).ToList();

    static int Highest(SimState s)
    {
        var all = NonEmpty(s);
        return all.Count == 0 ? -1 : all.MaxBy(i => s.Dishes[i]!.Value.Total);
    }

    static int Lowest(SimState s)
    {
        var all = NonEmpty(s);
        return all.Count == 0 ? -1 : all.MinBy(i => s.Dishes[i]!.Value.Total);
    }

    static void Boost(SimState s, int dish, int activity, int quantity = 0)
    {
        if (dish < 0 || s.Dishes[dish] is not { } m) return;
        s.Dishes[dish] = m with { Activity = m.Activity + activity, Quantity = m.Quantity + quantity };
    }

    static void BoostRandom(SimState s, Rng rng, int activity, int quantity)
    {
        var all = NonEmpty(s);
        if (all.Count > 0) Boost(s, rng.Pick(all), activity, quantity);
    }
}
