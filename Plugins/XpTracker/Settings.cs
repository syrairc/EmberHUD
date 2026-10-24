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
