using System;
using System.Collections.Generic;
using System.Linq;

namespace Tartarus.Sim;

public static class Policy
{
    public static Move Choose(Run run, Rng rng)
    {
        var legal = run.Legal();
        if (legal.Count == 0) return new Move(MoveKind.End);
        if (legal.Count == 1) return legal[0];
        if (run.Offers.Count > 0) return legal[0].Kind == MoveKind.Reward ? Gift(run, legal, rng) : Tool(run, legal);
        var worth = new Worth(run);
        return run.Phase switch
        {
            Phase.Patrol => Patrol(run, worth, legal),
            Phase.Office => Office(run, worth),
            Phase.Event => Event(run, worth),
            _ when run.Chaining >= 0 => Chain(run, worth, legal),
            _ => Manage(run, worth, legal),
        };
    }

    static Move Tool(Run run, IReadOnlyList<Move> legal) => legal.MaxBy(move => ToolGain(run, (ToolId)move.Param));

    static double ToolGain(Run run, ToolId tool)
    {
        var table = run.Chance.Tools ?? ToolSets.Standard;
        if (run.Tools.Count == 0) return table.Single[(int)tool] - table.Base;
        var sum = 0.0;
        foreach (var held in run.Tools) sum += table.Pair(held, tool) - table.Single[(int)held];
        return sum / run.Tools.Count;
    }

    static Move Gift(Run run, IReadOnlyList<Move> legal, Rng rng)
    {
        bool Offered(RewardId gift) => legal.Any(move => move.Param == (int)gift);
        Move Take(RewardId gift) => new(MoveKind.Reward, Param: (int)gift);
        if (LastMoveMade(run))
        {
            var best = (Move: default(Move), Gain: 0);
            foreach (var move in legal)
            {
                var gain = 0;
                for (var i = 0; i < 3; i++) gain += Trial(run, move, rng).Chromatics - run.Chromatics;
                if (gain > best.Gain) best = (move, gain);
            }
            if (best.Gain > 0) return best.Move;
            if (Offered(RewardId.Ring)) return Take(RewardId.Ring);
        }
        var left = run.Offers.Count(offer => offer.Kind == MoveKind.Reward);
        if (Offered(RewardId.Horn))
            for (var i = 0; i < 3; i++)
                if (Trial(run, Take(RewardId.Horn), rng).Chromatics > run.Chromatics) return Take(RewardId.Horn);
        if (left >= 3 && Offered(RewardId.Ring) && run.Cells.Skip(1).Any(cell => !cell.Open)) return Take(RewardId.Ring);
        if (left >= 2 && Offered(RewardId.Wine) && run.Cells.Any(cell => cell.Open && cell.Tier < Tiers.Legendary)) return Take(RewardId.Wine);
        return legal.MaxBy(move =>
        {
            var samples = move.Param is (int)RewardId.Ring or (int)RewardId.Horn ? 3 : 1;
            var sum = 0.0;
            for (var i = 0; i < samples; i++) sum += Worth.BoardValue(Trial(run, move, rng));
            return sum / samples;
        });
    }

    static Run Trial(Run run, Move move, Rng rng)
    {
        var trial = run.Fork(new Rng(rng.Next()));
        trial.Apply(move);
        return trial;
    }

    static bool LastMoveMade(Run run) => run.Day >= run.MaxDay && run.Chaining < 0 && !run.CanConsumeToday && !run.CanReplaceToday && !(run.Phase == Phase.Manage && !run.Consumed && !run.Replaced && !run.Hunted && run.PreyTargets().Any());

    static Move Event(Run run, Worth worth)
    {
        var accept = run.PendingEvent switch
        {
            EventKind.Execution or EventKind.BehindClosedDoors when run.EventCell < 0 || !run.Cells[run.EventCell].Open => false,
            EventKind.BehindClosedDoors => run.Cells[run.EventCell].Tier < Tiers.Ultimate,
            EventKind.Execution => Execution(run, worth) > 0,
            _ => true,
        };
        return new Move(accept ? MoveKind.Confirm : MoveKind.Skip);
    }

    static double Execution(Run run, Worth worth)
    {
        Span<byte> comp = stackalloc byte[20];
        worth.Composition(comp, run.EventCell);
        var executioner = run.Cells[run.EventCell];
        return worth.ExecutionValue(executioner.Tier, (int)executioner.Beast, comp);
    }

