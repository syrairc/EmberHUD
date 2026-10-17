using System.Drawing;
using EmberHUD;
using EmberHUD.Ui;

namespace DummyDpsPlugin;

public sealed class GeneralTab
{
    internal const int MinWidth = 160;
    internal const int MaxWidth = 800;

    public bool Show = true;
    public bool Lock = true;
    public Hotkey MoveKey = new(Key.Alt);
    public bool HideBehindPanels = true;
    public bool KeepAfterStop = true;
    public bool Exact;
    public int X = 1600;
    public int Y = 600;
    public int Width = 280;
}

public sealed class CombatTab
{
    public bool Show = true;
    public bool HideInTown = true;
    public bool CountOverkill;
    public bool Exact;
    public int Window = 5;
    public int X = 1600;
    public int Y = 780;
    public int Width = 280;
}

public sealed class LookTab
{
    public TextStyle Title = new() { Face = FontFace.Bold, Scale = 1.15f, ShadowEnabled = true };
    public TextStyle Labels = TextStyle.Hud();
    public TextStyle Values = new() { Face = FontFace.Bold, Color = GameColors.Gold, ShadowEnabled = true };
    public TextStyle Note = new() { Color = Color.FromArgb(0xFF, 0xA0, 0xA0, 0xA0), ShadowEnabled = true };
    public Color Background = Color.FromArgb(0xB0, 0, 0, 0);
}

public sealed class Settings : PluginSettings
{
    public GeneralTab General = new();
    public CombatTab Combat = new();
    public LookTab Look = new();

    public override void Draw() => Layout.TabBar("dummydps",
        ("General", DrawGeneral),
        ("Combat", DrawCombat),
        ("Look", DrawLook),
        ("Hotkeys", Controls.PluginHotkeys));

    void DrawGeneral()
    {
        Setting.Checkbox("Show", ref General.Show);
        Setting.Checkbox("Lock", ref General.Lock, "A locked panel ignores the mouse - hold the move key to drag or resize it anyway");
        Setting.Hotkey("Move key", ref General.MoveKey, "Hold to move or resize a locked panel");
        Setting.Checkbox("Hide behind full-screen panels", ref General.HideBehindPanels, "Nothing draws while the game has a full-screen panel open, e.g. the big map");
        Setting.Checkbox("Keep after stopping", ref General.KeepAfterStop, "Keep showing the last result after the dummy statistic is stopped, until you leave the area");
        Setting.Checkbox("Exact numbers", ref General.Exact, "Full digits instead of K / M / B / T / Qa");
        Setting.Slider("X", ref General.X, 0, 7680);
        Setting.Slider("Y", ref General.Y, 0, 4320);
        Setting.Slider("Width", ref General.Width, GeneralTab.MinWidth, GeneralTab.MaxWidth);
    }

    void DrawCombat()
    {
        Setting.Checkbox("Show", ref Combat.Show);
        Setting.Checkbox("Hide in town", ref Combat.HideInTown, "Hides the overlay in Ember's Rest and stops reading combat data there - stays on while the training dummy is running");
        Setting.Checkbox("Count overkill", ref Combat.CountOverkill, "Count the damage a hit does beyond the monster's remaining HP");
        Setting.Checkbox("Exact numbers", ref Combat.Exact, "Full digits instead of K / M / B / T / Qa");
        Setting.Slider("Window seconds", ref Combat.Window, 1, 30);
        Setting.Slider("X", ref Combat.X, 0, 7680);
        Setting.Slider("Y", ref Combat.Y, 0, 4320);
        Setting.Slider("Width", ref Combat.Width, GeneralTab.MinWidth, GeneralTab.MaxWidth);
    }

    void DrawLook()
    {
        Setting.Style("Title", Look.Title);
        Setting.Style("Labels", Look.Labels);
        Setting.Style("Values", Look.Values);
        Setting.Style("Game tracker note", Look.Note);
        Setting.Color("Background", ref Look.Background);
    }
}
