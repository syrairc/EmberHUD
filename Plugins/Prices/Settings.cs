using System;
using System.Drawing;
using System.Text.Json.Serialization;
using EmberHUD;
using EmberHUD.Ui;
using Hexa.NET.ImGui;

namespace Prices;

public sealed class Settings : PluginSettings
{
    public bool Enabled = true;
    public Corner Corner = Corner.TopLeft;
    public int OffsetX = 6;
    public int OffsetY = 8;
    public TextStyle Text = new() { Color = Color.FromArgb(0xF9, 0xDD, 0x75), ShadowEnabled = true };
    public bool ShowIcon = true;
    public bool ShowGrey = true;
    public bool ShowCyan = true;
    public bool ShowPurple = true;
    public bool ShowPink = true;
    public bool ShowOrange = true;
    public int RefreshMinutes = 5;
    public int Season;
    public bool Contribute;
    [JsonIgnore] public string Status = "";
    [JsonIgnore] public string ContributeStatus = "";

    public bool GroundEnabled = true;
    public bool GroundGrey;
    public bool GroundCyan;
    public bool GroundPurple;
    public bool GroundPink;
    public bool GroundOrange = true;
    public bool GroundNonEquipment;

    public const int MinRefreshMinutes = 5;

    static bool Pick(int rareValue, bool grey, bool cyan, bool purple, bool pink, bool orange) => rareValue switch { 1 => grey, 2 => cyan, 3 => purple, 4 => pink, 100 => orange, _ => true };

    public bool Shows(int rareValue) => Pick(rareValue, ShowGrey, ShowCyan, ShowPurple, ShowPink, ShowOrange);

    public bool ShowsOnGround(GroundItem item) =>
        (item.Equipment || GroundNonEquipment) && Pick(item.RareValue, GroundGrey, GroundCyan, GroundPurple, GroundPink, GroundOrange);

    string _seasonText = "";
    bool _seasonEditing;

    public override void Draw() => Layout.TabBar("prices", ("Prices", DrawPrices), ("Ground Items", DrawGround));

    internal void DrawGround()
    {
        Setting.Checkbox("Show on ground items", ref GroundEnabled, "A price tag next to the label of each priced item on the ground in maps");
        Setting.Checkbox("Grey items", ref GroundGrey, "Price tags on grey (normal) items on the ground", id: "GroundGrey");
        Setting.Checkbox("Cyan items", ref GroundCyan, "Price tags on cyan (magic) items on the ground", id: "GroundCyan");
        Setting.Checkbox("Purple items", ref GroundPurple, "Price tags on purple (rare) items on the ground", id: "GroundPurple");
        Setting.Checkbox("Pink items", ref GroundPink, "Price tags on pink items on the ground", id: "GroundPink");
        Setting.Checkbox("Orange items", ref GroundOrange, "Price tags on orange (legendary) items on the ground", id: "GroundOrange");
        Setting.Checkbox("Non-equipment items", ref GroundNonEquipment, "Also tag items that are not gear - currency, materials and the like. They follow the same colour choices");
    }

    internal void DrawPrices()
    {
        Setting.Checkbox("Show prices", ref Enabled, "A price tag on each priced item in the open bag");
        Setting.Choice("Corner", ref Corner, "Which corner of the cell the tag sits in - bottom-right is where the game draws its own item count");
        Setting.Slider("Offset X", ref OffsetX, -100, 100, "Pixels from the corner, positive moves the tag right");
        Setting.Slider("Offset Y", ref OffsetY, -100, 100, "Pixels from the corner, positive moves the tag down");
        Setting.Style("Text", Text, "Colour, size, background and shadow of the tag");
        Setting.Checkbox("Show FE icon", ref ShowIcon, "The FE icon in front of the number - needs the game icons from Setup");
        Setting.Checkbox("Grey items", ref ShowGrey, "Price tags on grey frame items (normal)");
        Setting.Checkbox("Cyan items", ref ShowCyan, "Price tags on cyan frame items (magic)");
        Setting.Checkbox("Purple items", ref ShowPurple, "Price tags on purple frame items (rare)");
        Setting.Checkbox("Pink items", ref ShowPink, "Price tags on pink frame items");
        Setting.Checkbox("Orange items", ref ShowOrange, "Price tags on orange frame items (legendary)");
        Setting.Slider("Refresh every (minutes)", ref RefreshMinutes, MinRefreshMinutes, 60, "How often new prices are downloaded");
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted("Season");
        EmberHUD.Ui.Text.Tip("Which season's prices are shown. 0 = automatic - the season from the game log, else the newest one with recent prices. Never changes what is shared - shared prices always carry the season from the game log. Type a number and press Enter");
        ImGui.SameLine(160);
        ImGui.SetNextItemWidth(120);
        if (!_seasonEditing) _seasonText = Season.ToString();
        var entered = ImGui.InputText("##season", ref _seasonText, 8, ImGuiInputTextFlags.EnterReturnsTrue | ImGuiInputTextFlags.AutoSelectAll | ImGuiInputTextFlags.CharsDecimal);
        _seasonEditing = ImGui.IsItemActive();
        if (entered) Season = int.TryParse(_seasonText, out var typed) ? Math.Max(0, typed) : 0;
        if (Status.Length > 0) ImGui.TextDisabled(Status);
        Setting.Checkbox("Share Exchange prices", ref Contribute, "Reads the game log (Settings > Other > Enable Log) and sends the prices of your Exchange searches to TITrack's price server, the same way TITrack does. Off = nothing is read or sent");
        if (Contribute && ContributeStatus.Length > 0) ImGui.TextDisabled(ContributeStatus);
    }
}
