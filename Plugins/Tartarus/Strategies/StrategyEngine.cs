using System;
using System.Collections.Generic;
using System.Linq;
using EmberHUD;
using Tartarus.Sim;

namespace Tartarus;

public sealed record Resolved(Move? Move, string Source, string? Error);

public static class StrategyEngine
{
    public const string DefaultMode = "Blahdot3h";

    public static Resolved Resolve(string? mode, Run? run, Ranked[]? ranked, Move? quick, IReadOnlyList<Strategy>? strategies = null)
    {
        if (run is null) return new Resolved(null, "", null);
        strategies ??= Registry.All();
        var strategy = strategies.FirstOrDefault(s => s.Name == mode) ?? strategies.FirstOrDefault(s => s.Name == DefaultMode);
        string? error = null;
        var label = "Simulator";
        if (strategy is not null)
        {
            var reason = "had no pick";
            try
            {
                var context = new StrategyContext(run, ranked, quick);
                var pick = strategy.Choose(context);
                if (pick is { } p)
                {
                    if (context.Legal.Contains(p.Move)) return new Resolved(p.Move, strategy.Name, null);
                    reason = $"{strategy.Name}: picked an illegal move";
                    error = reason;
                }
            }
            catch (Exception e)
            {
                error = e.Message;
                reason = $"{strategy.Name}: {e.Message}";
                Log.Error(e, "tartarus strategy");
            }

            label = reason == "had no pick" ? $"Simulator ({strategy.Name} had no pick)" : $"Simulator ({reason})";
        }

        return ranked is { Length: > 0 } ? new Resolved(ranked[0].Move, label, error) : new Resolved(null, "", error);
    }
}
