using System;
using System.Collections.Generic;
using System.Linq;

namespace Vorax.Sim;

public enum Targeting { None, One, OneOrTwo }

public sealed record PotionDef(int SimId, string Name, Targeting Targeting);

// Port of the community Vorax simulator potion handlers (MIT, see LICENSE-voraxsim.txt). Sim ids 33, 34, 40 are boxes with no effect, 41 is game-only
public static class Potions
{
    delegate void Handler(SimState s, IReadOnlyList<int> targets, Rng rng);

    public static IReadOnlyList<PotionDef> All { get; } =
    [
        new(0, "Probiotic Enzyme Solution", Targeting.None),
        new(1, "Molting Skin Solution", Targeting.OneOrTwo),
        new(2, "Pia Mater Solution", Targeting.One),
        new(3, "Bone Cleansing Ointment", Targeting.None),
        new(4, "Cleansing Ointment", Targeting.One),
        new(5, "Fresh Marrow Powder", Targeting.None),
        new(6, "Petrified Marrow Solution", Targeting.One),
        new(7, "Active Spawning Hormone", Targeting.None),
        new(8, "Eggshell Powder", Targeting.None),
        new(9, "Brain Fog Tincture", Targeting.None),
        new(10, "Bone Growth Powder", Targeting.None),
        new(11, "Gray Matter Spinal Solution", Targeting.None),
        new(12, "Hollow Spinal Solution", Targeting.One),
        new(13, "Green Bile Solution", Targeting.OneOrTwo),
        new(14, "Mixed Leech Solution", Targeting.One),
        new(15, "Digestive Enzyme Solution", Targeting.One),
        new(16, "Breeding Powder", Targeting.None),
        new(17, "Potent Xenopurge Powder", Targeting.One),
        new(18, "Xenopurge Powder", Targeting.One),
        new(19, "Pure Leech Solution", Targeting.One),
        new(20, "Entrancing Tincture", Targeting.One),
        new(21, "Mutagenic Powder", Targeting.OneOrTwo),
        new(22, "Viscous Bile Solution", Targeting.One),
        new(23, "Slender-Limb Powder - Pests", Targeting.None),
        new(24, "Twin Hormone - Pests", Targeting.One),
        new(25, "Spinal Solution - Agito", Targeting.None),
        new(26, "Anesthetic Tincture - Agito", Targeting.One),
        new(27, "Slender-Limb Powder - Bone Sentry", Targeting.None),
        new(28, "Twin Hormone - Bone Sentry", Targeting.One),
        new(29, "Anesthetic Tincture - Aemberon", Targeting.One),
        new(30, "Spinal Solution - Aemberon", Targeting.None),
        new(31, "Xeno Hormone", Targeting.OneOrTwo),
        new(32, "Targeted Xeno Hormone", Targeting.One),
        new(33, "Large Potion Box", Targeting.None),
        new(34, "Rare Potion Box", Targeting.None),
        new(35, "Compound Rebirth Pill", Targeting.None),
        new(36, "Fast-Acting Cardiotonic", Targeting.None),
        new(37, "Plague Peat Dressing", Targeting.None),
        new(38, "Insect Lure", Targeting.None),
        new(39, "Pure Holy Water", Targeting.None),
        new(40, "Small Potion Box", Targeting.None),
        new(41, "Rousing Salts", Targeting.None),
    ];

    static readonly Handler[] Handlers = Build();

    public static PotionDef Get(int simId) => All[simId];

    public static void Apply(SimState state, int simId, IReadOnlyList<int> targets, Rng rng)
    {
        var def = Get(simId);
        state.Rng = rng;
        var picked = targets.Where(t => t is >= 0 and < SimState.DishCount && state.Dishes[t] is not null).Distinct().Take(def.Targeting == Targeting.OneOrTwo ? 2 : 1).ToList();
        if (def.Targeting != Targeting.None && picked.Count == 0) return;
        Handlers[simId](state, picked, rng);
    }

