using EmberHUD;
using EmberHUD.Ui;

namespace Logging;

public sealed class Settings : PluginSettings
{
    public bool ShowPanel = true;
    public int MaxLines = 2000;
    public LogLevel DefaultLevel = LogLevel.Info;

    public override void Draw()
    {
        Setting.Checkbox("Show window", ref ShowPanel);
        Setting.Slider("Max lines", ref MaxLines, 50, 20000, "Newest lines kept in the list");
        Setting.Choice("Default level", ref DefaultLevel, "Level the window starts on, the host minimum is not changed");
        Controls.Section("Hotkeys");
        Controls.PluginHotkeys();
    }
}
