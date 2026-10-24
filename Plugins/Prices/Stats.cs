using System;
using System.Collections.Generic;
using System.Linq;
using EmberHUD;

namespace Prices;

public sealed record Economy(bool TradeTax, bool MapCosts, bool WallClock);

public sealed record Summary(float NetWorth, float FePerHour, float FePerMap, int Runs, TimeSpan AverageRun, TimeSpan TotalTime);

public static class Stats
{
    const float TradeTaxKept = 0.875f;

    public static Summary Summarise(IReadOnlyList<Run> runs, Economy economy, DateTimeOffset now)
    {
        if (runs.Count == 0) return new Summary(0, 0, 0, 0, TimeSpan.Zero, TimeSpan.Zero);
        var net = runs.Sum(run => Value(run, economy));
        var active = TimeSpan.FromTicks(runs.Sum(run => (run.End - run.Start).Ticks));
        var total = economy.WallClock ? TimeSpan.FromTicks(Math.Max(0, (now - runs.Min(run => run.Start)).Ticks)) : active;
        return new Summary(net, total > TimeSpan.Zero ? (float)(net / total.TotalHours) : 0, net / runs.Count, runs.Count, active / runs.Count, total);
    }

    public static float RunValue(Run run, Economy economy, IReadOnlyDictionary<int, Price> prices) => Value(run, economy);

    static float Value(Run run, Economy economy)
    {
        var value = run.Loot.Sum(line => economy.TradeTax && line.BaseId != Valuation.FeBaseId ? line.Value * TradeTaxKept : line.Value);
        return economy.MapCosts ? value - run.Costs.Sum(line => Math.Abs(line.Value)) : value;
    }
}
