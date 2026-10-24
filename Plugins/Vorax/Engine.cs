using System;
using System.Collections.Generic;
using System.Linq;
using EmberHUD;
using Vorax.Sim;

namespace Vorax;

public sealed class Engine(SolveSettings settings, Rng rng)
{
    delegate Ranked Ranker(Func<SimState, double>? score, bool allowReroll);

    sealed record Ranked(Pick Pick, double[] Values, double? RerollValue);

    static readonly Pick NoPick = new([], "");

    readonly HashSet<string> logged = [];

    public Stage DetectStage(VoraxState state)
    {
        if (state.Offered.Length == 0) return Stage.None;
        if (state.Phase == VoraxPhase.Instrument) return state.Offered.All(c => c.Kind == VoraxCardKind.Instrument) ? Stage.Instrument : Stage.None;
        if (state.Phase != VoraxPhase.Round || !state.Offered.All(c => c.Kind is VoraxCardKind.Potion or VoraxCardKind.Bundle)) return Stage.None;
        return SelectedTargeting(state) == Targeting.None ? Stage.Potion : Stage.Target;
    }

    public Advice? Advise(VoraxState state, Strategy? strategy, MapOptions options)
    {
        var stage = DetectStage(state);
        if (stage == Stage.None) return null;
        var name = strategy?.Name ?? "";
        var mapped = StateMapper.Map(state, options);
        if (mapped.State is not { } sim) return Failed(stage, name, LogOnce(mapped.Error));
        var rank = BuildRanker(state, stage, sim, out var error);
        if (rank is null) return Failed(stage, name, LogOnce(error));

        if (strategy is not null)
        {
            var maxTargets = stage == Stage.Target ? (int)SelectedTargeting(state) : 0;
            VoraxCard? chosen = stage == Stage.Target ? state.Offered[SelectedIndex(state)] : null;
            var context = new StrategyContext(state, stage, chosen, maxTargets, (score, allowReroll) => rank(score, allowReroll).Pick, sim, OfferedSimIds(state, stage), stage == Stage.Target ? SelectedIndex(state) : -1);
            try
            {
                var pick = Ask(strategy, stage, context);
                error = pick is null ? $"{name} has no rule for {stage}" : Invalid(pick.Value, stage, state, sim, maxTargets);
                if (error is null) return new(stage, AdviceSource.Strategy, name, pick!.Value, null, null, null);
            }
            catch (Exception e)
            {
                error = $"{name} threw {e.GetType().Name}: {e.Message}";
            }
        }

        try
        {
            var best = rank(null, true);
            return new(stage, strategy is null ? AdviceSource.Simulator : AdviceSource.SimulatorFallback, name, best.Pick, best.Values, best.RerollValue, error);
        }
        catch (Exception e)
        {
            return Failed(stage, name, $"simulator threw {e.GetType().Name}: {e.Message}");
        }
    }

    static int[]? OfferedSimIds(VoraxState state, Stage stage)
    {
        var ids = new int[state.Offered.Length];
        for (var i = 0; i < ids.Length; i++)
        {
            var card = state.Offered[i];
            var found = stage == Stage.Instrument ? GameIds.TryInstrument(card.Id, out ids[i])
                : card.Kind == VoraxCardKind.Bundle ? GameIds.TryKitSimId(card.Id, out ids[i])
                : GameIds.TryPotion(card.Id, out ids[i]);
            if (!found) return null;
        }
        return ids;
    }

    string? LogOnce(string? error)
    {
        bool isNew;
        lock (logged) isNew = error is not null && logged.Add(error);
        if (isNew) Log.Warn($"Vorax: {error}");
        return error;
    }

    static Advice Failed(Stage stage, string name, string? error) => new(stage, AdviceSource.Simulator, name, NoPick, null, null, error);

    static int SelectedIndex(VoraxState state) => state.SelectedCard < 0 ? -1 : Array.FindIndex(state.Offered, c => c.Slot == state.SelectedCard);

