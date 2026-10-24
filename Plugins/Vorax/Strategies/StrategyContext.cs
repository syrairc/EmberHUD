using System;
using EmberHUD;
using Vorax.Sim;

namespace Vorax;

public sealed class StrategyContext
{
    readonly Func<Func<SimState, double>?, bool, Pick> _simulate;

    internal StrategyContext(VoraxState state, Stage stage, VoraxCard? chosen, int maxTargets, Func<Func<SimState, double>?, bool, Pick> simulate, SimState? sim = null, int[]? offeredSimIds = null, int chosenIndex = -1)
    {
        Sim = sim;
        OfferedSimIds = offeredSimIds;
        ChosenIndex = chosenIndex;
        State = state;
        Stage = stage;
        Chosen = chosen;
        MaxTargets = maxTargets;
        _simulate = simulate;
    }

    public VoraxState State { get; }

    internal SimState? Sim { get; }

    // sim id per offered card (potion, kit or instrument), null when a card has no sim mapping
    internal int[]? OfferedSimIds { get; }

    internal int ChosenIndex { get; }

    public Stage Stage { get; }

    public VoraxCard[] Choices => State.Offered;

    public VoraxCard? Chosen { get; }

    public int MaxTargets { get; }

    public Pick Simulate(Func<SimState, double>? score = null, bool allowReroll = true) => _simulate(score, allowReroll);
}
