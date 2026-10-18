using System;
using System.Collections.Generic;
using System.Numerics;

namespace Tartarus.Sim;

// the reference solver's position values, profile "rewards" (tool rewards on), in tier value units of 3^tier
sealed class Worth
{
    public const double WOpen = 1.5, WNow = 0.844, WWalk = 0.75, WStep = 0.5, WNest = -6, WChain = 6, WGoal = 1, WIncident = 40, WPass = 40, WOfficeIncident = 60, WConsume = 20;
    const double RainbowMult = 1.5, WUsurp = 0.5, WInstigate = 0.5, WCrow = 1, WShort = 0.1, WKeys = 1, WCage = 1, WStair = 0.25;
    const int SwapMaxTier = 3, MaidenSwapTier = 3, ScratchExtra = 2, Comp = 20;

    static readonly double[] V = [1, 3, 9, 27, 81, 243];

    [ThreadStatic] static Dictionary<UInt128, double>? memo;

    readonly Run run;
    readonly byte[] eats = new byte[4], eatsHigh = new byte[4], counters = new byte[4], countersHigh = new byte[4];
    readonly double[] openDist = new double[5];
    readonly int[]? maiden;
    readonly int doubleFrom;
    readonly bool fight, keepBeast, saw, whip, muzzle, skewer, crown, maul, collar, revenge;
    readonly double scapegoatLoss;
    readonly bool scapegoat;

    public Worth(Run run)
    {
        this.run = run;
        Memo.Clear();
        fight = run.Rules.Card == StartCard.CageFight;
        keepBeast = fight || run.Rules.EaterKeepsBeast;
        saw = run.Holds(ToolId.Saw);
        whip = run.Holds(ToolId.Whip);
        muzzle = run.Holds(ToolId.Muzzle);
        skewer = run.Holds(ToolId.Skewer);
        crown = run.Holds(ToolId.Crown);
        maul = run.Holds(ToolId.Maul);
        collar = run.Holds(ToolId.Collar);
        for (var b = 0; b < 4; b++)
        {
            counters[b] = (byte)(1 << (b + 1) % 4);
            if (skewer && b == (int)Beast.Rat) counters[b] |= 1 << (int)Beast.Rat;
            eats[b] = crown && b == (int)Beast.Lion ? (byte)15 : counters[b];
            var second = b is (int)Beast.Mammoth or (int)Beast.Lion ? 1 << (b + 2) % 4 : 0;
            eatsHigh[b] = (byte)(eats[b] | second);
            countersHigh[b] = (byte)(counters[b] | second);
        }
        doubleFrom = fight ? 0 : run.Rules.SecondMealFromTier;
        R = Rainbow(run);
        for (var cell = 1; cell < run.Cells.Length; cell++) if (!run.Cells[cell].Open) Sealed++;
        revenge = run.Holds(ToolId.Revenge) && Sealed > 0;
        AxeDoors = run.Holds(ToolId.Axe) ? Math.Min(2, Sealed) : 0;
        var weights = run.Chance.DoorTierWeights ?? Odds.DoorTierWeights;
        for (var t = 0; t < openDist.Length; t++) openDist[t] = t < weights.Length ? weights[t] : 0;
        if (run.Holds(ToolId.Hook) && openDist.AsSpan(1).ContainsAnyExcept(0)) openDist[0] = 0;
        Normalize(openDist);
        for (var t = 0; t < openDist.Length; t++) OpenValue += openDist[t] * V[t];
        double lift = 0, tiers = 0;
        if (run.Holds(ToolId.Maiden)) maiden = new int[5];
        foreach (var cell in run.Cells)
        {
            if (!cell.Open || cell.Tier >= Tiers.Chromatic) continue;
            Board++;
            lift += Value(cell.Tier + 1) - V[cell.Tier];
            AllLift2 += Value(cell.Tier + 2) - V[cell.Tier];
            tiers += V[cell.Tier];
            if (maiden is not null) maiden[cell.Tier]++;
        }
        Lift = Board > 0 ? lift / Board : 0;
        Lift2 = Board > 0 ? AllLift2 / Board : 0;
        scapegoat = run.Holds(ToolId.Scapegoat) && Board >= 3;
        scapegoatLoss = scapegoat ? 2 * tiers / Board : 0;
        SwapCap = scapegoat ? Tiers.Ultimate : Math.Max(maiden is not null ? Math.Max(SwapMaxTier, MaidenSwapTier) : SwapMaxTier, run.Holds(ToolId.Stake) ? Tiers.Rare : 0);
        GoalLeft = Left(ObjectiveKind.Walk);
        IncidentsLeft = Left(ObjectiveKind.Incident);
        ConsumesLeft = Left(ObjectiveKind.Consume);
    }