    static Targeting SelectedTargeting(VoraxState state)
    {
        var selected = SelectedIndex(state);
        if (selected < 0) return Targeting.None;
        var card = state.Offered[selected];
        return card.Kind != VoraxCardKind.Bundle && GameIds.TryPotion(card.Id, out var simId) ? Potions.Get(simId).Targeting : Targeting.None;
    }

    static Pick? Ask(Strategy strategy, Stage stage, StrategyContext context) => stage switch
    {
        Stage.Instrument => strategy.PickInstrument(context),
        Stage.Potion => strategy.ShouldReroll(context) == true ? Pick.Reroll($"{strategy.Name} rerolls") : strategy.PickPotion(context),
        _ => strategy.PickTarget(context),
    };

    static string? Invalid(Pick pick, Stage stage, VoraxState state, SimState sim, int maxTargets)
    {
        if (pick.Indexes is null) return "pick has no indexes";
        if (pick.IsReroll) return stage == Stage.Potion && state.Rerolls > 0 ? null : "reroll not possible";
        var indexes = pick.Indexes;
        if (stage != Stage.Target)
            return indexes is [var index] && index >= 0 && index < state.Offered.Length ? null : $"pick [{string.Join(",", indexes)}] outside the {state.Offered.Length} choices";
        if (indexes.Length < 1 || indexes.Length > maxTargets) return $"{indexes.Length} targets for a potion taking {maxTargets}";
        if (indexes.Distinct().Count() != indexes.Length) return "same target twice";
        return indexes.All(s => s is >= 0 and < SimState.DishCount && sim.Dishes[s] is not null) ? null : $"target [{string.Join(",", indexes)}] is not a monster";
    }

    Ranker? BuildRanker(VoraxState state, Stage stage, SimState sim, out string? error)
    {
        error = null;
        if (stage == Stage.Instrument)
        {
            var instruments = new List<int>();
            foreach (var card in state.Offered)
            {
                if (!GameIds.TryInstrument(card.Id, out var instrumentId))
                {
                    error = $"instrument id {card.Id}";
                    return null;
                }

                instruments.Add(instrumentId);
            }

            return (score, _) =>
            {
                var r = Solver.RankInstruments(sim, instruments, settings, score, rng);
                return new(r.Best < 0 ? NoPick : Pick.One(r.Best, $"simulated {r.Values[r.Best]:0}"), r.Values, null);
            };
        }

        if (stage == Stage.Potion)
        {
            if (!StateMapper.TryPotions(state.Offered, out _, out error)) return null;
            var potions = new List<int>();
            var kits = new List<KitDef?>();
            foreach (var card in state.Offered)
            {
                var isKit = card.Kind == VoraxCardKind.Bundle;
                kits.Add(isKit && GameIds.TryKit(card.Id, out var kit) ? kit : null);
                potions.Add(!isKit && GameIds.TryPotion(card.Id, out var potionId) ? potionId : -1);
            }

            return (score, allowReroll) =>
            {
                var r = Solver.RankPotions(sim, potions, settings, score, allowReroll, rng, kits);
                var pick = r.RerollBest ? Pick.Reroll($"reroll {r.RerollValue:0} beats {r.Values[r.Best]:0}") : r.Best < 0 ? NoPick : Pick.One(r.Best, $"simulated {r.Values[r.Best]:0}");
                return new(pick, r.Values, allowReroll && state.Rerolls > 0 ? r.RerollValue : null);
            };
        }

        GameIds.TryPotion(state.Offered[SelectedIndex(state)].Id, out var targetedPotion);
        return (score, _) =>
        {
            var r = Solver.RankTargets(sim, targetedPotion, settings, score, rng);
            return r.Best < 0 ? new(NoPick, r.Values, null) : new(new(Solver.TargetOptions(sim, targetedPotion)[r.Best], $"simulated {r.Values[r.Best]:0}"), r.Values, null);
        };
    }
}