    static Handler[] Build()
    {
        var handlers = new Handler[All.Count];
        for (var i = 0; i < handlers.Length; i++) handlers[i] = (_, _, _) => { };

        handlers[0] = (s, _, _) =>
        {
            var pests = BySpecies(s, Species.Pests);
            foreach (var i in pests) Boost(s, i, 11 * pests.Count);
        };
        handlers[1] = (s, t, rng) => s.Fuse(t, Species.Pests, SimState.RandomRarity(rng));
        handlers[2] = (s, t, _) =>
        {
            var m = s.Dishes[t[0]]!.Value;
            var times = m.Species != Species.Agito ? 1 : m.Rarity switch { Rarity.Magic => 2, Rarity.Rare or Rarity.Boss => 3, _ => 1 };
            Boost(s, t[0], 30 * times);
        };
        handlers[3] = (s, _, rng) =>
        {
            if (BySpecies(s, Species.BoneSentry).Count == 0) return;
            var others = NonEmpty(s).Where(i => s.Dishes[i]!.Value.Species != Species.BoneSentry).ToList();
            foreach (var i in rng.Sample(others, Math.Min(3, others.Count)))
            {
                s.Remove(i);
                foreach (var d in NonEmpty(s)) Boost(s, d, 28, 28);
            }
        };
        handlers[4] = (s, t, _) =>
        {
            var i = t[0];
            Boost(s, i, 20);
            if (s.Dishes[i]!.Value.Species != Species.BoneSentry) return;
            Boost(s, i, 0, 41);
            var right = NonEmpty(s).Where(d => d > i).DefaultIfEmpty(-1).First();
            if (right >= 0) s.Remove(right);
        };
        handlers[5] = (s, _, rng) =>
        {
            var others = NonEmpty(s).Where(i => s.Dishes[i]!.Value.Species != Species.Aemberon).ToList();
            foreach (var i in rng.Sample(others, Math.Min(3, others.Count)))
            {
                s.Mutate(i, Species.Aemberon, null, rng);
                var aemberons = BySpecies(s, Species.Aemberon);
                foreach (var a in rng.Sample(aemberons, Math.Min(3, aemberons.Count))) Boost(s, a, 0, 42);
            }
        };
        handlers[6] = (s, t, rng) =>
        {
            var i = t[0];
            Boost(s, i, 20);
            if (s.Dishes[i]!.Value.Species == Species.Aemberon) return;
            s.Mutate(i, Species.Aemberon, null, rng);
            Boost(s, i, 30);
        };
        handlers[7] = (s, _, rng) =>
        {
            AddFresh(s, Species.Pests, rng);
            if (rng.NextDouble() >= 0.5) return;
            AddFresh(s, Species.Pests, rng);
            AddFresh(s, Species.Pests, rng);
        };
        handlers[8] = (s, _, rng) =>
        {
            if (!TryRandom(s, rng, out var i)) return;
            Boost(s, i, 0, 25);
            var m = s.Dishes[i]!.Value;
            if (m.Species != Species.Pests) return;
            var added = s.Add(Monster.Fresh(Species.Pests, m.Rarity));
            if (added >= 0) Boost(s, added, 0, 25);
        };
        handlers[9] = (s, _, rng) =>
        {
            if (!TryRandom(s, rng, out var i)) return;
            Boost(s, i, 41);
            if (s.Dishes[i]!.Value.Species == Species.Agito) s.Awaken(i, Rarity.Rare);
        };
        handlers[10] = (s, _, rng) =>
        {
            if (!TryRandom(s, rng, out var i)) return;
            Boost(s, i, 0, 40);
            if (s.Dishes[i]!.Value.Species != Species.BoneSentry) return;
            Boost(s, i, 0, 127);
            var others = NonEmpty(s).Where(d => s.Dishes[d]!.Value.Species != Species.BoneSentry).ToList();
            if (others.Count > 0) s.Remove(rng.Pick(others));
        };
        handlers[11] = (s, _, rng) =>
        {
            if (!TryRandom(s, rng, out var i)) return;
            Boost(s, i, 41);
            s.Mutate(i, null, null, rng);
            if (rng.NextDouble() >= 0.5) return;
            s.Mutate(i, Species.Aemberon, null, rng);
            Boost(s, i, 30);
        };
        handlers[12] = (s, t, rng) =>
        {
            Boost(s, t[0], 41);
            if (rng.NextDouble() < 0.5) s.Mutate(t[0], Species.Aemberon, null, rng);
        };
        handlers[13] = (s, t, rng) =>
        {
            foreach (var i in t)
            {
                s.Mutate(i, null, Rarity.Magic, rng);
                Boost(s, i, 31);
            }
        };
        handlers[14] = (s, t, rng) =>
        {
            var rarity = s.Dishes[t[0]]!.Value.Rarity;
            var same = NonEmpty(s).Where(i => s.Dishes[i]!.Value.Rarity == rarity).ToList();
            foreach (var i in rng.Sample(same, Math.Min(2, same.Count))) Boost(s, i, 31);
        };
        handlers[15] = (s, t, _) =>
        {
            Boost(s, t[0], 61);
            var left = NonEmpty(s).Where(d => d < t[0]).DefaultIfEmpty(-1).Last();
            if (left >= 0) s.Remove(left);
        };
        handlers[16] = (s, _, rng) =>
        {
            var selected = new List<int>();
            foreach (var rarity in Enum.GetValues<Rarity>())
            {
                var group = NonEmpty(s).Where(i => s.Dishes[i]!.Value.Rarity == rarity).ToList();
                if (group.Count > 0) selected.Add(rng.Pick(group));
            }
            foreach (var i in selected.Take(4)) Boost(s, i, 0, 32);
        };
        handlers[17] = (s, t, rng) => Xenopurge(s, t[0], 154, 2, rng);
        handlers[18] = (s, t, rng) => Xenopurge(s, t[0], 127, 1, rng);
        handlers[19] = (s, t, rng) =>
        {
            var species = s.Dishes[t[0]]!.Value.Species;
            var same = BySpecies(s, species);
            foreach (var i in rng.Sample(same, Math.Min(2, same.Count))) Boost(s, i, 31);
        };
        handlers[20] = (s, t, _) => s.Awaken(t[0]);
        handlers[21] = (s, t, rng) =>
        {
            foreach (var i in t)
            {
                Boost(s, i, 0, 52);
                s.Mutate(i, null, Rarity.Rare, rng);
            }
        };
        handlers[22] = (s, t, rng) =>
        {
            var m = s.Dishes[t[0]]!.Value;
            Boost(s, t[0], 31);
            var right = NonEmpty(s).Where(d => d > t[0]).DefaultIfEmpty(-1).First();
            if (right < 0) return;
            s.Mutate(right, m.Species, m.Rarity, rng);
            Boost(s, right, 31);
        };
        handlers[23] = (s, _, rng) => AddFresh(s, Species.Pests, rng, 0, 73);
        handlers[24] = (s, t, rng) =>
        {
            s.Mutate(t[0], Species.Pests, null, rng);
            s.Add(Monster.Fresh(Species.Pests, s.Dishes[t[0]]!.Value.Rarity));
        };
        handlers[25] = (s, _, rng) => AddFresh(s, Species.Agito, rng, 31);
        handlers[26] = (s, t, rng) =>
        {
            s.Mutate(t[0], Species.Agito, null, rng);
            s.Awaken(t[0], Rarity.Magic);
        };
        handlers[27] = (s, _, rng) => AddFresh(s, Species.BoneSentry, rng, 0, 73);
        handlers[28] = (s, t, rng) =>
        {
            s.Mutate(t[0], Species.BoneSentry, null, rng);
            Boost(s, t[0], 0, 62);
        };
        handlers[29] = (s, t, rng) =>
        {
            s.Mutate(t[0], Species.Aemberon, null, rng);
            if (rng.NextDouble() >= 0.5) return;
            var others = NonEmpty(s).Where(i => i != t[0]).ToList();
            if (others.Count > 0) s.Mutate(rng.Pick(others), Species.Aemberon, null, rng);
        };
        handlers[30] = (s, _, rng) => AddFresh(s, Species.Aemberon, rng, 31);
        handlers[31] = (s, t, rng) =>
        {
            foreach (var i in t) s.Remove(i);
            for (var n = 0; n < 4; n++) AddFresh(s, rng.Pick(SimState.AllSpecies), rng, 5);
        };
        handlers[32] = (s, t, rng) =>
        {
            s.Remove(t[0]);
            for (var n = 0; n < 2; n++) s.Add(Boosted(Monster.Fresh(rng.Pick(SimState.AllSpecies), Rarity.Magic), 0, 25));
        };
        handlers[35] = (s, _, rng) =>
        {
            foreach (var i in NonEmpty(s))
            {
                s.Mutate(i, Species.Aemberon, null, rng);
                Boost(s, i, 11);
                var rarity = s.Dishes[i]!.Value.Rarity;
                if (rarity == Rarity.Magic) Boost(s, i, 11);
                else if (rarity is Rarity.Rare or Rarity.Boss) Boost(s, i, 0, 11);
            }
        };
        handlers[36] = (s, _, _) =>
        {
            var all = NonEmpty(s);
            if (all.Count == 0) return;
            Boost(s, all.MinBy(i => s.Dishes[i]!.Value.Total), 42, 42);
        };
        handlers[37] = (s, _, _) =>
        {
            if (BySpecies(s, Species.BoneSentry).Count == 0) return;
            foreach (var i in NonEmpty(s).Where(d => s.Dishes[d]!.Value.Species != Species.BoneSentry).ToList())
            {
                s.Remove(i);
                foreach (var sentry in BySpecies(s, Species.BoneSentry)) Boost(s, sentry, 20, 39);
            }
        };
        handlers[38] = (s, _, rng) =>
        {
            var overflow = 0;
            for (var n = 0; n < 4; n++)
                if (AddFresh(s, Species.Pests, rng) < 0) overflow++;
            if (overflow == 0) return;
            foreach (var i in BySpecies(s, Species.Pests)) Boost(s, i, 10 * overflow, 15 * overflow);
        };
        handlers[39] = (s, _, _) =>
        {
            foreach (var i in BySpecies(s, Species.Agito))
            {
                var rarity = s.Dishes[i]!.Value.Rarity;
                if (rarity == Rarity.Boss) Boost(s, i, 120);
                else s.Awaken(i, (Rarity)Math.Min((int)rarity + 2, (int)Rarity.Boss), 120);
            }
        };
        handlers[41] = (s, _, rng) =>
        {
            if (TryRandom(s, rng, out var i)) Boost(s, i, 111, 111);
        };
        return handlers;
    }

