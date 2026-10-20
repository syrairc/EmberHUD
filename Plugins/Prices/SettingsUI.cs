using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json.Serialization;
using EmberHUD.Ui;
using Hexa.NET.ImGui;

namespace Prices;

public sealed partial class Settings
{
    record ShareFile(string Path, string Name, long Size, DateTime Written);

    List<ShareFile> _shareFiles = [];
    int _shareFrame = -10;
    string _sharePath = "";
    string _selectedShare = "";
    string _shareResult = "";

    [JsonIgnore] public PriceSource[] SharedDatabases => [.. new[] { (ShareTitrack, PriceSource.Titrack), (ShareEtor, PriceSource.Etor), (ShareLocal, PriceSource.Local) }.Where(pair => pair.Item1).Select(pair => pair.Item2)];

    void RefreshShareFiles()
    {
        try
        {
            _shareFiles = ShareFolder.Length == 0 || !Directory.Exists(ShareFolder) ? [] : [.. new DirectoryInfo(ShareFolder).EnumerateFiles().Where(file => file.Name.EndsWith(".json.gz", StringComparison.OrdinalIgnoreCase) || file.Name.EndsWith(".json", StringComparison.OrdinalIgnoreCase)).OrderByDescending(file => file.LastWriteTimeUtc).Select(file => new ShareFile(file.FullName, file.Name, file.Length, file.LastWriteTime))];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _shareFiles = [];
        }
    }

    internal void DrawShare()
    {
        var frame = ImGui.GetFrameCount();
        if (frame - _shareFrame > 1) RefreshShareFiles();
        _shareFrame = frame;

        ImGui.SeparatorText("Export");
        Setting.Checkbox("TITrack", ref ShareTitrack, "Put the TITrack prices (downloaded plus anything you imported) in the file", id: "ShareTitrack");
        Setting.Checkbox("ETor", ref ShareEtor, "Put the ETor prices (downloaded plus anything you imported) in the file", id: "ShareEtor");
        Setting.Checkbox("Local", ref ShareLocal, "Put your own saved trade house listings in the file. This is the one worth sharing - TITrack and ETor can be downloaded by anyone", id: "ShareLocal");
        if (ImGui.Button("Export##share"))
        {
            _shareResult = ExportShare is { } export ? export(SharedDatabases) : "";
            RefreshShareFiles();
        }
        EmberHUD.Ui.Text.Tip("Writes one compressed file for the current season into the share folder");
        ImGui.SameLine();
        ImGui.BeginDisabled(!Directory.Exists(ShareFolder));
        if (ImGui.Button("Open folder##share")) System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(ShareFolder) { UseShellExecute = true });
        ImGui.EndDisabled();
        EmberHUD.Ui.Text.Tip("Opens the share folder in Explorer");

        ImGui.SeparatorText("Import");
        ImGui.TextDisabled("Share folder: " + ShareFolder);
        if (ImGui.Button("Refresh##share")) RefreshShareFiles();
        ImGui.SameLine();
        ImGui.BeginDisabled(_selectedShare.Length == 0);
        if (ImGui.Button("Import selected##share") && ImportShare is { } importSelected) _shareResult = importSelected(_selectedShare);
        ImGui.EndDisabled();
        EmberHUD.Ui.Text.Tip("Importing merges the file into what you already have and never deletes anything. Listings you both have keep the newer price and the earlier first-seen time, TITrack / ETor prices keep the newer update");
        if (ImGui.BeginChild("sharefiles", new Vector2(0, 140), ImGuiChildFlags.Borders))
        {
            if (_shareFiles.Count == 0) ImGui.TextDisabled("No files in the share folder - drop a .json.gz here, then Refresh");
            foreach (var file in _shareFiles)
                if (ImGui.Selectable($"{file.Name}   {file.Size / 1024.0:0.#} KB   {file.Written.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)}##{file.Path}", file.Path == _selectedShare)) _selectedShare = file.Path;
        }
        ImGui.EndChild();
        Setting.FilePath("File path", ref _sharePath, "A price file anywhere on this PC", "SharePath", 520, new EmberHUD.DialogFilter("Price files", "*.gz;*.json"), new EmberHUD.DialogFilter("All files", "*.*"));
        if (ImGui.Button("Import path##share") && ImportShare is { } importPath) _shareResult = importPath(_sharePath);
        if (_shareResult.Length > 0) ImGui.TextWrapped(_shareResult);
    }
}
