using System;
using System.Collections.Generic;
using System.Linq;

namespace Tarot;

public readonly record struct Move(int HandIndex, int Steps, bool End)
{
    public static readonly Move EndRun = new(-1, 0, true);
}

public static class Sim
{
    const int EmptyTile = 10000;
    const int ChestTile = 2010;
    const int MaxRarity = 7;
    static readonly double[] RarityWeights = [0.4, 0.3, 0.2, 0.08, 0.016, 0.004];

    public static IEnumerable<Move> Legal(SimState s)
    {
        for (var i = 0; i < s.Hand.Count; i++)
        {
            var card = s.Hand[i];
            if (Rules.IsUnknown(card)) continue;
            if (Rules.IsUpgradeCard(card)) { yield return new Move(i, 0, false); continue; }
            var options = Board.Options(card);
            if (options.Length == 0 && Rules.OfCard(card).FirstOrDefault(e => e.Kind == EffectKind.Move).Set is { Length: > 1 } set) options = set;
            if (options.Length == 0) options = [0];
            foreach (var steps in options) yield return new Move(i, steps, false);
        }
        yield return Move.EndRun;
    }

    public static void Play(SimState s, Move m, Rng rng)
    {
        if (s.Ended) return;
        if (m.End || m.HandIndex < 0 || m.HandIndex >= s.Hand.Count) { s.Ended = true; return; }
        var card = s.Hand[m.HandIndex];
        s.Hand.RemoveAt(m.HandIndex);
        if (card is >= 21 and <= 24) Land(s, m.Steps, rng);
        else Apply(s, Rules.OfCard(card), m.Steps, rng);
        if (s.Hp <= 0) { s.Dead = true; s.Ended = true; }
    }

    public static int ChestsLeftAhead(SimState s)
    {
        var count = 0;
        for (var i = s.Position + 1; i < s.TileChest.Length; i++) if (s.TileChest[i] > 0) count++;
        return count;
    }

    static void Apply(SimState s, Effect[] effects, int pickedSteps, Rng rng)
    {
        foreach (var e in effects)
        {
            if (s.Dead) return;
            switch (e.Kind)
            {
                case EffectKind.Move: Land(s, e.Set.Length == 1 ? e.Set[0] : pickedSteps, rng); break;
                case EffectKind.Life: s.Hp = Math.Min(Rules.MaxLife, s.Hp + e.Min); break;
                case EffectKind.GiveCards: Give(s, e, rng); break;
                case EffectKind.RarityUpCollected: RaiseCollected(s, e); break;
                case EffectKind.RarityUpAhead: RaiseAhead(s, e); break;
                case EffectKind.SetAheadRarity: SetAhead(s, e); break;
                case EffectKind.TileChange: Change(s, e, rng); break;
            }
            if (s.Hp <= 0) { s.Dead = true; s.Ended = true; return; }
        }
    }

    static void Land(SimState s, int steps, Rng rng)
    {
        if (s.TileCfg.Length == 0) return;
        var target = Math.Clamp(s.Position + steps, 0, s.TileCfg.Length - 1);
        if (target == s.Position) return;
        s.Position = target;
        if (s.TileChest[target] > 0)
        {
            if (s.Taken.Count < Rules.ChestSlots)
            {
                s.Taken.Add(s.TileChest[target]);
                s.TileChest[target] = 0;
                s.TileCfg[target] = EmptyTile;
            }
        }
        else
        {
            var tile = s.TileCfg[target];
            s.TileCfg[target] = EmptyTile;
            Apply(s, Rules.OfTile(tile), 0, rng);
        }
        if (target == s.TileCfg.Length - 1) s.Ended = true;
    }

    static void Give(SimState s, Effect e, Rng rng)
    {
        if (e.Set.Length == 0) return;
        var count = e.Min + (e.Max > e.Min ? rng.Next(e.Max - e.Min + 1) : 0);
        for (var i = 0; i < count && s.Hand.Count < Rules.HandCap; i++) s.Hand.Add(e.Set[rng.Next(e.Set.Length)]);
    }

    static void RaiseCollected(SimState s, Effect e)
    {
        var picked = Enumerable.Range(0, s.Taken.Count)
            .Where(i => s.Taken[i] <= e.Target && s.Taken[i] < MaxRarity)
            .OrderByDescending(i => s.Taken[i])
            .Take(e.Min)
            .ToArray();
        foreach (var i in picked) s.Taken[i]++;
    }

    static void RaiseAhead(SimState s, Effect e)
    {
        var left = e.Min;
        for (var i = s.Position + 1; i < s.TileChest.Length && left > 0; i++)
        {
            if (s.TileChest[i] <= 0 || s.TileChest[i] > e.Target || s.TileChest[i] >= MaxRarity) continue;
            s.TileChest[i]++;
            left--;
        }
    }

    static void SetAhead(SimState s, Effect e)
    {
        for (var i = s.Position + e.RangeMin; i <= s.Position + e.RangeMax && i < s.TileChest.Length; i++)
            if (i >= 0 && s.TileChest[i] > 0) s.TileChest[i] = e.Target;
    }

    static void Change(SimState s, Effect e, Rng rng)
    {
        var eligible = new List<int>();
        for (var i = Math.Max(0, s.Position + e.RangeMin); i <= s.Position + e.RangeMax && i < s.TileCfg.Length; i++)
            if (s.TileChest[i] == 0 && e.Set.Contains(s.TileCfg[i])) eligible.Add(i);
        var count = Math.Min(eligible.Count, e.Min + (e.Max > e.Min ? rng.Next(e.Max - e.Min + 1) : 0));
        for (var n = 0; n < count; n++)
        {
            var pick = rng.Next(eligible.Count);
            var tile = eligible[pick];
            eligible.RemoveAt(pick);
            Rewrite(s, tile, e.Target, rng);
        }
    }

    static void Rewrite(SimState s, int tile, int target, Rng rng)
    {
        s.TileCfg[tile] = target;
        s.TileChest[tile] = target switch
        {
            >= 2001 and <= 2007 => target - 2000,
            ChestTile => RollRarity(rng),
            _ => 0,
        };
    }

    static int RollRarity(Rng rng)
    {
        var roll = rng.NextDouble();
        for (var i = 0; i < RarityWeights.Length; i++)
        {
            roll -= RarityWeights[i];
            if (roll < 0) return i + 2;
        }
        return MaxRarity;
    }
}
