using System;
using EmberHUD;
using EmberHUD.Ui;

namespace Tarot;

public enum Objective { Rarity, Good }

public sealed class Settings : PluginSettings
{
    public bool ShowPanel = true;
    public bool Lock = true;
    public Hotkey MoveKey = new(Key.Alt);
    public int PanelX = 40;
    public int PanelY = 430;
    public int AheadTiles = 24;
    public bool HighlightCard = true;
    public bool UseSolver = true;
    public Objective Objective = Objective.Rarity;
    public int Rollouts = 150;
    public bool DeathLosesLoot = true;
    public bool ShowStats = true;

    public SolverOptions ToOptions() => SolverOptions.Default with
    {
        Weights = (Objective == Objective.Good ? Weights.Good : Weights.Rarity),
        Rollouts = Math.Clamp(Rollouts, 20, 1000),
        DeathLosesLoot = DeathLosesLoot,
    };

    public override void Draw()
    {
        Setting.Checkbox("Show panel", ref ShowPanel, "The board card next to the screen");
        Setting.Checkbox("Lock", ref Lock, "A locked panel ignores the mouse - hold the move key to drag it anyway");
        Setting.Hotkey("Move key", ref MoveKey, "Hold to move the panel while locked");
        Setting.Checkbox("Highlight card", ref HighlightCard, "Points an arrow at the best card in the game's own hand: green when it lands somewhere good, gold when every card is a trap or a miss");
        Setting.Slider("Tiles ahead", ref AheadTiles, 4, 80, "How many tiles the grid shows, counted from the start of the current row of 4");
        Setting.Checkbox("Use solver", ref UseSolver, "Plays the rest of the run out thousands of times in the background and ranks every card by the result. Off keeps the quick greedy advice");
        Setting.Choice("Goal", ref Objective, "Rarity values chests by how rare they are, Good only chases the good ones");
        Setting.Slider("Rollouts", ref Rollouts, 60, 400, "Test plays per card. More is steadier but slower to show - 60 is quick, 150 normal, 400 thorough");
        Setting.Checkbox("Death loses loot", ref DeathLosesLoot, "Counts a run that ends dead as worth nothing");
        Setting.Checkbox("Show stats", ref ShowStats, "Shows the expected score and the chance to die next to each card");
    }
}
