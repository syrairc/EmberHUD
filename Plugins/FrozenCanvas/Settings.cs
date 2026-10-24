using EmberHUD;
using EmberHUD.Ui;

namespace FrozenCanvas;

public sealed class Settings : PluginSettings
{
    public bool Enabled = true;
    public bool ShowPanel = true;
    public bool Lock = true;
    public Hotkey MoveKey = new(Key.Alt);
    public int PanelX = 2750;
    public int PanelY = 200;

    public override void Draw()
    {
        Setting.Checkbox("Enabled", ref Enabled);
        Setting.Checkbox("Show panel", ref ShowPanel, "Every value the reader sees, for checking against the game");
        Setting.Checkbox("Lock", ref Lock, "A locked panel ignores the mouse - hold the move key to drag it anyway");
        Setting.Hotkey("Move key", ref MoveKey, "Hold to move the panel while locked");
    }
}
