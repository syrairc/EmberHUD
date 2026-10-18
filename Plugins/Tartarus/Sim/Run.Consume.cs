using System;
using System.Collections.Generic;
using System.Linq;

namespace Tartarus.Sim;

public sealed partial class Run
{
    bool CanManage => Phase == Phase.Manage && Chaining < 0 && Cells[Position].Open;

    IEnumerable<Move> ManageMoves()
    {
        if (!CanManage) yield break;
        if (Hunted) yield break;
        if (CanConsumeToday)
            foreach (var meal in Meals(Position)) yield return new Move(MoveKind.Consume, meal);
        if (CanReplaceToday && ReplaceTierWeights().Sum() > 0) yield return new Move(MoveKind.Replace);
        if (!Consumed && !Replaced)
            foreach (var prey in PreyTargets()) yield return new Move(MoveKind.Hunt, prey);
    }

    internal bool CanConsumeToday => !Consumed && !Hunted && (Rules.ReplaceAfterConsume || !Replaced);

    internal bool CanReplaceToday => !Replaced && !Hunted && (Rules.ReplaceAfterConsume || !Consumed);

    internal IEnumerable<int> PreyTargets()
    {
        var lion = Cells[Position];
        if (!Holds(ToolId.Trap) || !lion.Open || lion.Beast != Beast.Lion || Marked(Position)) return [];
        return Inmates().Where(cell => cell != Position && Reaches(lion, Cells[cell]) && !Marked(cell));
    }

    bool Marked(int cell) => Prey.Any(mark => mark.Lion == cell || mark.Target == cell);

    void Stalk(int prey)
    {
        Prey.Add(new PreyMark(Position, prey, 3, Day + 1));
        Hunted = true;
    }

    internal IReadOnlyList<int> Meals(int eaterCell)
    {
        var eater = Cells[eaterCell];
        if (!eater.Open || eater.Tier is < 0 or >= Tiers.Chromatic) return [];
        return [.. Enumerable.Range(0, Cells.Length).Where(cell => cell != eaterCell && Eatable(eater, cell))];
    }

    internal bool Eatable(Cell eater, int cell) =>
        Cells[cell].Open ? Reaches(eater, Cells[cell]) : eater.Beast == Beast.Wolf && Cells[cell].Markers.HasFlag(Marker.Scratch) && Cells[cell].Tier == eater.Tier;

    bool Eats(Cell eater, Beast meal)
    {
        var offset = Offset(eater.Beast, meal);
        return offset == 1 || offset == 2 && eater.Beast is Beast.Mammoth or Beast.Lion && (eater.Tier >= Rules.SecondMealFromTier || Rules.Card == StartCard.CageFight);
    }

    internal bool Reaches(Cell eater, Cell meal)
    {
        if (meal.Tier >= Tiers.Chromatic) return false;
        var cage = Rules.Card == StartCard.CageFight;
        if (Eats(eater, meal.Beast) && (meal.Tier == eater.Tier || cage && meal.Tier < eater.Tier)) return true;
        return eater.Beast switch
        {
            Beast.Rat => Holds(ToolId.Skewer) && meal.Beast == Beast.Rat && meal.Tier == eater.Tier,
            Beast.Lion => Holds(ToolId.Crown) && meal.Tier == eater.Tier,
            Beast.Mammoth => Holds(ToolId.Maul) && Eats(eater, meal.Beast) && meal.Tier > eater.Tier,
            _ => Holds(ToolId.Guillotine) && meal.Beast == Beast.Rat && meal.Tier < eater.Tier,
        };
    }

    static int Offset(Beast eater, Beast meal) => ((int)meal - (int)eater + 4) % 4;

    void Bite(int meal)
    {
        Eat(Position, meal, Chaining >= 0);
        Consumed = true;
        Chaining = Meals(Position).Count > 0 ? Position : -1;
    }

