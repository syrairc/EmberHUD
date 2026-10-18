using System;
using System.Linq;
using System.Numerics;
using EmberHUD.Ui;
using Hexa.NET.ImGui;

namespace Tartarus;

static class MinigameLogUI
{
    const long ConfirmMs = 5000;

    static readonly string[] Tiers = ["Normal", "Magic", "Rare", "Legendary", "Ultimate", "Chromatic"];
    static readonly string[] Causes = ["start", "door", "replace", "consume", "refill", "other"];
    static readonly Vector4 Missing = new(0.88f, 0.27f, 0.24f, 1);

    static long _armedAt = long.MinValue / 2;

    public static void Draw(MinigameStore store, Action clear)
    {
        ImGui.SeparatorText("Pulls by inmate (no consumes)");
        Grid(store);
        ImGui.SeparatorText("Causes");
        if (Tables.Begin("causes", [("Cause", 0), ("Entries", 0)]))
        {
            for (var i = 0; i < Causes.Length; i++)
            {
                using (Tables.Row(i))
                {
                    Cell(Causes[i]);
                    Cell(store.Entries.Count(entry => entry.Cause == Causes[i]).ToString());
                }
            }
            Tables.End();
        }

        ImGui.SeparatorText("Not collected");
        if (Tables.Begin("missing", [("Time", 70), ("Run", 40), ("Day", 40), ("Cell", 40), ("Inmate", 0), ("Cause", 70), ("Collected", 70)]))
        {
            var shown = store.Entries.Where(entry => entry.Collected == 0).Reverse().Take(20).ToArray();
            for (var i = 0; i < shown.Length; i++)
            {
                var entry = shown[i];
                using (Tables.Row(i))
                {
                    Cell(entry.When.ToString("HH:mm:ss"));
                    Cell(entry.Run.ToString());
                    Cell(entry.Day.ToString());
                    Cell((entry.Cell + 1).ToString());
                    Cell(Inmate(entry));
                    Cell(entry.Cause);
                    Cell(entry.Collected.ToString());
                }
            }
            Tables.End();
        }

        ImGui.SeparatorText("Events");
        if (Tables.Begin("events", [("Day", 40), ("Cell", 40), ("Event", 0), ("Inmate", 0)]))
        {
            var landed = store.Events.AsEnumerable().Reverse().Take(20).ToArray();
            for (var i = 0; i < landed.Length; i++)
            {
                var entry = landed[i];
                using (Tables.Row(i))
                {
                    Cell(entry.Day.ToString());
                    Cell((entry.Cell + 1).ToString());
                    Cell(entry.Name);
                    Cell(entry.Species is >= 0 and < 4 && entry.Tier is >= 0 and < 6 ? $"{Inmates.SpeciesName((InmateSpecies)entry.Species)} {Tiers[entry.Tier]}" : "-");
                }
            }
            Tables.End();
        }

        ImGui.Separator();
        var armed = Environment.TickCount64 - _armedAt < ConfirmMs;
        if (ImGui.Button(armed ? "Click again to clear the minigame log" : "Clear##minigame"))
        {
            if (armed)
            {
                clear();
                _armedAt = 0;
            }
            else _armedAt = Environment.TickCount64;
        }
    }

    static string Inmate(PullEntry entry) =>
        $"{(entry.Species is >= 0 and < 4 ? Inmates.SpeciesName((InmateSpecies)entry.Species) : "?")} {(entry.Tier is >= 0 and < 6 ? Tiers[entry.Tier] : entry.Tier.ToString())}";

    static void Grid(MinigameStore store)
    {
        if (!Tables.Begin("pulls", [("", 0), .. Tiers.Take(6).Select(tier => (tier, 0f))])) return;
        var pulls = store.Entries.Where(entry => entry.Cause != "consume" && entry.Species is >= 0 and < 4 && entry.Tier is >= 0 and < 6).ToArray();
        for (var s = 0; s < 4; s++)
        {
            using (Tables.Row(s))
            {
                Cell(Inmates.SpeciesName((InmateSpecies)s));
                for (var tier = 0; tier < 6; tier++)
                {
                    var here = pulls.Where(entry => entry.Species == s && entry.Tier == tier).ToArray();
                    ImGui.TableNextColumn();
                    if (here.Any(entry => entry.Collected == 0)) ImGui.TextColored(Missing, here.Length.ToString());
                    else ImGui.TextUnformatted(here.Length.ToString());
                }
            }
        }
        Tables.End();
    }

    static void Cell(string text)
    {
        ImGui.TableNextColumn();
        ImGui.TextUnformatted(text);
    }
}
