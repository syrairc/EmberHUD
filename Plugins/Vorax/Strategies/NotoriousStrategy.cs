using System.Linq;
using Vorax.Sim;

namespace Vorax;

// The method of the community Notorious solver: shortlist by the expected board after the round, then play the shortlist out on shared seeded futures with a learned board value, ranked by Activity capped at 950,000
public sealed class NotoriousStrategy : Strategy
{
    readonly Notorious _search = new();

    public override string Name => "Notorious v1";

    public override string Description => "Plays each shortlisted option out to the last round on shared futures, scored by a learned value, capped at 950k.";

    public override Pick? PickInstrument(StrategyContext c)
    {
        if (c.Sim is null || c.OfferedSimIds is not { Length: > 0 } ids) return null;
        var result = _search.RankInstruments(c.Sim, ids);
        return Pick.One(result.Best.Option!.Value.Card, Reason(result.Best));
    }

    public override Pick? PickPotion(StrategyContext c)
    {
        if (c.Sim is null || c.OfferedSimIds is not { Length: > 0 } ids) return null;
        if (_search.RankPotions(c.Sim, ids) is not { } result) return null;
        return result.Best.Option is { } option ? Pick.One(option.Card, Reason(result.Best)) : Pick.Reroll($"reroll {Reason(result.Best)}");
    }

    public override Pick? PickTarget(StrategyContext c)
    {
        if (c.Sim is null || c.OfferedSimIds is not { Length: > 0 } ids || c.ChosenIndex < 0) return null;
        if (_search.RankPotions(c.Sim, ids, false, c.ChosenIndex) is not { } result || result.Best.Option is not { } option) return null;
        return new(option.Targets.ToArray(), Reason(result.Best));
    }

    static string Reason(NotoriousEntry e) => $"{e.Goal:0} average, {e.Reached:P0} reach 950k";
}