    static Move Office(Run run, Worth worth)
    {
        Span<byte> comp = stackalloc byte[20];
        worth.Composition(comp, -1);
        var (bestow, instigate, _) = worth.Office(comp);
        return new Move(bestow >= instigate ? MoveKind.Bestow : MoveKind.Instigate);
    }

    static Move Patrol(Run run, Worth worth, IReadOnlyList<Move> legal)
    {
        Span<byte> comp = stackalloc byte[20];
        var crowd = worth.Composition(comp, -1);
        var swapLeft = run.CanReplaceToday;
        Span<double> open = stackalloc double[5];
        worth.OpenDist.CopyTo(open);
        worth.Tagged(open);
        var fresh = worth.FreshValue(open, comp, crowd + 1, swapLeft);
        var size = run.Cells.Length;
        var range = Math.Min(run.Rules.DoorWindow, size - 1);
        var best = (Move: legal[0], Score: double.NegativeInfinity);
        Span<double> dist = stackalloc double[5];
        foreach (var move in legal)
        {
            var door = move.Cell;
            var cell = run.Cells[door];
            var sealedHere = door != 0 && !cell.Open;
            var walk = (door - run.Position + size) % size;
            var steps = !run.Ratway && walk <= range ? walk : 0;
            var score = Worth.WStep * steps;
            if (run.ChainArmed && !run.Ratway)
            {
                var passed = 0;
                for (var step = 1; step <= Math.Max(1, steps); step++)
                {
                    var on = steps > 0 ? (run.Position + step) % size : door;
                    if (on != 0 && !run.Cells[on].Open) passed++;
                }
                score += Worth.WOpen * (passed - (sealedHere ? 1 : 0)) * worth.OpenValue + (sealedHere ? fresh : cell.Open ? Worth.WWalk * Known(run, worth, door, swapLeft) : 0);
            }
            else if (sealedHere && cell.Markers.HasFlag(Marker.Nest)) score += Worth.WNest;
            else if (sealedHere)
            {
                if (cell.Known && cell.Tier >= 0 || cell.AtLeast > 0)
                {
                    dist.Clear();
                    if (cell.Known && cell.Tier >= 0) dist[Math.Min(Tiers.Ultimate, cell.Tier)] = 1;
                    else Floor(worth.OpenDist, cell.AtLeast, dist);
                    worth.Tagged(dist);
                    score += worth.FreshValue(dist, comp, crowd + 1, swapLeft);
                }
                else score += fresh;
                if (run.Holds(ToolId.Blacklist) && worth.Sealed - 1 <= 5) score += 2 * worth.Lift;
                score += worth.Keys(door);
                if (cell.Event is { } waiting) score += worth.Event(waiting, comp) ?? 0;
                else if (cell.Marked) score += worth.AnyEvent(comp);
            }
            else if (door == 0)
            {
                var (bestow, instigate, tyranny) = worth.Office(comp);
                score += Math.Max(bestow, instigate) + tyranny;
            }
            else if (cell.Open)
            {
                score += Worth.WWalk * Known(run, worth, door, swapLeft);
                if (cell.Event is { } waiting) score += worth.Event(waiting, comp) ?? worth.AnyEvent(comp);
                else if (cell.Marked) score += worth.AnyEvent(comp);
            }
            if (run.Holds(ToolId.Shackles) && worth.Board > 0 && steps >= 4) score += 0.4 * worth.Lift;
            score += worth.ShortChain(steps, sealedHere);
            if (worth.GoalLeft > 0) score += Worth.WGoal * Math.Min(steps, worth.GoalLeft);
            if (worth.IncidentsLeft > 0)
            {
                if (Incident(run, door)) score += Worth.WIncident / worth.IncidentsLeft;
                if (steps > 1)
                    for (var step = 1; step < steps; step++)
                        if (Incident(run, (run.Position + step) % size)) score -= Worth.WPass / worth.IncidentsLeft;
            }
            if (score > best.Score) best = (move, score);
        }
        return best.Move;
    }

    static bool Incident(Run run, int cell) => run.Cells[cell].HasEvent && cell != 0;

