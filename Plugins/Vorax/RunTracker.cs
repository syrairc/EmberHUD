using System.Collections.Generic;
using System.Linq;
using EmberHUD;
using Vorax.Sim;

namespace Vorax;

public sealed class RunTracker
{
    int lastRound;
    bool passed8000;
    bool passed28000;
    readonly Dictionary<int, int> acquiredRounds = [];
    MapOptions options;

    public MapOptions Update(VoraxState state)
    {
        if (state.Phase == VoraxPhase.Other) return options;
        if (lastRound == 0 || (state.Round == 1 && lastRound > 1))
        {
            passed8000 = passed28000 = false;
            acquiredRounds.Clear();
        }

        lastRound = state.Round;
        var relics = state.Instruments.Count(c => c.Id is >= 200 and < 300);
        passed8000 |= state.Activity >= 8000 || relics >= 1;
        passed28000 |= state.Activity >= 28000 || relics >= 2;
        foreach (var card in state.Instruments) acquiredRounds.TryAdd(card.Id, state.Round);
        return options = new(passed8000, passed28000, new Dictionary<int, int>(acquiredRounds));
    }
}
