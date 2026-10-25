using Vorax.Sim;

namespace Vorax;

// The Notorious search dealing hands from TLI Overlay Plus's crowd-counted offers: kits rarer, supremes at 0.82%
public sealed class TliOverlayPlusStrategy() : NotoriousStrategy(DrawModel.OverlayPlus)
{
    public override string Name => "TLI Overlay Plus";

    public override string Description => "Notorious search on TLI Overlay Plus's offer odds, counted from 20,866 boards: kits are rarer than in the fork's fit.";
}