    static Dictionary<UInt128, double> Memo => memo ??= [];

    public double R { get; }

    public int Sealed { get; }

    public int Board { get; }

    public double Lift { get; }

    public double Lift2 { get; }

    public double AllLift2 { get; }

    public double OpenValue { get; }

    public int AxeDoors { get; }

    public int SwapCap { get; }

    public int GoalLeft { get; }

    public int IncidentsLeft { get; }

    public int ConsumesLeft { get; }

    public ReadOnlySpan<double> OpenDist => openDist;

    int Left(ObjectiveKind kind) => run.Objective.Kind == kind && !run.Objective.Paid ? Run.Target(kind) - run.Objective.Progress : 0;

    public double Value(int tier) => tier >= Tiers.Chromatic ? R : V[tier];

    public static double Tier(int tier) => V[tier];

    static void Normalize(Span<double> weights)
    {
        var total = 0.0;
        foreach (var w in weights) total += Math.Max(0, w);
        if (total == 0) total = 1;
        for (var i = 0; i < weights.Length; i++) weights[i] = Math.Max(0, weights[i]) / total;
    }

    public int Composition(Span<byte> comp, int exclude)
    {
        comp.Clear();
        var crowd = 0;
        for (var cell = 0; cell < run.Cells.Length; cell++)
        {
            var inmate = run.Cells[cell];
            if (!inmate.Open) continue;
            crowd++;
            if (cell != exclude && inmate.Tier is >= 0 and < Tiers.Chromatic) comp[inmate.Tier * 4 + (int)inmate.Beast]++;
        }
        return crowd;
    }

    byte EatsAt(int t, int b) => (t >= doubleFrom ? eatsHigh : eats)[b];

    byte CountersAt(int t, int b) => (t >= doubleFrom ? countersHigh : counters)[b];

    int TopMeal(int t, int b) => maul && b == (int)Beast.Mammoth ? Tiers.Ultimate : t;

    byte MealsAt(int t, int b, int mt) => mt != t ? CountersAt(t, b) : EatsAt(t, b);

    double Rise(int t, Span<byte> comp, int crowd, int keep)
    {
        if (t >= Tiers.Chromatic) return R;
        var sum = 0.0;
        for (var b = keep >= 0 ? keep : 0; b <= (keep >= 0 ? keep : 3); b++)
        {
            if (!revenge)
            {
                sum += Feed(t, b, comp, crowd);
                continue;
            }
            var foods = EatsAt(t, b);
            var spawned = 0.0;
            for (var f = 0; f < 4; f++)
            {
                if ((foods >> f & 1) == 0) continue;
                comp[t * 4 + f]++;
                spawned += V[t] + Feed(t, b, comp, crowd + 1);
                comp[t * 4 + f]--;
            }
            sum += 0.3 * spawned / BitOperations.PopCount(foods) + 0.7 * Feed(t, b, comp, crowd);
        }
        return keep >= 0 ? sum : sum / 4;
    }

    double Feed(int t, int b, Span<byte> comp, int crowd)
    {
        var from = fight ? 0 : t * 4;
        var key = (UInt128)(uint)(t | b << 3 | (muzzle ? crowd : 0) << 5);
        for (var i = from; i < Comp; i++) key |= (UInt128)comp[i] << 12 + 4 * i;
        if (Memo.TryGetValue(key, out var hit)) return hit;
        var best = double.NegativeInfinity;
        for (var mt = fight ? 0 : t; mt <= TopMeal(t, b); mt++)
        {
            var meals = MealsAt(t, b, mt);
            for (var f = 0; f < 4; f++)
            {
                var i = mt * 4 + f;
                if ((meals >> f & 1) == 0 || comp[i] == 0) continue;
                comp[i]--;
                best = Math.Max(best, Bite(t, b, comp, crowd, 0, true, mt));
                comp[i]++;
            }
        }
        if (best == double.NegativeInfinity || best - V[t] < 0) best = V[t];
        Memo[key] = best;
        return best;
    }

