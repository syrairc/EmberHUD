using Tartarus.Sim;

namespace Tartarus;

public sealed class TliOverlayPlusStrategy : Strategy
{
    public override string Name => "TLI Overlay Plus";

    public override string Description => "Follows the playout leader under TLI Overlay Plus's rules: its tool values (2026-10-24) and the Coin of Fate at 25%";

    internal override Chance Chance => new(Tools: ToolSetsOverlayPlus.Table, CoinPercent: 25);

    public override StrategyPick? Choose(StrategyContext c) => c.Ranked is { Length: > 0 } ranked ? new StrategyPick(ranked[0].Move, "playout leader") : null;
}
