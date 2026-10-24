using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using EmberHUD;
using Vorax.Sim;

namespace Vorax;

public readonly record struct HighlightMark(Vector2 Min, Vector2 Max, bool Strategy, string Label);

public static class Highlight
{
    public static IReadOnlyList<HighlightMark> Marks(VoraxState state, Advice? advice, NamedRect[] parts)
    {
        var marks = new List<HighlightMark>();
        if (advice is null || advice.Stage == Stage.None) return marks;
        var strategy = advice.Source == AdviceSource.Strategy;
        var source = strategy ? $"Strategy: {advice.StrategyName}" : "Simulator";
        var indexes = advice.Pick.Indexes ?? [];

        if (advice.Pick.IsReroll)
        {
            AddPart(marks, parts, "RefreshBtn", strategy, $"{source} - Reroll");
            return marks;
        }

        if (advice.Stage == Stage.Target)
        {
            var picked = indexes.Select(slot => state.Monsters.FirstOrDefault(m => m.Slot == slot)).Where(m => m.HasRect).ToList();
            marks.AddRange(picked.Select(m => new HighlightMark(m.Min, m.Max, strategy, $"{source} - {m.Name}")));
            if (indexes.Length > 0 && state.SelectedMonsters.Length == indexes.Length && !indexes.Except(state.SelectedMonsters).Any()) AddPart(marks, parts, "ComfirmBtn", strategy, "Confirm");
            return marks;
        }

        if (indexes.Length != 1 || indexes[0] < 0 || indexes[0] >= state.Offered.Length) return marks;
        var card = state.Offered[indexes[0]];
        if (card.HasRect) marks.Add(new(card.Min, card.Max, strategy, $"{source} - {card.Name}"));
        if (state.SelectedCard == card.Slot) AddPart(marks, parts, "ComfirmBtn", strategy, "Confirm");
        return marks;
    }

    static void AddPart(List<HighlightMark> marks, NamedRect[] parts, string name, bool strategy, string label)
    {
        foreach (var part in parts)
            if (part.Name == name && part.Max.X > part.Min.X && part.Max.Y > part.Min.Y)
            {
                marks.Add(new(part.Min, part.Max, strategy, label));
                return;
            }
    }
}
