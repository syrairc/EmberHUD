using System;
using System.Collections.Generic;
using System.Linq;

namespace Vorax.Sim;

public sealed partial class SimState
{
    const int MaxChainDepth = 8;

    internal static readonly Species[] AllSpecies = [Species.Pests, Species.BoneSentry, Species.Agito, Species.Aemberon];
    static readonly (Rarity Rarity, double Weight)[] RarityWeights = [(Rarity.Normal, 43.98493535769686), (Rarity.Magic, 28.425974506855187), (Rarity.Rare, 17.26303751545767), (Rarity.Boss, 20.96270306486954)];

    int _chainDepth;

    public int Add(Monster m)
    {
        var dish = Array.FindIndex(Dishes, d => d is null);
        if (dish < 0)
        {
            Raise(new(EventKind.AddOverflow, -1, m.Species, m.Species));
            return -1;
        }
        Dishes[dish] = m;
        Raise(new(EventKind.Add, dish, m.Species, m.Species));
        return dish;
    }

    public Monster? Remove(int dish)
    {
        var m = Dishes[dish];
        if (m is null) return null;
        Dishes[dish] = null;
        Raise(new(EventKind.Remove, dish, m.Value.Species, m.Value.Species));
        return m;
    }

    public void Mutate(int dish, Species? to, Rarity? rarity, Rng rng)
    {
        if (Dishes[dish] is not { } m) return;
        var species = to ?? rng.Pick(AllSpecies);
        var newRarity = rarity ?? RandomRarity(rng);
        Dishes[dish] = m with { Species = species, Rarity = newRarity };
        Raise(new(EventKind.Mutate, dish, m.Species, species));
    }

    public void Awaken(int dish, Rarity? to = null, int? bonus = null)
    {
        if (Dishes[dish] is not { } m) return;
        var next = to ?? (m.Rarity == Rarity.Boss ? Rarity.Boss : m.Rarity + 1);
        if (next < m.Rarity) next = m.Rarity;
        var gained = bonus ?? next switch { Rarity.Normal => 0, Rarity.Magic => 60, _ => 120 };
        Dishes[dish] = m with { Rarity = next, Activity = m.Activity + gained };
        Raise(new(EventKind.Awaken, dish, m.Species, m.Species));
    }

    public int Fuse(IReadOnlyList<int> dishes, Species species, Rarity rarity)
    {
        int activity = 0, quantity = 0;
        var sources = new List<int>();
        foreach (var i in dishes)
            if (Dishes[i] is { } m)
            {
                activity += m.Activity;
                quantity += m.Quantity;
                sources.Add(i);
            }
        if (sources.Count == 0) return -1;
        foreach (var i in sources) Remove(i);
        var dish = Array.FindIndex(Dishes, d => d is null);
        if (dish < 0) return -1;
        Dishes[dish] = new Monster(species, rarity, activity, quantity);
        Raise(new(EventKind.Add, dish, species, species));
        Raise(new(EventKind.Fuse, dish, species, species));
        return dish;
    }

    public void Devour(int eater, int prey)
    {
        if (eater == prey || Dishes[eater] is not { } e || Dishes[prey] is not { } p) return;
        Dishes[eater] = e with { Activity = e.Activity + p.Activity, Quantity = e.Quantity + p.Quantity };
        Remove(prey);
        Raise(new(EventKind.Devour, eater, e.Species, p.Species));
    }

    // owned reactive instruments answer each event the moment it happens, in id order, chains stop at depth 8
    void Raise(SimEvent e)
    {
        Events.Add(e);
        Dispatch(e);
    }

    internal void Dispatch(SimEvent e)
    {
        if (_chainDepth >= MaxChainDepth || Rng is not { } rng) return;
        List<int>? reactive = null;
        foreach (var id in Instruments)
            if (Sim.Instruments.EventDriven(id)) (reactive ??= []).Add(id);
        if (reactive is null) return;
        reactive.Sort();
        var depth = _chainDepth;
        foreach (var id in reactive)
        {
            _chainDepth = depth + 1;
            Sim.Instruments.React(this, id, e, rng);
        }
        _chainDepth = depth;
    }

    internal static Rarity RandomRarity(Rng rng)
    {
        var roll = rng.NextDouble() * RarityWeights.Sum(w => w.Weight);
        var cumulative = 0.0;
        foreach (var (rarity, weight) in RarityWeights)
        {
            cumulative += weight;
            if (roll < cumulative) return rarity;
        }
        return Rarity.Boss;
    }
}
