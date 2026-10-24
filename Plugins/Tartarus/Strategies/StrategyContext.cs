using System.Collections.Generic;
using Tartarus.Sim;

namespace Tartarus;

public sealed class StrategyContext(Run run, Ranked[]? ranked, Move? quick)
{
    public Run Run { get; } = run;

    public IReadOnlyList<Move> Legal { get; } = run.Legal();

    public Ranked[]? Ranked { get; } = ranked;

    public Move? Quick { get; } = quick;
}
