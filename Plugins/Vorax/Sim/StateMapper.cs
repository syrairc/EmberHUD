using System;
using System.Collections.Generic;
using EmberHUD;

namespace Vorax.Sim;

public sealed record MapResult(SimState? State, string? Error);

public readonly record struct MapOptions(bool Passed8000, bool Passed28000, IReadOnlyDictionary<int, int>? AcquiredRounds = null);

public static class StateMapper
{
    public static MapResult Map(VoraxState state, MapOptions options)
    {
        var sim = new SimState { Round = state.Round, Rerolls = state.Rerolls, Passed8000 = options.Passed8000, Passed28000 = options.Passed28000 };
        foreach (var m in state.Monsters)
        {
            if (m.Slot is < 0 or >= SimState.DishCount) return Fail($"monster slot {m.Slot}");
            if (!Enum.IsDefined((Species)m.Species)) return Fail($"monster species {m.Species}");
            if (!Enum.IsDefined((Rarity)m.Rarity)) return Fail($"monster rarity {m.Rarity}");
            sim.Dishes[m.Slot] = new((Species)m.Species, (Rarity)m.Rarity, m.Activity, m.Count);
        }

        foreach (var card in state.Instruments)
        {
            if (GameIds.IsStartSetup(card.Id)) continue;
            if (!GameIds.TryInstrument(card.Id, out var simId)) return Fail($"instrument id {card.Id}");
            if (sim.Instruments.Contains(simId)) continue;
            sim.Instruments.Add(simId);
            if (options.AcquiredRounds is not null && options.AcquiredRounds.TryGetValue(card.Id, out var round)) sim.AcquiredRound[simId] = round;
        }

        return new(sim, null);
    }

    public static bool TryPotions(IEnumerable<VoraxCard> cards, out List<int> simIds, out string? error)
    {
        simIds = [];
        error = null;
        foreach (var card in cards)
        {
            if (card.Kind == VoraxCardKind.Bundle)
            {
                if (GameIds.TryKit(card.Id, out _)) continue;
                error = $"unknown bundle id {card.Id}";
                return false;
            }

            if (!GameIds.TryPotion(card.Id, out var simId))
            {
                error = $"potion id {card.Id}";
                return false;
            }

            simIds.Add(simId);
        }

        return true;
    }

    static MapResult Fail(string what) => new(null, $"unsupported {what}");
}
