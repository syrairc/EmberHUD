using System;
using System.Drawing;
using System.Text.Json.Serialization;
using EmberHUD;
using EmberHUD.Ui;
using Hexa.NET.ImGui;

namespace Prices;

public sealed partial class Settings
{
    public bool ProfitFull = true;
    public TextStyle ProfitFullStyle = ProfitStyle();
    public int ProfitFullX = 40;
    public int ProfitFullY = 500;
    public bool ProfitCompact;
    public TextStyle ProfitCompactStyle = ProfitStyle();
    public int ProfitCompactX = 600;
    public int ProfitCompactY = 10;
    public bool ProfitIcon = true;
    public bool ProfitHideFullScreen = true;
    public bool ProfitKeepOnTradeHouse;
    public bool ProfitLock = true;
    public Hotkey ProfitMoveKey = new(Key.Alt);

    [JsonIgnore] public Action? SessionReset;

    static TextStyle ProfitStyle() => new()
    {
        Color = Color.FromArgb(0xF9, 0xDD, 0x75),
        BgEnabled = true,
        BgColor = Color.FromArgb(0xB4, 0, 0, 0),
        BorderEnabled = true,
        BorderColor = Color.FromArgb(0xD9, 0xA4, 0x41),
        BorderRounding = 4,
        BorderThickness = 1,
        ShadowEnabled = true,
    };

    internal void DrawOverlay()
    {
        Setting.Checkbox("Show full profit widget", ref ProfitFull, "A panel with the session stats: this map, last map, profit per map, profit per hour, session profit, total wealth and FE on hand. Needs Track wealth on", id: "ProfitFull");
        Setting.Style("Full widget text", ProfitFullStyle, "Colour, size, background and border of the full profit widget", id: "ProfitFullStyle");
        Setting.Checkbox("Show compact profit widget", ref ProfitCompact, "A one-line widget with /hr, /map and total wealth", id: "ProfitCompact");
        Setting.Style("Compact widget text", ProfitCompactStyle, "Colour, size, background and border of the compact profit widget", id: "ProfitCompactStyle");
        Setting.Checkbox("Show FE icon", ref ProfitIcon, "The FE icon next to the profit numbers - needs the game icons from Setup", id: "ProfitIcon");
        Setting.Checkbox("Hide behind full-screen panels", ref ProfitHideFullScreen, "The profit widgets do not draw while the game has a full-screen panel open, e.g. the big map", id: "ProfitHideFullScreen");
        Setting.Checkbox("Keep on trade house", ref ProfitKeepOnTradeHouse, "Keep the profit widgets up on the trade house even though it is full screen", id: "ProfitKeepOnTradeHouse");
        Setting.Checkbox("Lock", ref ProfitLock, "The profit widgets ignore the mouse while locked - hold the move key to drag them anyway", id: "ProfitLock");
        Setting.Hotkey("Move key", ref ProfitMoveKey, "Hold to move the profit widgets while locked", id: "ProfitMoveKey");
        if (ImGui.Button("Reset session##profit")) SessionReset?.Invoke();
        EmberHUD.Ui.Text.Tip("Starts the profit session over from now. Only the numbers on the widgets restart - the dashboard and the saved wealth data are untouched");
    }
}
