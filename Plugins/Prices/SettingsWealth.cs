using System;
using System.Text.Json.Serialization;
using EmberHUD.Ui;
using Hexa.NET.ImGui;

namespace Prices;

public sealed partial class Settings
{
    public bool TrackWealth = true;
    public int SnapshotMinutes = 5;
    public bool WealthPaused;

    [JsonIgnore] public Func<string[]>? WealthStatus;
    [JsonIgnore] public Action? WealthReset;
    [JsonIgnore] public Func<string>? WealthOpen;
    [JsonIgnore] public string WealthResult = "";

    internal void DrawWealth()
    {
        Setting.Checkbox("Track wealth", ref TrackWealth, "Records the FE value of your bags (and every stash tab you open) and the loot of every map run to files next to the price data, for the local dashboard. Needs Show prices on");
        Setting.Slider("Snapshot every (minutes)", ref SnapshotMinutes, 1, 10, "How often the bag value is recorded while you stay in one area. Entering or leaving an area always records one");
        Setting.Checkbox("Pause", ref WealthPaused, "Stop recording without losing anything already saved");
        if (WealthStatus is { } status)
            foreach (var line in status()) ImGui.TextDisabled(line);
        if (ImGui.Button("Reset##wealth")) ImGui.OpenPopup("Reset wealth data");
        EmberHUD.Ui.Text.Tip("Erases the saved runs, totals and stash tabs of the current season, and the run in progress");
        Layout.ConfirmModal("Reset wealth data", "Erase the saved runs, totals and stash tabs of this season?", out var confirmed);
        if (confirmed) WealthReset?.Invoke();
        ImGui.SameLine();
        if (ImGui.Button("Open dashboard##wealth") && WealthOpen is { } open) WealthResult = open();
        EmberHUD.Ui.Text.Tip("Opens the wealth dashboard page in your browser");
        if (WealthResult.Length > 0) ImGui.TextWrapped(WealthResult);
    }
}
