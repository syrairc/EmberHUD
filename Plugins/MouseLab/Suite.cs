using System;
using System.Collections.Generic;
using System.Linq;
using EmberHUD;

namespace MouseLab;

public enum StepKind { Move, Click, MenuClick }

public enum MenuOption { Lock, More, None }

public sealed record Step(string Label, ScreenRect Target, bool Scored, StepKind Kind = StepKind.Move, MenuOption Option = MenuOption.Lock);

public sealed record Scenario(string Name, IReadOnlyList<Step> Steps);

public sealed record SuiteBuild(IReadOnlyList<Scenario> Scenarios, IReadOnlyList<string> Skipped);

public static class Suite
{
    const int Columns = 7;
    const int HopCount = 8;

    public static SuiteBuild Build(StashView stash, BagView? bag, int seed = 1, MenuOption? menuOption = null)
    {
        var cells = stash.Cells.GroupBy(cell => cell.Slot).ToDictionary(group => group.Key, group => group.First());
        var scenarios = new List<Scenario>();
        var skipped = new List<string>();

        void Fixed(string name, params (int Column, int Row)[] route)
        {
            var missing = route.Prepend((0, 0)).Where(point => !cells.ContainsKey(Slot(point))).Cast<(int Column, int Row)?>().FirstOrDefault();
            if (missing is { } gap) skipped.Add($"{name}: no stash cell ({gap.Column},{gap.Row})");
            else scenarios.Add(new Scenario(name, route.Select(point => Step(cells[Slot(point)], $"({point.Column},{point.Row})", true)).Prepend(Step(cells[0], "(0,0)", false)).ToArray()));
        }

        Fixed("Adjacent hop", (1, 0));
        Fixed("Row sweep", (6, 0));
        Fixed("Column sweep", (0, 4));
        Fixed("Diagonal", (6, 4));
        Fixed("Return trip", (3, 2), (0, 0));

        if (cells.ContainsKey(0) && cells.Count > 1)
        {
            var random = new Random(seed);
            var slots = cells.Keys.Order().ToArray();
            var route = new List<int> { 0 };
            while (route.Count <= HopCount)
            {
                var next = slots[random.Next(slots.Length)];
                if (next != route[^1]) route.Add(next);
            }
            scenarios.Add(new Scenario("Hop chain", route.Select((slot, index) => Step(cells[slot], index == 0 ? "start" : $"hop {index}", index > 0)).ToArray()));
        }
        else skipped.Add("Hop chain: needs stash cell (0,0) and one more");

        if (bag?.Cells.Length > 0 && cells.TryGetValue(0, out var origin))
            scenarios.Add(new Scenario("Bag to stash", [Step(origin, "start", false), Step(bag.Cells.MinBy(cell => cell.Slot), "bag", true), Step(origin, "stash", true)]));
        else skipped.Add("Bag to stash: needs a bag view with cells and stash cell (0,0)");

        if (menuOption is { } option and not MenuOption.None)
        {
            if (cells.TryGetValue(0, out var gear))
                scenarios.Add(new Scenario("Click and menu", [Step(gear, "cell", false) with { Kind = StepKind.Click }, Step(gear, option.ToString(), true) with { Kind = StepKind.MenuClick, Option = option }]));
            else skipped.Add("Click and menu: no stash cell (0,0)");
        }

        return new SuiteBuild(scenarios, skipped);
    }

    static int Slot((int Column, int Row) point) => point.Row * Columns + point.Column;

    static Step Step(BagCell cell, string label, bool scored) => new(label, new ScreenRect(cell.Min, cell.Max), scored);
}
