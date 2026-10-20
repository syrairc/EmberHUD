using EmberHUD;
using EmberHUD.Ui;

namespace Tarot;

public sealed class Settings : PluginSettings
{
    public bool ShowPanel = true;
    public bool Lock = true;
    public Hotkey MoveKey = new(Key.Alt);
    public int PanelX = 40;
    public int PanelY = 430;
    public int AheadTiles = 24;
    public bool HighlightCard = true;

    public override void Draw()
    {
        Setting.Checkbox("Show panel", ref ShowPanel, "The board card next to the screen");
        Setting.Checkbox("Lock", ref Lock, "A locked panel ignores the mouse - hold the move key to drag it anyway");
        Setting.Hotkey("Move key", ref MoveKey, "Hold to move the panel while locked");
        Setting.Checkbox("Highlight card", ref HighlightCard, "Points an arrow at the best card in the game's own hand: green when it lands somewhere good, gold when every card is a trap or a miss");
        Setting.Slider("Tiles ahead", ref AheadTiles, 4, 80, "How many tiles the grid shows, counted from the start of the current row of 4");
    }
}