    double Bite(int t, int b, Span<byte> comp, int crowd, int extra, bool chained, int mealTier)
    {
        var gain = (maul && b == (int)Beast.Mammoth ? Math.Max(1, mealTier + 2 - t) : 1) + extra;
        if (muzzle && crowd <= 4) gain += 2;
        if (skewer && b == (int)Beast.Rat) gain++;
        if (crown && b == (int)Beast.Lion) gain += 2;
        var value = chained && collar ? 0.2 * Sawed(t, b, comp, crowd, gain) + 0.8 * Sawed(t, b, comp, crowd, gain + 1) : Sawed(t, b, comp, crowd, gain);
        return value - V[mealTier];
    }

    double Sawed(int t, int b, Span<byte> comp, int crowd, int gain)
    {
        var s = saw ? 0.25 : 0;
        var w = whip ? 0.35 : 0;
        if (s == 0 && w == 0) return After(t, b, comp, crowd, gain);
        return (1 - s) * (1 - w) * After(t, b, comp, crowd, gain) + (s + w - 2 * s * w) * After(t, b, comp, crowd, gain + 1) + s * w * After(t, b, comp, crowd, gain + 2);
    }

    double After(int t, int b, Span<byte> comp, int crowd, int gain) => Rise(Math.Min(Tiers.Chromatic, t + gain), comp, crowd - 1, keepBeast ? b : -1);

    public double DevourDelta(int tier, int beast, int meal, bool scratch, Span<byte> comp, int crowd, bool chained, int mealTier)
    {
        var i = mealTier * 4 + meal;
        if (!scratch) comp[i]--;
        var value = Bite(tier, beast, comp, scratch ? crowd + 1 : crowd, scratch ? ScratchExtra : 0, chained, mealTier) - V[tier];
        if (!scratch) comp[i]++;
        return value;
    }

    double BestMeal(int tier, int beast, Span<byte> comp, int crowd)
    {
        var best = double.NegativeInfinity;
        for (var mt = fight ? 0 : tier; mt <= TopMeal(tier, beast); mt++)
        {
            var meals = MealsAt(tier, beast, mt);
            for (var f = 0; f < 4; f++)
                if ((meals >> f & 1) != 0 && comp[mt * 4 + f] > 0) best = Math.Max(best, DevourDelta(tier, beast, f, false, comp, crowd, false, mt));
        }
        return best;
    }

    double Stair(Span<byte> comp)
    {
        var value = 0.0;
        for (var t = 0; t < Tiers.Chromatic; t++)
        {
            var present = 0;
            for (var f = 0; f < 4; f++) if (comp[t * 4 + f] > 0) present |= 1 << f;
            var fed = 0;
            for (var b = 0; b < 4; b++) if ((EatsAt(t, b) & present) != 0) fed++;
            value += fed / 4.0 * V[t];
        }
        return value;
    }

    public double StairDelta(Span<byte> comp, int tier, int beast, int sign)
    {
        if (tier >= Tiers.Chromatic) return 0;
        var before = Stair(comp);
        comp[tier * 4 + beast] = (byte)(comp[tier * 4 + beast] + sign);
        var after = Stair(comp);
        comp[tier * 4 + beast] = (byte)(comp[tier * 4 + beast] - sign);
        return WStair * (after - before);
    }

    double StairFresh(ReadOnlySpan<double> dist, Span<byte> comp)
    {
        var sum = 0.0;
        for (var t = 0; t < dist.Length; t++)
            if (dist[t] != 0)
                for (var b = 0; b < 4; b++) sum += dist[t] / 4 * StairDelta(comp, t, b, 1);
        return sum;
    }

    void SwapDist(int oldTier, Span<double> dist)
    {
        var weights = run.Chance.ReplaceTierWeights ?? Odds.ReplaceTierWeights;
        dist.Clear();
        for (var t = 0; t < Math.Min(weights.Length, Tiers.Chromatic); t++) dist[t] = weights[t];
        if (run.Holds(ToolId.Rope)) dist[0] = dist[1] = 0;
        if (scapegoat)
        {
            for (var t = 0; t <= Math.Min(oldTier, Tiers.Ultimate); t++) dist[t] = 0;
            if (!dist.ContainsAnyExcept(0)) dist[Math.Min(Tiers.Chromatic, oldTier + 1)] = 1;
        }
        Normalize(dist);
        if (!run.Holds(ToolId.Stake) || oldTier >= Tiers.Legendary) return;
        for (var t = 0; t < dist.Length; t++) dist[t] *= 0.6;
        dist[Tiers.Legendary] += 0.4;
    }

