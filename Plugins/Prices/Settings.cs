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

    public const int MinRefreshMinutes = 5;

    public bool Shows(int rareValue) => rareValue switch { 1 => ShowGrey, 2 => ShowCyan, 3 => ShowPurple, 4 => ShowPink, 100 => ShowOrange, _ => true };

    string _seasonText = "";
    bool _seasonEditing;

    public override void Draw()
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
