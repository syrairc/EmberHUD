using System;
using EmberHUD;
using Tartarus.Sim;

namespace Tartarus;

public sealed class Settings : PluginSettings
{
    public bool ShowPanel = true;
    public bool HighlightCards = true;
    public int Playouts = 300;
    public bool OnlyBest;
    public string Mode = StrategyEngine.DefaultMode;
    public bool LogDecisions = true;
    public bool LogRunData;
    public int ChromaticCap = 10;
    public bool EaterKeepsBeast;
    public int SecondMealFromTier = 0;
    public bool ReplaceIsExecute;

    internal int ClampedPlayouts => Math.Clamp(Playouts, 20, 2000);

    internal Rules ToRules() => new() { ChromaticCap = Math.Clamp(ChromaticCap, 1, 30), EaterKeepsBeast = EaterKeepsBeast, SecondMealFromTier = SecondMealFromTier, ReplaceIsExecute = ReplaceIsExecute };

    public override void Draw() { }
}
