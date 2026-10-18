using System;
using System.Collections.Generic;
using System.Linq;
using EmberHUD;

namespace Tartarus;

public sealed class RunRecorder
{
    const double EndAfterSeconds = 3;

    readonly List<RunCell> cells = [];
    readonly Dictionary<int, int> current = [];
    bool running;
    DateTime start;
    DateTime lastSeen;
    int? before;
    int? lastCount;
    double? nullSince;

    public RunEntry? Update(TartarusState? state, int? nameplates, DateTime now, double nowSeconds)
    {
        if (state is null)
        {
            if (!running) return null;
            nullSince ??= nowSeconds;
            if (nowSeconds - nullSince < EndAfterSeconds) return null;
            running = false;
            return new(start, lastSeen, before, nameplates ?? lastCount, cells.OrderBy(cell => cell.Index).ToArray());
        }

        if (!running)
        {
            running = true;
            start = now;
            before = nameplates;
            lastCount = null;
            cells.Clear();
            current.Clear();
        }

        nullSince = null;
        lastSeen = now;
        lastCount = nameplates ?? lastCount;
        foreach (var cell in state.Cells)
        {
            if (!cell.Open) current.Remove(cell.Index);
            if (!cell.Open || cell.Beast == TartarusBeast.Unknown || cell.Tier < 0) continue;
            if (current.TryGetValue(cell.Index, out var at) && cells[at].Beast == (int)cell.Beast)
                cells[at] = cells[at] with { Tier = Math.Max(cells[at].Tier, cell.Tier) };
            else
            {
                current[cell.Index] = cells.Count;
                cells.Add(new(cell.Index, (int)cell.Beast, cell.Tier));
            }
        }
        return null;
    }
}