    public double SwapValue(int tier)
    {
        Span<double> dist = stackalloc double[6];
        SwapDist(tier, dist);
        double value = -V[tier], higher = 0;
        for (var t = 0; t < dist.Length; t++)
        {
            value += dist[t] * Value(t);
            if (t > tier) higher += dist[t];
        }
        if (AxeDoors > 0) value += higher * AxeDoors * WOpen * OpenValue;
        value -= scapegoatLoss;
        if (maiden is null) return value;
        Span<int> counts = stackalloc int[5];
        maiden.CopyTo(counts);
        if (counts[tier] > 0) counts[tier]--;
        for (var t = 0; t < dist.Length; t++)
        {
            int above = 0, below = 0;
            for (var u = 0; u < counts.Length; u++)
            {
                if (u > t) above += counts[u];
                if (u < t) below += counts[u];
            }
            if (dist[t] != 0 && above == below) value += dist[t] * (Value(t + 3) - V[t]);
        }
        return value;
    }

    public double Activation(int tier, int beast, Span<byte> comp, int crowd, bool swapLeft)
    {
        var value = 0.0;
        if (tier < Tiers.Chromatic) value = Math.Max(value, BestMeal(tier, beast, comp, crowd));
        if (swapLeft && tier <= SwapCap) value = Math.Max(value, SwapValue(tier));
        return value;
    }

    public void Tagged(Span<double> dist)
    {
        if (!run.Holds(ToolId.DogTag)) return;
        for (var t = dist.Length - 1; t >= 0; t--)
        {
            var moved = 0.25 * dist[t];
            dist[t] -= moved;
            dist[Math.Min(dist.Length - 1, t + 1)] += moved;
        }
    }

    public double FreshValue(ReadOnlySpan<double> dist, Span<byte> comp, int crowd, bool swapLeft)
    {
        double worth = 0, now = 0;
        for (var t = 0; t < dist.Length; t++)
        {
            if (dist[t] == 0) continue;
            worth += dist[t] * V[t];
            for (var b = 0; b < 4; b++) now += dist[t] / 4 * Activation(t, b, comp, crowd, swapLeft);
        }
        return WOpen * worth + WNow * now + StairFresh(dist, comp);
    }

    public double ExecutionValue(int tier, int beast, Span<byte> comp)
    {
        var prey = (beast + 1) % 4;
        int count = 0;
        var lost = 0.0;
        for (var t = 0; t < tier; t++)
        {
            count += comp[t * 4 + prey];
            lost += comp[t * 4 + prey] * V[t];
        }
        return count == 0 ? 0 : Value(Math.Min(Tiers.Chromatic, tier + count)) - V[tier] - lost;
    }

    public double? Event(EventKind kind, Span<byte> comp) => kind switch
    {
        EventKind.Execution => Execution(comp),
        EventKind.ChainReaction => WChain,
        EventKind.BehindClosedDoors => Trickster(),
        EventKind.RavenousAppetite => Appetite(comp),
        EventKind.HighRiskConvict => WOpen * V[Tiers.Legendary],
        EventKind.CrowWhispers => WCrow,
        EventKind.TasteOfFreedom => 3 * WOpen * OpenValue,
        EventKind.UsurpationI => Lift + 2 * Lift * WUsurp,
        EventKind.UsurpationII => 2 * Lift + 2 * Lift2 * WUsurp,
        EventKind.UsurpationIII => 2 * Lift2 + AllLift2 * WUsurp,
        EventKind.UsurpationIV => AllLift2,
        EventKind.ExtraPortion => Math.Min(3, Board) * Lift,
        EventKind.SewerRatway => WOpen * OpenValue,
        EventKind.TreasureHunt => WCrow * Math.Max(1, Sealed / 4.0),
        _ => null,
    };