    static void Xenopurge(SimState s, int dish, int quantity, int removals, Rng rng)
    {
        var species = s.Dishes[dish]!.Value.Species;
        Boost(s, dish, 0, quantity);
        var others = NonEmpty(s).Where(i => i != dish && s.Dishes[i]!.Value.Species != species).ToList();
        foreach (var i in rng.Sample(others, Math.Min(removals, others.Count))) s.Remove(i);
    }

    static List<int> NonEmpty(SimState s) => Enumerable.Range(0, SimState.DishCount).Where(i => s.Dishes[i] is not null).ToList();

    static List<int> BySpecies(SimState s, Species species) => NonEmpty(s).Where(i => s.Dishes[i]!.Value.Species == species).ToList();

    static bool TryRandom(SimState s, Rng rng, out int dish)
    {
        var all = NonEmpty(s);
        dish = all.Count == 0 ? -1 : rng.Pick(all);
        return dish >= 0;
    }

    static void Boost(SimState s, int dish, int activity, int quantity = 0)
    {
        var m = s.Dishes[dish]!.Value;
        s.Dishes[dish] = m with { Activity = m.Activity + activity, Quantity = m.Quantity + quantity };
    }

    static Monster Boosted(Monster m, int activity, int quantity) => m with { Activity = m.Activity + activity, Quantity = m.Quantity + quantity };

    static int AddFresh(SimState s, Species species, Rng rng, int activity = 0, int quantity = 0) =>
        s.Add(Boosted(Monster.Fresh(species, SimState.RandomRarity(rng)), activity, quantity));
}
