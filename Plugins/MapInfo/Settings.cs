using System.Drawing;
using EmberHUD;
using EmberHUD.Ui;

namespace MapInfoPlugin;

public sealed class GeneralTab
{
    internal const int MinWidth = 160;
    internal const int MaxWidth = 1600;

    public bool Show = true;
    public bool Lock = true;
    public Hotkey MoveKey = new(Key.Alt);
    public bool HideInTown = true;
    public bool HideBehindPanels = true;
    public int X = 40;
    public int Y = 220;
    public int Width = 360;
}

public sealed class SectionsTab
{
    public bool Title = true;
    public bool Level = true;
    public bool Enemies = true;
    public bool EnemiesBar;
    public bool Xp = true;
    public bool Content = true;
    public bool Modifiers = true;
}

public sealed class LookTab
{
    public TextStyle Title = new() { Face = FontFace.Bold, Scale = 1.25f, ShadowEnabled = true };
    public TextStyle Lines = TextStyle.Hud();
    public TextStyle ContentText = new() { ShadowEnabled = true };
    public TextStyle Modifiers = TextStyle.Hud();
    public TextStyle Bar = TextStyle.Hud();
    public Color BarFill = Color.FromArgb(0xFF, 0xF9, 0xB6, 0x26);
    public Color BarTrack = Color.FromArgb(0x80, 0, 0, 0);
    public float BarHeight = 18;
    public Color Underline = Color.FromArgb(0xFF, 0x76, 0x57, 0x18);
    public float IconSize = 20;
    public Color Done = Color.FromArgb(0xFF, 0x6C, 0xD9, 0x6C);
    public Color Active = Color.FromArgb(0xFF, 0xF9, 0xB6, 0x26);
    public Color Pending = Color.FromArgb(0xFF, 0xA0, 0xA0, 0xA0);
}

public sealed class Settings : PluginSettings
{
    public GeneralTab General = new();
    public SectionsTab Sections = new();
    public LookTab Look = new();

    public override void Draw() => Layout.TabBar("mapinfo",
        ("General", DrawGeneral),
        ("Sections", DrawSections),
        ("Look", DrawLook));

    void DrawGeneral()
    {
        Setting.Checkbox("Show", ref General.Show);
        Setting.Checkbox("Lock", ref General.Lock, "A locked panel ignores the mouse - hold the move key to drag or resize it anyway");
        Setting.Hotkey("Move key", ref General.MoveKey, "Hold to move or resize a locked panel");
        Setting.Checkbox("Hide in town", ref General.HideInTown, "Nothing draws in town or the hideout");
        Setting.Checkbox("Hide behind full-screen panels", ref General.HideBehindPanels, "Nothing draws while the game has a full-screen panel open, e.g. the big map");
        Setting.Slider("X", ref General.X, 0, 7680);
        Setting.Slider("Y", ref General.Y, 0, 4320);
        Setting.Slider("Width", ref General.Width, GeneralTab.MinWidth, GeneralTab.MaxWidth);
    }

    void DrawSections()
    {
        Setting.Checkbox("Map name", ref Sections.Title);
        Setting.Checkbox("Monster level", ref Sections.Level);
        Setting.Checkbox("Remaining enemies", ref Sections.Enemies);
        Setting.Checkbox("Enemies as progress bar", ref Sections.EnemiesBar, "Needs Remaining enemies on - replaces the enemy count with a bar that fills as enemies die, text is remaining / the highest count seen this map");
        Setting.Checkbox("XP gain", ref Sections.Xp, "Only shows when the game's value could be read");
        Setting.Checkbox("Content", ref Sections.Content, "Vigil, tarot, gods, etc with a done / in progress / not done mark");
        Setting.Checkbox("Modifiers", ref Sections.Modifiers, "Every map modifier with text that isn't already a content row");
    }

    void DrawLook()
    {
        Setting.Style("Map name", Look.Title);
        Setting.Style("Level / enemies / XP", Look.Lines);
        Setting.Style("Content", Look.ContentText);
        Setting.Style("Modifiers", Look.Modifiers);
        Setting.Style("Enemies bar text", Look.Bar);
        Setting.Color("Enemies bar fill", ref Look.BarFill);
        Setting.Color("Enemies bar track", ref Look.BarTrack);
        Setting.Slider("Enemies bar height", ref Look.BarHeight, 8, 40);
        Setting.Color("Underline", ref Look.Underline, "Gold line under the map name, fades out to the right");
        Setting.Slider("Icon size", ref Look.IconSize, 10, 48);
        Setting.Color("Done", ref Look.Done);
        Setting.Color("In progress", ref Look.Active);
        Setting.Color("Not done", ref Look.Pending);
    }
}
