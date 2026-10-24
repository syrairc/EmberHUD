using System.Collections.Generic;
using System.Linq;

namespace Vorax.Sim;

public static class Round
{
    public const int Rounds = 13;
    public const int PotionRounds = 10;
    public const int FirstThreshold = 8000;
    public const int SecondThreshold = 28000;
    public const int MaxInstruments = 3;

    public static void EndRound(SimState state, Rng rng)
    {
        state.Rng = rng;
        foreach (var id in state.Instruments.Order().ToList())
            if (!Instruments.EventDriven(id)) Instruments.EndOfRound(state, id, rng);
    }

    public static SimState Play(SimState state, int potionSimId, IReadOnlyList<int> targets, Rng rng)
    {
        var next = state.Clone();
        if (next.Over) return next;
        next.Rng = rng;
        next.Events.Clear();
        if (potionSimId >= 0) Potions.Apply(next, potionSimId, targets, rng);
        EndRound(next, rng);
        var total = next.TotalActivity;
        if (!next.Passed8000 && total >= FirstThreshold)
        {
            next.Passed8000 = true;
            if (next.Instruments.Count + next.PendingInstrumentPicks < MaxInstruments) next.PendingInstrumentPicks++;
        }
        if (!next.Passed28000 && total >= SecondThreshold)
        {
            next.Passed28000 = true;
            if (next.Instruments.Count + next.PendingInstrumentPicks < MaxInstruments) next.PendingInstrumentPicks++;
        }
        next.Events.Clear();
        next.Rng = null;
        if (next.Round >= Rounds)
        {
            next.PendingInstrumentPicks = 0;
            next.Over = true;
            return next;
        }
        next.Round++;
        return next;
    }
}
