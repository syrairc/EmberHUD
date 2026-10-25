using System.Collections.Generic;
using System.Linq;

namespace Vorax.Sim;

// Offer weights are the Notorious fork numbers, fitted on real runs (hand draws, supreme chance, instrument offers), the game never shows them
public sealed record DrawProfile(double SupremeChance, IReadOnlyDictionary<int, double> Offer)
{
    public int[] OfferIds { get; } = [.. Offer.Keys.Order()];
}

public static class DrawModel
{
    const double SupremeChance = 0.013;
    const int Large = 33, Small = 34, RareSmall = 40;
    static readonly int[] Supreme = [35, 36, 37, 38, 39, 41];
    static readonly HashSet<int> Boxes = [Large, Small, RareSmall];
    static readonly Dictionary<int, double> Offer = BuildOffer();
    static readonly int[] OfferIds = [.. Offer.Keys.Order()];
    static readonly IReadOnlyList<(int SimId, double Weight)> Default = BuildDefault();

    public static DrawProfile Standard { get; } = new(SupremeChance, Offer);

    // TLI Overlay Plus's crowd counts of card slots (20,866 boards, 2026-10-21) by sim id, kits included
    public static DrawProfile OverlayPlus { get; } = new(0.0082, new Dictionary<int, double>
    {
        [0] = 31, [1] = 34, [2] = 26, [3] = 24, [4] = 30, [5] = 20, [6] = 31, [7] = 34, [8] = 27, [9] = 35, [10] = 44, [11] = 29,
        [12] = 40, [13] = 44, [14] = 31, [15] = 20, [16] = 42, [17] = 36, [18] = 41, [19] = 29, [20] = 34, [21] = 43, [22] = 39,
        [23] = 46, [24] = 46, [25] = 49, [26] = 54, [27] = 68, [28] = 64, [29] = 46, [30] = 43, [31] = 59, [32] = 40,
        [Large] = 10, [Small] = 15, [RareSmall] = 20,
    });

    static readonly Dictionary<int, double> InstrumentBasic = new()
    {
        [1] = 0.13120819718391608, [2] = 0.10096880798918542, [3] = 0.08508031536144559, [4] = 0.1619601183988964,
        [5] = 0.10045627596893576, [6] = 0.14812175385215526, [7] = 0.16503531052039444, [8] = 0.09734395556082358,
    };

    static readonly Dictionary<int, double> InstrumentAdvanced = new()
    {
        [9] = 1, [10] = 1.131050605060506, [11] = 1, [12] = 1, [13] = 1, [14] = 1, [15] = 1, [16] = 1, [17] = 1, [18] = 1,
        [19] = 0.25, [20] = 0.25, [21] = 1, [22] = 0.25, [23] = 0.25, [24] = 1, [25] = 0.125,
    };

    // how the fork values an instrument when it has to pick one without looking ahead
    static readonly Dictionary<int, double> Strength = new()
    {
        [19] = 730655.579, [23] = 702500.892, [18] = 632679.946, [2] = 598565.458, [6] = 590653.633, [15] = 568622.928,
        [13] = 529036.304, [14] = 510384.721, [17] = 493509.333, [7] = 492872.202, [21] = 490747.581, [24] = 484925.698,
        [10] = 482764.132, [12] = 482048.088, [22] = 478912.077, [4] = 475667.902, [11] = 467945.344, [1] = 448594.592,
        [8] = 447815.551, [5] = 438493.716, [9] = 433228.819, [25] = 432532.566, [3] = 429863.603, [16] = 402664.767, [20] = 393278.47,
    };

    public static double InstrumentStrength(int simId) => Strength.GetValueOrDefault(simId);

    public static bool IsKit(int simId) => Boxes.Contains(simId);

    public static KitDef? KitOf(int simId) => simId switch
    {
        Large => new KitDef(5, [40, 44, 15, 1]),
        Small => new KitDef(3, [40, 44, 15, 1]),
        RareSmall => new KitDef(3, [40, 44, 18, 2]),
        _ => null,
    };

    public static IReadOnlyList<(int SimId, double Weight)> PotionPool(IReadOnlyDictionary<int, int>? realWeights)
    {
        if (realWeights is not { Count: > 0 }) return Default;
        return realWeights.Where(kv => kv.Value > 0 && kv.Key >= 0 && kv.Key < Potions.All.Count && !Boxes.Contains(kv.Key)).Select(kv => (kv.Key, (double)kv.Value)).ToList();
    }

    // Normal, Magic, Rare, Supreme - the order of KitDef.RarityWeights
    public static int RarityClass(int simId) => simId switch { <= 6 => 2, <= 22 => 1, <= 32 => 0, _ => 3 };

    public static IReadOnlyList<(int SimId, double Weight)> KitPool(KitDef kit, IReadOnlyList<(int SimId, double Weight)> pool)
    {
        var result = new List<(int, double)>();
        for (var rarity = 0; rarity < 4 && rarity < kit.RarityWeights.Length; rarity++)
        {
            var members = pool.Where(p => RarityClass(p.SimId) == rarity).ToList();
            var total = members.Sum(p => p.Weight);
            if (kit.RarityWeights[rarity] <= 0 || total <= 0) continue;
            result.AddRange(members.Select(p => (p.SimId, kit.RarityWeights[rarity] * p.Weight / total)));
        }
        return result;
    }

