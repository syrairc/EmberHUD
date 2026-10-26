using EmberHUD;
using EmberHUD.Ui;

namespace MouseLab;

public enum LabPreset { VerySlow, Slow, Normal, Fast, VeryFast }

public sealed class Settings : PluginSettings
{
    public LabPreset Preset = LabPreset.Normal;
    public MenuOption MenuOption = MenuOption.Lock;
    public float MaxBacktrack = 3;
    public float Overshoot = 3;
    public float FinalError = 6;
    public int LongestStallMs = 40;
    public bool ShowTrail = true;
    public int TrailSeconds = 10;
    public bool Lock = true;
    public Hotkey MoveKey = new(Key.Alt);
    public int PanelX = 40;
    public int PanelY = 300;

    public Thresholds ToThresholds() => new(MaxBacktrack, Overshoot, FinalError, LongestStallMs);

    public override void Draw()
    {
        Setting.Choice("Preset", ref Preset, "The mouse speed preset the Run hotkey tests - Run all always goes through the five named ones");
        Setting.Choice("Menu option", ref MenuOption, "The item menu button the Click and menu scenario presses - Lock is pressed twice so the lock ends where it started, None leaves the scenario out");
        Controls.Section("Pass marks");
        Setting.Slider("Max backtrack", ref MaxBacktrack, 0, 20, "Most the cursor may move backwards along the way, in pixels");
        Setting.Slider("Overshoot", ref Overshoot, 0, 20, "Most the cursor may run past the target before coming back, in pixels");
        Setting.Slider("Final error", ref FinalError, 0, 30, "Furthest from the target centre the cursor may stop, in pixels");
        Setting.Slider("Longest stall", ref LongestStallMs, 0, 200, "Longest time the cursor may sit still mid move, in milliseconds");
        Controls.Section("Panel");
        Setting.Checkbox("Show trail", ref ShowTrail, "Draws where the cursor went after a run");
        Setting.Slider("Trail seconds", ref TrailSeconds, 1, 60, "How long the trail stays up after a run");
        Setting.Checkbox("Lock", ref Lock, "A locked panel ignores the mouse - hold the move key to drag it anyway");
        Setting.Hotkey("Move key", ref MoveKey, "Hold to move the panel while locked");
        Controls.Section("Hotkeys");
        Controls.PluginHotkeys();
    }
}
