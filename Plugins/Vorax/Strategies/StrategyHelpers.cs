using System;
using System.Linq;
using EmberHUD;

namespace Vorax;

public static class StrategyHelpers
{
    static int FirstOffered(StrategyContext c, int[] priority)
    {
        foreach (var id in priority)
        {
            var index = Array.FindIndex(c.Choices, card => card.Id == id && card.Kind != VoraxCardKind.Bundle);
            if (index >= 0) return index;
        }

        return -1;
    }

    public static Pick? PickFirstOffered(StrategyContext c, int[] priority, string reason)
    {
        var index = FirstOffered(c, priority);
        return index < 0 ? null : Pick.One(index, reason);
    }

    public static bool Owns(StrategyContext c, int instrumentId) => c.State.Instruments.Any(i => i.Id == instrumentId);

    public static bool SimulatorWantsAKit(StrategyContext c)
    {
        if (!c.Choices.Any(card => card.Kind == VoraxCardKind.Bundle)) return false;
        var best = c.Simulate(null, false);
        return best.Indexes is [var index] && c.Choices[index].Kind == VoraxCardKind.Bundle;
    }
}
