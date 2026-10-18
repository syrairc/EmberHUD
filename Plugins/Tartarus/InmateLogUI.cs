using System;
using System.Linq;
using EmberHUD.Ui;
using Hexa.NET.ImGui;

namespace Tartarus;

static class InmateLogUI
{
    const long ConfirmMs = 5000;

    static readonly string[] Steps = ["Normal", "Magic", "Rare", "Legendary"];

    static long _armedAt = long.MinValue / 2;

    public static void Draw(InmateStore store, int? nameplates, Action clear)
    {
        ImGui.TextUnformatted($"Nameplates: {(nameplates?.ToString() ?? "unknown")} / 48");
        Grid(store);
        ImGui.TextUnformatted($"Unclassified: {store.Unclassified}   Unsure: {store.Unsure}");
        ImGui.SeparatorText("Last kills");
        if (Tables.Begin("kills", [("Time", 70), ("Area", 0), ("Inmate", 0), ("Outcome", 130)]))
        {
            var shown = store.Kills.AsEnumerable().Reverse().Take(20).ToArray();
            for (var i = 0; i < shown.Length; i++)
            {
                var kill = shown[i];
                using (Tables.Row(i))
                {
                    Cell(kill.When.ToString("HH:mm:ss"));
                    Cell(kill.Area ?? "");
                    Cell(kill.Species is { } species && Enum.IsDefined(species) && kill.Step is >= 1 and <= 4 ? $"{Inmates.SpeciesName(species)} {Steps[kill.Step - 1]}" : kill.CfgId.ToString());
                    Cell(kill.Source is null ? kill.Outcome : $"{kill.Outcome} ({kill.Source})");
                }
            }
            Tables.End();
        }

        ImGui.SeparatorText("Last runs");
        if (Tables.Begin("runs", [("Start", 130), ("Cells", 50), ("Nameplates", 0)]))
        {
            var shown = store.Runs.AsEnumerable().Reverse().Take(10).ToArray();
            for (var i = 0; i < shown.Length; i++)
            {
                var run = shown[i];
                using (Tables.Row(i))
                {
                    Cell(run.Start.ToString("MM-dd HH:mm"));
                    Cell(run.Cells.Length.ToString());
                    Cell($"{run.NameplatesBefore?.ToString() ?? "?"} -> {run.NameplatesAfter?.ToString() ?? "?"}");
                }
            }
            Tables.End();
        }

        ImGui.Separator();
        var armed = Environment.TickCount64 - _armedAt < ConfirmMs;
        if (ImGui.Button(armed ? "Click again to clear all data" : "Clear"))
        {
            if (armed)
            {
                clear();
                _armedAt = 0;
            }
            else _armedAt = Environment.TickCount64;
        }
    }

    static void Grid(InmateStore store)
    {
        if (!Tables.Begin("grid", [("", 0), .. Steps.Select(step => (step, 0f)), ("Total", 0)])) return;
        var columns = new int[4];
        for (var s = 0; s < 4; s++)
        {
            using (Tables.Row(s))
            {
                Cell(Inmates.SpeciesName((InmateSpecies)s));
                var total = 0;
                for (var step = 0; step < 4; step++)
                {
                    Cell(store.Grid[s, step].ToString());
                    total += store.Grid[s, step];
                    columns[step] += store.Grid[s, step];
                }
                Cell(total.ToString());
            }
        }

        using (Tables.Row(4))
        {
            Cell("Total");
            foreach (var count in columns) Cell(count.ToString());
            Cell(columns.Sum().ToString());
        }
        Tables.End();
    }

    static void Cell(string text)
    {
        ImGui.TableNextColumn();
        ImGui.TextUnformatted(text);
    }
}
