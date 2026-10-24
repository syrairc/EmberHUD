using System.Linq;

namespace Tartarus;

public sealed class Blahdot3hStrategy : Strategy
{
    public override string Name => StrategyEngine.DefaultMode;

    public override string Description => "The ported blahdot3h pick rule, kept unless the playouts clearly beat it";

    public override StrategyPick? Choose(StrategyContext c)
    {
        if (c.Ranked is not { Length: > 0 } ranked) return c.Quick is { } quick ? new StrategyPick(quick, "quick pick") : null;
        if (ranked.FirstOrDefault(e => e.Quick) is not { } mine) return null;
        return SimAdvisor.Close(ranked[0], mine) ? new StrategyPick(mine.Move, "kept") : new StrategyPick(ranked[0].Move, "playouts lead");
    }
}
