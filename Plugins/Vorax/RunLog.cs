using System;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using EmberHUD;
using Vorax.Sim;

namespace Vorax;

// One line per event of a run, `run=<id> <event> <json>`, so a game can be replayed from the log: every advice we gave, what the player did next and whether it followed the advice, how each round ended
public sealed class RunLog(Action<string> log)
{
    readonly Stopwatch clock = new();
    string? run;
    VoraxState? last;
    Advice? logged;
    string decisionKey = "";
    string stageKey = "";

    public string Config { get; set; } = "";

    public void Observe(VoraxState? state, Advice? advice)
    {
        if (state is null)
        {
            End();
            return;
        }

        if (run is null || state.Round < last!.Round)
        {
            End();
            Start(state);
        }

        Track(state);
        EmitStage(state);
        Decide(state, advice);
        last = state;
    }

    void Start(VoraxState state)
    {
        run = Guid.NewGuid().ToString("N")[..6];
        Emit("start", new { round = state.Round, rounds = state.Rounds, activity = state.Activity, config = Config });
    }

    void End()
    {
        if (run is null || last is not { } s) return;
        Emit("end", new { round = s.Round, rounds = s.Rounds, complete = s.Round >= s.Rounds, activity = s.Activity, rewards = s.RewardsTaken, rewardsTotal = s.RewardsTotal, instruments = s.Instruments.Select(c => c.Id), monsters = Monsters(s) });
        run = null;
        last = null;
        logged = null;
        decisionKey = "";
        stageKey = "";
    }

    // Screens the advisor has no rule for (the final battle, rewards): snapshot the board whenever it changes so the log still shows them
    void EmitStage(VoraxState state)
    {
        if (state.Phase != VoraxPhase.Other) return;
        var key = $"{state.State}|{state.Round}|{state.Activity}|{state.SelectedCard}|{string.Join(',', state.Offered.Select(c => c.Id))}";
        if (key == stageKey) return;
        stageKey = key;
        Emit("stage", new { state = state.State, round = state.Round, activity = state.Activity, rewards = state.RewardsTaken, selected = state.SelectedCard, offered = state.Offered.Select(Card), instruments = state.Instruments.Select(c => c.Id), monsters = Monsters(state) });
    }

    void Decide(VoraxState state, Advice? advice)
    {
        var key = $"{state.Phase}|{state.Round}|{state.SelectedCard}|{state.Rerolls}|{string.Join(',', state.Offered.Select(c => c.Id))}";
        if (key != decisionKey)
        {
            decisionKey = key;
            clock.Restart();
        }

        if (advice is null || ReferenceEquals(advice, logged)) return;
        logged = advice;
        Emit("decision", new
        {
            round = state.Round,
            stage = advice.Stage.ToString(),
            state = state.State,
            activity = state.Activity,
            next = state.NextRewardActivity,
            rewards = state.RewardsTaken,
            rerolls = state.Rerolls,
            selected = state.SelectedCard,
            offered = state.Offered.Select(Card),
            instruments = state.Instruments.Select(c => c.Id),
            monsters = Monsters(state),
            source = advice.Source.ToString(),
            strategy = advice.StrategyName,
            pick = advice.Pick.Indexes,
            reroll = advice.Pick.IsReroll,
            reason = advice.Pick.Reason,
            values = advice.Values?.Select(v => Math.Round(v)),
            rerollValue = advice.RerollValue is { } r ? Math.Round(r) : (double?)null,
            error = advice.Error,
            ms = clock.ElapsedMilliseconds,
        });
    }

    void Track(VoraxState state)
    {
        if (last is not { } before) return;
        var gained = state.Instruments.Select(c => c.Id).Except(before.Instruments.Select(c => c.Id)).ToArray();
        foreach (var id in gained)
        {
            var index = Array.FindIndex(before.Offered, c => c.Id == id);
            Emit("instrument", new { round = state.Round, id, index, followed = Followed(Stage.Instrument, a => a.Pick.Indexes is [var i] && i == index) });
        }

        if (state.Round > before.Round)
        {
            Emit("round", new { from = before.Round, to = state.Round, activity = state.Activity, gained = state.Activity - before.Activity, rewards = state.RewardsTaken, rerolls = state.Rerolls, instruments = state.Instruments.Select(c => c.Id), monsters = Monsters(state) });
            return;
        }

        if (state.Rerolls < before.Rerolls)
            Emit("reroll", new { round = state.Round, left = state.Rerolls, followed = Followed(Stage.Potion, a => a.Pick.IsReroll) });

        if (state.SelectedCard >= 0 && state.SelectedCard != before.SelectedCard)
        {
            var index = Array.FindIndex(state.Offered, c => c.Slot == state.SelectedCard);
            if (index >= 0)
                Emit("choice", new { round = state.Round, index, card = Card(state.Offered[index]), followed = Followed(Stage.Potion, a => !a.Pick.IsReroll && a.Pick.Indexes is [var i] && i == index) });
        }

        if (state.SelectedMonsters.Length > 0 && !state.SelectedMonsters.SequenceEqual(before.SelectedMonsters))
            Emit("targets", new { round = state.Round, slots = state.SelectedMonsters, followed = Followed(Stage.Target, a => a.Pick.Indexes is { } picked && picked.Order().SequenceEqual(state.SelectedMonsters.Order())) });
    }

    // The advice last shown must be for this kind of decision, otherwise nothing can be said about it
    bool? Followed(Stage stage, Func<Advice, bool> matches) => logged is { } a && a.Stage == stage && a.Pick.Indexes is not null ? matches(a) : null;

    static object Card(VoraxCard card) => new { slot = card.Slot, id = card.Id, kind = card.Kind.ToString(), rarity = card.Rarity, name = card.Name };

    static object Monsters(VoraxState state) => state.Monsters.Select(m => new { slot = m.Slot, id = m.Id, species = m.Species, rarity = m.Rarity, count = m.Count, activity = m.Activity });

    void Emit(string name, object payload) => log($"run={run} {name} {JsonSerializer.Serialize(payload)}");
}
