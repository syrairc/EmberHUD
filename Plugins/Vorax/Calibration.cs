using System;
using System.Collections.Generic;
using System.Linq;
using EmberHUD;
using Vorax.Sim;

namespace Vorax;

// Checks the simulator against the real game: one log line per round. Kits and instrument picks are skipped, the sim does not play them
public sealed class Calibration(Action<string> log)
{
    const int Trials = 300;

    // Sim potion ids whose result does not depend on the rng, every other potion is compared against the outcomes of many sims
    static readonly HashSet<int> Deterministic = [0, 1, 2, 4, 15, 20, 22, 26, 36, 37, 39];

    readonly RunTracker tracker = new();
    readonly Rng rng = new((ulong)Environment.TickCount64);
    VoraxState? start;
    MapOptions startOptions;
    VoraxCard? card;
    int[] targets = [];
    string? kitSkip;
    bool sawInstrument;

    public void Observe(VoraxState state)
    {
        var options = tracker.Update(state);
        if (state.Phase == VoraxPhase.Instrument)
        {
            sawInstrument = true;
            return;
        }

        if (state.Phase != VoraxPhase.Round) return;
        if (start is null || state.Round < start.Round) Begin(state, options);
        else if (state.Round == start.Round) Track(state);
        else
        {
            Compare(state);
            Begin(state, options);
        }
    }

    void Begin(VoraxState state, MapOptions options)
    {
        start = state;
        startOptions = options;
        card = null;
        targets = [];
        kitSkip = null;
        sawInstrument = false;
        Track(state);
    }

    void Track(VoraxState state)
    {
        var index = Array.FindIndex(state.Offered, c => c.Slot == state.SelectedCard);
        if (state.SelectedCard < 0 || index < 0) return;
        var selected = state.Offered[index];
        if (selected.Kind == VoraxCardKind.Bundle) kitSkip = GameIds.TryKit(selected.Id, out _) ? "potion kit" : "monster kit";
        else (card, targets) = (selected, state.SelectedMonsters);
    }

    void Compare(VoraxState next)
    {
        var round = start!.Round;
        void Say(string text) => log($"Vorax calibration round {round}: {text}");
        if (next.Round != round + 1) Say($"skipped, round jumped to {next.Round}");
        else if (sawInstrument) Say("skipped, instrument pick");
        else if (kitSkip is not null) Say($"skipped, {kitSkip}");
        else if (card is not { } picked) Say("skipped, no potion pick seen");
        else if (!GameIds.TryPotion(picked.Id, out var potion)) Say($"skipped, potion id {picked.Id}");
        else
        {
            var before = StateMapper.Map(start, startOptions);
            var after = StateMapper.Map(next with { Instruments = [] }, default);
            if (before.State is not { } from || after.State is not { } real) Say($"skipped, {before.Error ?? after.Error}");
            else Say(Verdict(from, real, potion));
        }
    }

    string Verdict(SimState from, SimState real, int potion)
    {
        var name = Potions.Get(potion).Name;
        if (Deterministic.Contains(potion))
        {
            var diff = Diff(Round.Play(from, potion, targets, rng).Dishes, real.Dishes);
            return diff == "" ? $"{name} match" : $"{name} differs, {diff}";
        }

        string? first = null;
        for (var n = 0; n < Trials; n++)
        {
            var diff = Diff(Round.Play(from, potion, targets, rng).Dishes, real.Dishes);
            if (diff == "") return $"{name} match (random)";
            first ??= diff;
        }

        return $"{name} random, no outcome in {Trials} sims equals the board, e.g. {first}";
    }

    static string Diff(Monster?[] sim, Monster?[] real) =>
        string.Join("; ", Enumerable.Range(0, SimState.DishCount).Where(i => sim[i] != real[i]).Select(i => $"slot {i} sim {Show(sim[i])} real {Show(real[i])}"));

    static string Show(Monster? m) => m is { } v ? $"{v.Species} {v.Rarity} {v.Activity}x{v.Quantity}" : "empty";
}
