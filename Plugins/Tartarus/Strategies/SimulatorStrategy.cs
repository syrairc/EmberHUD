namespace Tartarus;

public sealed class SimulatorStrategy : Strategy
{
    public override string Name => "Simulator";

    public override string Description => "Always follows the highest playout value";

    public override StrategyPick? Choose(StrategyContext c) => c.Ranked is { Length: > 0 } ranked ? new StrategyPick(ranked[0].Move, "playout leader") : null;
}