    public static int[] Draw(IReadOnlyList<(int SimId, double Weight)> pool, int count, Rng rng)
    {
        var left = new List<(int SimId, double Weight)>(pool);
        var hand = new int[System.Math.Min(count, left.Count)];
        for (var n = 0; n < hand.Length; n++)
        {
            var roll = rng.NextDouble() * left.Sum(p => p.Weight);
            var pick = 0;
            for (; pick < left.Count - 1; pick++)
            {
                roll -= left[pick].Weight;
                if (roll < 0) break;
            }
            hand[n] = left[pick].SimId;
            left.RemoveAt(pick);
        }
        return hand;
    }

    // a hand the way the fork deals it: per slot a supreme roll, then a weighted pick without repeats. An opened kit deals by its own rarity weights
    public static int[] DrawHand(Rng rng, int count, KitDef? kit = null, DrawProfile? profile = null)
    {
        var taken = new HashSet<int>();
        var hand = new List<int>();
        for (var n = 0; n < count; n++)
        {
            var pick = kit is { } k ? DrawFromKit(rng, k, taken) : DrawNormal(rng, taken, profile ?? Standard);
            if (pick < 0) break;
            taken.Add(pick);
            hand.Add(pick);
        }
        return [.. hand];
    }

    static int DrawNormal(Rng rng, HashSet<int> taken, DrawProfile profile)
    {
        var supreme = rng.NextDouble() < profile.SupremeChance;
        var common = profile.OfferIds.Where(id => !taken.Contains(id)).ToList();
        var rare = Supreme.Where(id => !taken.Contains(id)).ToList();
        var list = supreme ? rare : common;
        if (list.Count == 0) list = supreme ? common : rare;
        if (list.Count == 0) return -1;
        if (supreme && rare.Count > 0) return rng.Pick(list);
        var roll = rng.NextDouble() * list.Sum(id => profile.Offer.GetValueOrDefault(id, 1));
        foreach (var id in list)
        {
            roll -= profile.Offer.GetValueOrDefault(id, 1);
            if (roll < 0) return id;
        }
        return list[^1];
    }

    static int DrawFromKit(Rng rng, KitDef kit, HashSet<int> taken)
    {
        var classes = Enumerable.Range(0, 4)
            .Select(c => (Weight: (double)kit.RarityWeights[c], Members: Enumerable.Range(0, Potions.All.Count).Where(id => !Boxes.Contains(id) && RarityClass(id) == c && !taken.Contains(id)).ToList()))
            .Where(c => c.Weight > 0 && c.Members.Count > 0).ToList();
        if (classes.Count == 0) return -1;
        var roll = rng.NextDouble() * classes.Sum(c => c.Weight);
        foreach (var c in classes)
        {
            roll -= c.Weight;
            if (roll < 0) return rng.Pick(c.Members);
        }
        return rng.Pick(classes[^1].Members);
    }

    // three distinct offers from the basic pool while nothing is owned, the advanced pool after that
    public static int[] DrawInstruments(Rng rng, IReadOnlyCollection<int> owned)
    {
        var weights = owned.Count == 0 ? InstrumentBasic : InstrumentAdvanced;
        var left = weights.Keys.Order().Where(id => !owned.Contains(id)).ToList();
        var offers = new List<int>();
        while (offers.Count < 3 && left.Count > 0)
        {
            var roll = rng.NextDouble() * left.Sum(id => weights[id]);
            var pick = left[^1];
            foreach (var id in left)
            {
                roll -= weights[id];
                if (roll < 0)
                {
                    pick = id;
                    break;
                }
            }
            left.Remove(pick);
            offers.Add(pick);
        }
        return [.. offers];
    }

    static Dictionary<int, double> BuildOffer()
    {
        var weights = new Dictionary<int, double>
        {
            [0] = 1.3997486597499431, [6] = 0.9227298472841119, [10] = 2, [12] = 1.7279325425949048, [13] = 1.5775778294509257,
            [16] = 1.6415359154651594, [19] = 0.9270249224306107, [31] = 2.2003557896774977, [32] = 0.7775696441677071,
            [Large] = 0.878, [Small] = 0.878, [RareSmall] = 1.6109949489091089,
        };
        foreach (var id in Enumerable.Range(1, 5)) weights[id] = 0.878;
        foreach (var id in Enumerable.Range(23, 8)) weights[id] = 1.538;
        foreach (var id in Enumerable.Range(0, 33)) weights.TryAdd(id, 1);
        return weights;
    }

    static List<(int, double)> BuildDefault()
    {
        var common = Offer.Where(kv => !Boxes.Contains(kv.Key)).OrderBy(kv => kv.Key).ToList();
        var total = common.Sum(kv => kv.Value);
        return [.. common.Select(kv => (kv.Key, (1 - SupremeChance) * kv.Value / total)), .. Supreme.Select(id => (id, SupremeChance / Supreme.Length))];
    }
}
