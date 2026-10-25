using System;
using EmberHUD;
using EmberHUD.Ui;
using Hexa.NET.ImGui;

namespace XpTrackerPlugin;

public sealed class Settings : PluginSettings
{
    const string Tokens = "{xphr} {xpmap} {mapshr} {etah} {etamaps} {maps} {level} {pct}";

    public bool Show = true;
    public bool MapTimeOnly = true;
    public bool HideBehindPanels = true;
    public int OffsetY = 6;
    public bool ShowDeathPanel = true;
    public bool Lock = true;
    public Hotkey MoveKey = new(Key.Alt);
    public int PanelX = 1582;
    public int PanelY = 1010;
    public int BackgroundOpacity = 0;
    public TextLine Left = new() { Enabled = true, Template = "{xpmap} xp/map  {mapshr} maps/hr", Align = HAlign.Left };
    public TextLine Center = new() { Enabled = true, Template = "{xphr} xp/hr", Align = HAlign.Center };
    public TextLine Right = new() { Enabled = true, Template = "Level in {etah} / {etamaps} maps", Align = HAlign.Right };

    internal Action? ResetSession;

    internal void Normalize()
    {
        Left ??= new() { Enabled = true, Template = "{xpmap} xp/map  {mapshr} maps/hr", Align = HAlign.Left };
        Center ??= new() { Enabled = true, Template = "{xphr} xp/hr", Align = HAlign.Center };
        Right ??= new() { Enabled = true, Template = "Level in {etah} / {etamaps} maps", Align = HAlign.Right };
        foreach (var line in new[] { Left, Center, Right })
        {
            line.Template ??= "";
            line.Style ??= TextStyle.Hud();
        }
    }

    public override void Draw()
    {
        Setting.Checkbox("Show", ref Show);
        Setting.Checkbox("Map time only", ref MapTimeOnly, "Only count time spent in maps, so town and hideout do not drag the rates down");
        Setting.Checkbox("Hide behind full-screen panels", ref HideBehindPanels, "Nothing draws while the game has a full-screen panel open, e.g. the big map");
        Setting.Slider("Offset Y", ref OffsetY, -100, 200, "Pixels above the game's XP bar, negative moves it down over the bar");
        Setting.Checkbox("Show death panel", ref ShowDeathPanel, "On the death screen, shows the XP you would lose without a revival token and how long that takes to farm back");
        Setting.Checkbox("Lock death panel", ref Lock, "A locked panel ignores the mouse - hold the move key to drag it anyway");
        Setting.Hotkey("Move key", ref MoveKey, "Hold to move the death panel while locked");
        Setting.Slider("Death panel background", ref BackgroundOpacity, 0, 100, "Opacity of the box behind the death panel, 0 = fully transparent");
        if (ImGui.Button("Reset session")) ResetSession?.Invoke();
        EditLine("Left", Left);
        EditLine("Center", Center);
        EditLine("Right", Right);
    }

    static void EditLine(string label, TextLine line)
    {
        ImGui.PushID(label);
        ImGui.SeparatorText(label);
        Setting.Checkbox($"{label} text", ref line.Enabled);
        Setting.Input($"{label} template", ref line.Template, $"Tokens: {Tokens}");
        Setting.Style($"{label} style", line.Style);
        Setting.Choice($"{label} align", ref line.Align);
        ImGui.PopID();
    }
}