    static void Floor(ReadOnlySpan<double> dist, int floor, Span<double> kept)
    {
        var total = 0.0;
        for (var t = 0; t < kept.Length; t++) total += kept[t] = t >= floor ? dist[t] : 0;
        for (var t = 0; t < kept.Length; t++) kept[t] = total > 0 ? kept[t] / total : dist[t];
    }

    static double Known(Run run, Worth worth, int cell, bool swapLeft)
    {
        Span<byte> comp = stackalloc byte[20];
        var crowd = worth.Composition(comp, cell);
        return worth.Activation(run.Cells[cell].Tier, (int)run.Cells[cell].Beast, comp, crowd, swapLeft);
    }

    static (Move Move, double Score) BestDevour(Run run, Worth worth, IReadOnlyList<Move> legal)
    {
        Span<byte> comp = stackalloc byte[20];
        var here = run.Position;
        var crowd = worth.Composition(comp, here);
        var eater = run.Cells[here];
        var own = worth.LinkLoss(here, false);
        var ownMark = run.Prey.FindIndex(mark => mark.Lion == here || mark.Target == here);
        var best = (Move: default(Move), Score: double.NegativeInfinity);
        foreach (var move in legal)
        {
            if (move.Kind != MoveKind.Consume) continue;
            var meal = run.Cells[move.Cell];
            var scratch = !meal.Open;
            var mealLink = scratch ? 0 : worth.LinkLoss(move.Cell, true);
            var same = own > 0 && mealLink > 0 && ownMark == run.Prey.FindIndex(mark => mark.Lion == move.Cell || mark.Target == move.Cell);
            var loss = same ? Math.Max(own, mealLink) : own + mealLink;
            var stair = scratch ? 0 : worth.StairDelta(comp, meal.Tier, (int)meal.Beast, -1);
            var score = worth.DevourDelta(eater.Tier, (int)eater.Beast, (int)meal.Beast, scratch, comp, crowd, run.Chaining >= 0, scratch ? eater.Tier : meal.Tier) - loss + stair + worth.Cage;
            if (score > best.Score) best = (move, score);
        }
        return best;
    }

    internal static Move ChainMeal(Run run)
    {
        var best = BestDevour(run, new Worth(run), run.Legal());
        return best.Score > double.NegativeInfinity ? best.Move : new Move(MoveKind.StopChain);
    }

    static Move Chain(Run run, Worth worth, IReadOnlyList<Move> legal)
    {
        var best = BestDevour(run, worth, legal);
        return best.Score < 0 ? new Move(MoveKind.StopChain) : best.Move;
    }

    static Move Manage(Run run, Worth worth, IReadOnlyList<Move> legal)
    {
        var swap = legal.Contains(new Move(MoveKind.Replace)) ? SwapGain(run, worth) : double.NegativeInfinity;
        var hunt = (Move: default(Move), Value: double.NegativeInfinity);
        foreach (var move in legal)
        {
            if (move.Kind != MoveKind.Hunt) continue;
            var value = 0.8 * worth.LinkValue(run.Position, move.Cell, 3);
            if (value > hunt.Value) hunt = (move, value);
        }
        var devour = BestDevour(run, worth, legal);
        if (devour.Score > double.NegativeInfinity && worth.ConsumesLeft > 0) devour.Score += Worth.WConsume / worth.ConsumesLeft;
        if (devour.Score >= 0)
        {
            if (hunt.Value > devour.Score && hunt.Value > swap) return hunt.Move;
            if (swap > 0 && swap > devour.Score) return new Move(MoveKind.Replace);
            return devour.Move;
        }
        if (hunt.Value > 0 && hunt.Value > swap) return hunt.Move;
        return swap > 0 ? new Move(MoveKind.Replace) : new Move(MoveKind.End);
    }

    static double SwapGain(Run run, Worth worth)
    {
        var here = run.Cells[run.Position];
        var gain = worth.SwapValue(here.Tier) - worth.LinkLoss(run.Position, true);
        if (!here.Markers.HasFlag(Marker.Tusk)) return here.Tier <= worth.SwapCap ? gain : double.NegativeInfinity;
        var board = run.Cells.Where(cell => cell.Open && cell.Tier < Tiers.Chromatic).ToList();
        if (board.Count == 0) return gain;
        return gain + Math.Min(4, board.Count) * board.Average(cell => Worth.Tier(cell.Tier + 1) - Worth.Tier(cell.Tier));
    }
}
