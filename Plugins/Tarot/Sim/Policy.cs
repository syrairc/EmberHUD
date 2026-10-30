using System;
using System.Collections.Generic;
using System.Linq;

namespace Tarot;

public static class Policy
{
    const double Never = -1000000;
    const int LowChestRarity = 3;
    const int OrangeCap = 5;
    const int RedCap = 6;
    const int RedHoldPosition = 50;
    static readonly double[] UpgradeGain = [0, 0, 0.75, 0.6, 0.96, 1.5, 2.1, 0];

    public static bool SlotPressure(SimState s) => s.Taken.Count + Sim.ChestsLeftAhead(s) > Rules.ChestSlots;

    public static double TileScore(SimState s, int index, Weights w)
    {
        if (index < 0 || index >= s.TileCfg.Length || index >= s.TileChest.Length) return w.Empty;
        var chest = s.TileChest[index];
        if (chest > 0)
        {
            if (s.Taken.Count >= Rules.ChestSlots) return w.Empty;
            var score = w.Chest * w.Values[Math.Min(chest, 7)] + w.Ticket;
            return chest <= LowChestRarity && SlotPressure(s) ? score - w.LowChestPenalty : score;
        }
        var effects = Rules.OfTile(s.TileCfg[index]);
        return effects.Length == 0 ? w.Empty : effects.Sum(e => EffectScore(s, e, w));
    }

    static double EffectScore(SimState s, Effect e, Weights w) => e.Kind switch
    {
        EffectKind.Life when e.Min < 0 => TrapScore(s, w),
        EffectKind.Life => e.Min * 0.5 * (s.Hp <= 2 ? w.LowLife : w.Life),
        EffectKind.GiveCards => (e.Min + e.Max) / 2.0 * w.Card,
        EffectKind.TileChange or EffectKind.RarityUpAhead or EffectKind.SetAheadRarity => w.Question,
        EffectKind.RarityUpCollected => w.UpgradeTile + UpgradeGain[Math.Max(0, s.Taken.Where(t => t <= e.Target).DefaultIfEmpty(0).Max())],
        _ => w.Empty,
    };

    static double TrapScore(SimState s, Weights w)
    {
        var score = -w.Trap;
        if (s.Hp <= 2) score -= w.TrapLowLife;
        if (s.Hp <= 1) score -= 30 - 2 * s.Taken.Count;
        return score;
    }

    public static double ActionScore(SimState s, Move m, Weights w)
    {
        if (m.End || m.HandIndex < 0 || m.HandIndex >= s.Hand.Count) return Never;
        var card = s.Hand[m.HandIndex];
        if (Rules.IsUpgradeCard(card)) return Never;
        var target = Math.Clamp(s.Position + m.Steps, 0, Math.Max(0, s.TileCfg.Length - 1));
        var score = TileScore(s, target, w);
        if (Rules.IsFallback(card)) score += w.RefillBias;
        if (m.Steps < 0) score -= w.BackCost * -m.Steps;
        return score;
    }

    public static int UpgradeToPlay(SimState s, Weights w)
    {
        var best = -1;
        var bestCount = 0;
        for (var i = 0; i < s.Hand.Count; i++)
        {
            var (count, cap) = Rules.Upgrade(s.Hand[i]);
            if (count <= bestCount || !UpgradeReady(s, cap)) continue;
            best = i;
            bestCount = count;
        }
        return best;
    }

    static bool UpgradeReady(SimState s, int cap) => cap switch
    {
        OrangeCap => s.Taken.Contains(OrangeCap),
        RedCap => s.Taken.Any(t => t <= cap) && (s.Hp < 3 || s.Position >= RedHoldPosition),
        _ => s.Taken.Any(t => t <= cap),
    };

    public static Move Choose(SimState s, Weights w, Rng rng)
    {
        var up = UpgradeToPlay(s, w);
        if (up >= 0) return new Move(up, 0, false);
        var best = Move.EndRun;
        var bestScore = double.NegativeInfinity;
        var ties = 0;
        foreach (var move in Sim.Legal(s).Where(m => !m.End))
        {
            var score = ActionScore(s, move, w);
            if (score > bestScore) { best = move; bestScore = score; ties = 1; }
            else if (score == bestScore && rng.Next(++ties) == 0) best = move;
        }
        return best;
    }
}
