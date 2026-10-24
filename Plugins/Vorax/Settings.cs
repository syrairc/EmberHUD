using System;
using System.Drawing;
using System.Linq;
using EmberHUD;
using EmberHUD.Ui;
using Hexa.NET.ImGui;

namespace Vorax;

public sealed class Settings : PluginSettings
{
    public const string SimulatorMode = "Simulator";
    const int MinRollouts = 200;

    public bool Enabled = true;
    public bool ShowPanel = true;
    public bool ShowHighlights = true;
    public bool Debug;
    public bool ShowBar = true;
    public bool Lock = true;
    public Hotkey MoveKey = new(Key.Alt);
    public int PanelX = 2750;
    public int PanelY = 200;
    public int BarX = 2750;
    public int BarY = 130;
    public int BarWidth = 460;
    public TextStyle BarText = TextStyle.Hud();
    public Color BarBackground = Color.FromArgb(0xC8, 0x12, 0x12, 0x16);
    public Color BarFill = Color.FromArgb(0xFF, 0xE0, 0xA0, 0x30);
    public Color BarTrack = Color.FromArgb(0x60, 0xFF, 0xFF, 0xFF);
    public string Mode = SimulatorMode;
    public int RerollMargin = 5;
    public int Rollouts = MinRollouts;
    public int BudgetMilliseconds = 1500;
    public Color StrategyColour = Color.FromArgb(0xFF, 0x4C, 0xD9, 0x64);
    public Color SimulatorColour = Color.FromArgb(0xFF, 0x5B, 0xB8, 0xFF);

    internal int ClampedRollouts => Math.Max(MinRollouts, Rollouts);
    internal int ClampedBudget => Math.Max(100, BudgetMilliseconds);
    internal int ClampedMargin => Math.Clamp(RerollMargin, 0, 100);

    public override void Draw()
    {
        Setting.Checkbox("Enabled", ref Enabled);
        Setting.Checkbox("Show highlights", ref ShowHighlights, "Frames the card, targets and buttons to click");
        Setting.Checkbox("Show bar", ref ShowBar, "A one line strip: the current pick on the left, the simulator's estimate of the final Activity on the right, a bar that fills while the advice is being worked out");
        Setting.Checkbox("Show panel", ref ShowPanel, "The advice, its reason and the simulated value of every option");
        Setting.Checkbox("Debug rows", ref Debug, "Every value the reader sees, for checking against the game - also logs one simulator check per round");
        Setting.Checkbox("Lock", ref Lock, "A locked panel ignores the mouse - hold the move key to drag it anyway");
        Setting.Hotkey("Move key", ref MoveKey, "Hold to move the panel while locked");
        DrawMode();
        Setting.Slider("Reroll margin %", ref RerollMargin, 0, 25, "A reroll has to beat the best card by this much");
        Setting.Slider("Rollouts", ref Rollouts, MinRollouts, 2000, "Simulated games per option - below 200 the reroll margin is inside the noise");
        Rollouts = ClampedRollouts;
        Setting.Slider("Budget ms", ref BudgetMilliseconds, 200, 10000, "Time cap for one advice");
        Setting.Slider("Bar width", ref BarWidth, 200, 1200, "Also drag the right edge of the bar");
        Setting.Style("Bar text", BarText, "Size, colour and shadow of the text on the bar");
        Setting.Color("Bar background", ref BarBackground);
        Setting.Color("Bar fill", ref BarFill);
        Setting.Color("Bar track", ref BarTrack);
        Setting.Color("Strategy colour", ref StrategyColour);
        Setting.Color("Simulator colour", ref SimulatorColour);
    }

    void DrawMode()
    {
        var strategies = Registry.All();
        string[] labels = [SimulatorMode, .. strategies.Select(s => s.Name)];
        var index = Math.Max(0, Array.IndexOf(labels, Mode));
        ImGui.TextUnformatted("Mode");
        ImGui.SameLine(160);
        if (Combo.Option(ref index, labels, "voraxmode", 260)) Mode = labels[index];
        var description = index == 0 ? "Picks by simulating the rest of the run" : strategies[index - 1].Description;
        if (description != "") ImGui.TextDisabled(description);
    }
}