    public double AnyEvent(Span<byte> comp)
    {
        var pool = run.EventPool;
        if (pool.Count == 0) return 0;
        var sum = 0.0;
        foreach (var kind in pool) sum += Event(kind, comp) ?? 0;
        return sum / pool.Count;
    }

    double Execution(Span<byte> comp)
    {
        var sum = 0.0;
        for (var t = 0; t < openDist.Length; t++)
            for (var b = 0; b < 4; b++) sum += openDist[t] / 4 * Math.Max(0, ExecutionValue(t, b, comp));
        return sum;
    }

    double Trickster()
    {
        var sum = 0.0;
        for (var t = 0; t < openDist.Length; t++) sum += openDist[t] * Math.Max(0, V[Tiers.Ultimate] - V[Math.Min(t, Tiers.Ultimate)]);
        return sum;
    }

    double Appetite(Span<byte> comp)
    {
        var low = int.MaxValue;
        for (var i = 0; i < Comp && low == int.MaxValue; i++) if (comp[i] > 0) low = i / 4;
        const int tries = 5;
        var sum = 0.0;
        for (var t = 0; t < openDist.Length; t++)
        {
            if (openDist[t] == 0) continue;
            var from = Math.Min(t, low);
            var choose = 1.0;
            for (var k = 0; k <= tries; k++)
            {
                if (k > 0) choose = choose * (tries - k + 1) / k;
                sum += openDist[t] * choose * Math.Pow(0.5, tries) * (Value(Math.Min(Tiers.Chromatic, from + k)) - Value(from));
            }
        }
        return sum;
    }

    public (double Bestow, double Instigate, double Tyranny) Office(Span<byte> comp)
    {
        var free = run.InstigateCells().Count;
        var each = AnyEvent(comp);
        var extra = run.Rules.Card == StartCard.Tyranny ? 3 : 0;
        var first = run.InstigateCount;
        var tyranny = extra > 0 ? extra * Lift + Math.Min(first + extra - 1, free) * each * WInstigate : 0;
        var made = Math.Min(extra > 0 ? 1 : first, free);
        var goal = IncidentsLeft > 0 ? WOfficeIncident * Math.Min(made, IncidentsLeft) / IncidentsLeft : 0;
        return (Lift, made * each * WInstigate + goal, tyranny);
    }

    public double Keys(int door)
    {
        if (!run.Holds(ToolId.Keys)) return 0;
        var size = run.Cells.Length;
        var beside = 0;
        foreach (var side in (ReadOnlySpan<int>)[(door + size - 1) % size, (door + 1) % size])
            if (side != 0 && !run.Cells[side].Open) beside++;
        return WKeys * 0.8 * beside * WOpen * OpenValue;
    }

    public double ShortChain(int steps, bool sealedHere) => run.Holds(ToolId.ShortChain) && steps is >= 1 and <= 3 && Sealed > (sealedHere ? 1 : 0) ? WShort * 0.5 * WOpen * OpenValue : 0;

    public double Cage => run.Holds(ToolId.Cage) ? WCage * 0.4 * WOpen * OpenValue : 0;

    public double LinkValue(int lion, int prey, int rounds)
    {
        var value = 0.0;
        foreach (var cell in (ReadOnlySpan<int>)[lion, prey])
        {
            if (!run.Cells[cell].Open) continue;
            var t = run.Cells[cell].Tier;
            value += Value(Math.Min(Tiers.Chromatic, t + rounds)) - V[t];
        }
        return value;
    }

    public double LinkLoss(int cell, bool certain)
    {
        foreach (var mark in run.Prey)
            if (mark.Lion == cell || mark.Target == cell)
                return LinkValue(mark.Lion, mark.Target, mark.Rounds) * (certain || mark.Target == cell ? 1 : 0.75);
        return 0;
    }

    static double Rainbow(Run run) => run.Chromatics >= run.Rules.ChromaticCap ? V[Tiers.Ultimate] : V[Tiers.Chromatic] * RainbowMult;

    public static double BoardValue(Run run)
    {
        var value = run.Chromatics * Rainbow(run);
        for (var cell = 0; cell < run.Cells.Length; cell++)
            if (run.Cells[cell].Open) value += V[run.Cells[cell].Tier] * (run.Meals(cell).Count > 0 ? 1.25 : 1);
        return value;
    }
}