    void Eat(int eater, int meal, bool later)
    {
        var hunter = Cells[eater];
        var gain = Cells[meal].Open ? 1 : 3;
        if (Holds(ToolId.Maul) && hunter.Beast == Beast.Mammoth) gain = Math.Max(1, Cells[meal].Tier + 2 - hunter.Tier);
        if (Holds(ToolId.Muzzle) && Inmates().Count <= 4) gain += 2;
        Lock(meal);
        Progress(ObjectiveKind.Consume, 1);
        if (Holds(ToolId.Skewer) && hunter.Beast == Beast.Rat) gain++;
        if (Holds(ToolId.Crown) && hunter.Beast == Beast.Lion) gain += 2;
        if (Holds(ToolId.Saw) && Roll(25)) gain++;
        if (Holds(ToolId.Whip) && Roll(35)) gain++;
        if (later && Holds(ToolId.Collar) && Roll(80)) gain++;
        var casket = Holds(ToolId.Revenge) && Roll(30);
        var reaches = Cells[eater].Tier + gain >= Tiers.Chromatic;
        var tier = Raise(eater, gain);
        if (tier is >= 0 and < Tiers.Chromatic)
        {
            if (!reaches && !Rules.EaterKeepsBeast && Rules.Card != StartCard.CageFight)
                Cells[eater] = Cells[eater] with { Beast = Odds.DrawBeast(rng, tier, Chance.BeastWeights) };
            if (casket) Casket(eater);
        }
        Prey.RemoveAll(mark => !PreyHolds(mark));
        if (Holds(ToolId.Cage) && Roll(40) && Locked(meal)) OpenDoor(meal, 0, true);
    }

    void Casket(int eater)
    {
        var dark = LockedCells();
        if (dark.Count == 0) return;
        var hunter = Cells[eater];
        var beasts = Enum.GetValues<Beast>().Where(beast => Eats(hunter, beast)).ToList();
        if (beasts.Count == 0) return;
        Fill(dark[Pick(dark.Count)], new Cell(true, beasts[Pick(beasts.Count)], hunter.Tier, false));
    }

    double[] ReplaceTierWeights()
    {
        var weights = Chance.ReplaceTierWeights ?? Odds.ReplaceTierWeights;
        return Holds(ToolId.Rope) ? [.. weights.Select((weight, tier) => tier < 2 ? 0 : weight)] : weights;
    }

    void Replace()
    {
        var old = Cells[Position];
        Prey.RemoveAll(mark => mark.Lion == Position || mark.Target == Position);
        var scapegoat = Holds(ToolId.Scapegoat) && Inmates().Count(cell => cell != Position) >= 2;
        if (scapegoat)
            foreach (var other in Sample([.. Inmates().Where(cell => cell != Position)], 2)) Lock(other);
        var tier = Odds.Draw(rng, ReplaceTierWeights());
        if (scapegoat && tier <= old.Tier) tier = old.Tier + 1;
        if (Holds(ToolId.Stake) && old.Tier < Tiers.Legendary && Roll(40)) tier = Math.Max(tier, Tiers.Legendary);
        var beast = Holds(ToolId.Pestle) ? old.Beast : Odds.DrawBeast(rng, tier, Chance.BeastWeights);
        Cells[Position] = old with { Beast = beast, Tier = Math.Min(tier, Tiers.Ultimate) };
        Replaced = true;
        if (tier >= Tiers.Ultimate) Raise(Position, tier - Tiers.Ultimate);
        if (Holds(ToolId.Maiden) && Cells[Position].Open)
        {
            var others = Inmates().Where(cell => cell != Position).Select(cell => Cells[cell].Tier).ToList();
            if (others.Count(other => other > tier) == others.Count(other => other < tier)) tier = Math.Max(tier, Raise(Position, 3));
        }
        if (Holds(ToolId.Axe) && tier > old.Tier) OpenRandom(2);
        if (Holds(ToolId.Scales) && Roll(80)) Scout();
        if (old.Markers.HasFlag(Marker.Tusk))
            foreach (var cell in Sample(Inmates(), 4)) Raise(cell, 1);
    }
}
