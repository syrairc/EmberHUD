using System.Drawing;
using EmberHUD;
using EmberHUD.Ui;

namespace GroundRing;

public sealed class Settings : PluginSettings
{
    public bool Show = true;
    public float WorldRadius = 100;
    public float Thickness = 2;
    public Color Colour = Color.FromArgb(220, 255, 80, 40);

    public override void Draw()
    {
        Setting.Checkbox("Show rings", ref Show);
        Setting.Slider("World radius", ref WorldRadius, 20, 500);
        Setting.Slider("Thickness", ref Thickness, 1, 6);
        Setting.Color("Colour", ref Colour);
        Controls.Section("Hotkeys");
        Controls.PluginHotkeys();
    }
}
