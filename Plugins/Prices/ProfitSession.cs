using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Prices;

public sealed record ProfitView(TimeSpan Time, int Runs, float? ThisMap, float? LastMap, float? PerMap, float? PerHour, float? Profit, bool Unpriced, float Wealth, float Fe, bool ThisMapUnpriced = false);

public sealed class ProfitSession(Func<DateTimeOffset> now)
{
    static readonly Economy SessionEconomy = new(false, true, false);

    readonly List<Run> runs = [];
    DateTimeOffset start = now();

    public void Restart()
    {
        lock (runs)
        {
            runs.Clear();
            start = now();
        }
    }

    public void Add(Run run)
    {
        lock (runs)
            if (run.Start >= start) runs.Add(run);
    }

    public ProfitView? View(Run? open, Snapshot? last, IReadOnlyList<StashTab> tabs, float? bagWealth = null)
    {
        if (last is null) return null;
        Run[] closed;
        DateTimeOffset since;
        lock (runs)
        {
            closed = [.. runs];
            since = start;
        }
        var summary = Stats.Summarise(closed, SessionEconomy, now());
        var any = closed.Length > 0;
        float? thisMap = open is not null && open.Start >= since ? open.Loot.Sum(line => line.Value) : null;
        var wealth = (bagWealth ?? last.Total) + tabs.Sum(tab => tab.Lines.Sum(line => line.Value));
        return new ProfitView(
            summary.TotalTime,
            summary.Runs,
            thisMap,
            any ? Stats.RunValue(closed[^1], SessionEconomy, new Dictionary<int, Price>()) : null,
            any ? summary.FePerMap : null,
            any ? summary.FePerHour : null,
            any ? summary.NetWorth : null,
            closed.Any(run => run.Unpriced),
            wealth,
            last.Fe,
            open is not null && open.Start >= since && open.Unpriced);
    }
}

public static class ProfitText
{
    public static string Signed(float? value, bool unpriced)
    {
        if (value is not { } number) return "-";
        var star = unpriced ? "*" : "";
        var size = Math.Abs(number);
        if (Tags.Format(size) is not { } text) return "0" + star;
        var tenths = size.ToString("0.0", CultureInfo.InvariantCulture);
        if (size < 100 && tenths != "100.0") text = size >= 10 ? tenths.Replace(".0", "") : tenths;
        return (number < 0 ? "-" : "+") + text + star;
    }

    public static string Plain(float value) => Tags.Format(value) ?? "0";
}
