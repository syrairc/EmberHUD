using System;
using System.Linq;
using EmberHUD;
using EmberHUD.Ui;
using Hexa.NET.ImGui;

namespace Tartarus;

static class SettingsUI
{
    public static void DrawPanel(Settings s)
    {
        Setting.Checkbox("Show advice", ref s.ShowPanel, "Predicted score, sim progress and strategy in the middle of the prison, only while the prison itself is on screen");
        Setting.Checkbox("Highlight cards", ref s.HighlightCards, "Glows the best card on the game's own decision cards and instrument pick instead of a row in the panel");
    }

    public static void DrawAdvice(Settings s)
    {
        Setting.Slider("Playouts", ref s.Playouts, 20, 2000, "Simulated runs per option, more is steadier but slower to appear");
        Setting.Checkbox("Only show the best", ref s.OnlyBest, "Glows one option, the best, instead of every option within the margin of error of it");
        DrawMode(s);
        Setting.Checkbox("Log decisions", ref s.LogDecisions, "Logs what the plugin sees and advises to the Ember log (Info, Tartarus source), one line per change");
        Setting.Checkbox("Record run data", ref s.LogRunData, "Writes every Tartarus run to Config\\Tartarus\\runs, one file per run, so you can share it for odds research - board, choices, tools and events only, nothing about you or your character");
    }

    static void DrawMode(Settings s)
    {
        var strategies = Registry.All();
        var labels = strategies.Select(strategy => strategy.Name).ToArray();
        var index = Math.Max(0, Array.IndexOf(labels, Array.IndexOf(labels, s.Mode) < 0 ? StrategyEngine.DefaultMode : s.Mode));
        ImGui.TextUnformatted("Mode");
        ImGui.SameLine(160);
        if (Combo.Option(ref index, labels, "tartarusmode", 260)) s.Mode = labels[index];
        if (strategies[index].Description != "") ImGui.TextDisabled(strategies[index].Description);
    }

    public static void DrawRules(Settings s)
    {
        Setting.Slider("Chromatic cap", ref s.ChromaticCap, 1, 30, "Chromatic inmates allowed per run, past it nothing reaches Chromatic");
        Setting.Checkbox("Eater keeps its beast", ref s.EaterKeepsBeast, "Off: an inmate that eats and rises comes back as a random beast. On: it stays the same beast");
        Setting.Slider("Second meal from tier", ref s.SecondMealFromTier, 0, 5, "Mammoths and lions can eat two beasts down from this tier up");
        Setting.Checkbox("Replace is Execute", ref s.ReplaceIsExecute, "Calls the once-a-day swap Execute on the card, it acts the same either way");
    }
}
