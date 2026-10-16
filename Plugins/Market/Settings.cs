using System.Drawing;
using EmberHUD;
using EmberHUD.Ui;

namespace Market;

public sealed class Settings : PluginSettings
{
    public bool Show = true;
    public bool CurrentPageOnly;
    public int MaxPrice;
    public float MaxPerUnit;
    public int StaleMinutes = 5;
    public bool DimNonMatches = true;
    public Color GlowColor = Color.FromArgb(0xFF, 0xF9, 0xB6, 0x26);
    public float GlowThickness = 2;

    public override void Draw()
    {
        Setting.Checkbox("Show window", ref Show, "The listing window, only while the trade results are open");
        Setting.Slider("Stale after (minutes)", ref StaleMinutes, 1, 60, "Rows from a page you looked at longer ago than this go grey - other players keep buying");
        Setting.Checkbox("Dim non-matches", ref DimNonMatches, "Darken every cell on the current page that fails the filters or is sold");
        Setting.Color("Glow colour", ref GlowColor);
        Setting.Slider("Glow thickness", ref GlowThickness, 1, 10);
        Controls.Section("Hotkeys");
        Controls.PluginHotkeys();
    }
}
