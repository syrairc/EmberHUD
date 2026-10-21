using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using Hexa.NET.ImGui;
using EmberHUD;
using EmberHUD.Ui;

namespace Logging;

public sealed class Main : Plugin<Settings>
{
    const string WindowName = "Logging###logging";
    const string TextLabel = "##text";

    readonly HashSet<string> _sources = [];
    readonly Throttle _daysThrottle = new(5000, () => Environment.TickCount64);
    LogEntry[]? _raw;
    (int Version, DateTime Date, DateOnly? Day, int Max) _rawKey;
    (int Loads, LogLevel Level, string Search, DateTime ClearedAt, int Sources, int Max) _shownKey;
    int _loads;
    int _sourceEpoch;
    string[] _sourceNames = [];
    string _sourcePreview = "All sources";
    internal Func<DateTime> Now = () => DateTime.Now;
    DateOnly[] _days = [];
    string[] _dayLabels = ["Today"];
    bool _levelSet;
    byte[] _utf8 = new byte[1];
    string? _scrollWindowName;

    internal int Loads => _loads;
    internal int FilterRuns { get; private set; }
    internal int FramesDrawn { get; private set; }
    internal int Utf8Length { get; private set; }
    internal ReadOnlySpan<byte> Utf8Bytes => _utf8;
    internal bool ScrollWindowFound { get; private set; }
    internal LogEntry[] Shown { get; private set; } = [];
    internal string Block { get; private set; } = "";
    internal LogLevel Level { get; set; }
    internal string Search { get; set; } = "";
    internal DateOnly? Day { get; set; }
    internal bool Paused { get; set; }
    internal DateTime ClearedAt { get; private set; } = DateTime.MinValue;
    internal int MaxLines => Math.Clamp(Settings.MaxLines, 50, 20000);
    internal string CopyText => LogFilter.Text(Shown);

    public override void Initialize() => RegisterHotkey("Toggle", "Shows or hides the log window", () => Settings.ShowPanel = !Settings.ShowPanel, new Hotkey(Key.F9));

    internal void Clear() => ClearedAt = DateTime.Now;

    internal void ToggleSource(string source)
    {
        if (!_sources.Remove(source)) _sources.Add(source);
        SourcesChanged();
    }

    internal void ClearSources()
    {
        _sources.Clear();
        SourcesChanged();
    }

    void SourcesChanged()
    {
        _sourceEpoch++;
        _sourcePreview = _sources.Count == 0 ? "All sources" : string.Join(", ", _sources.Order());
    }

    void Refresh()
    {
        if (!_levelSet)
        {
            Level = Settings.DefaultLevel;
            _levelSet = true;
        }
        var max = MaxLines;
        if (Day is { } picked && picked == DateOnly.FromDateTime(Now().Date)) Day = null;
        var rawKey = (Day is not null ? 0 : Paused && _raw is not null ? _rawKey.Version : LogStore.Version, Now().Date, Day, max);
        if (_raw is null || rawKey != _rawKey)
        {
            _rawKey = rawKey;
            try
            {
                _raw = Day is { } day ? LogStore.Read(day, max) : LogStore.Today(max);
                _sourceNames = _raw.Select(entry => entry.Source ?? "").Distinct().Order().ToArray();
            }
            catch (Exception)
            {
                _raw ??= [];
            }
            _loads++;
        }
        var shownKey = (_loads, Level, Search, ClearedAt, _sourceEpoch, max);
        if (FilterRuns > 0 && shownKey == _shownKey) return;
        _shownKey = shownKey;
        var cleared = ClearedAt;
        Shown = LogFilter.Apply(_raw.Where(entry => entry.Time > cleared).ToArray(), Level, _sources, Search);
        Block = LogFilter.Block(Shown, LogFilter.SourceWidth(Shown));
        var needed = Encoding.UTF8.GetByteCount(Block) + 1;
        if (_utf8.Length < needed || _utf8.Length > needed * 2 + 64) _utf8 = new byte[needed];
        Encoding.UTF8.GetBytes(Block, 0, Block.Length, _utf8, 0);
        _utf8[needed - 1] = 0;
        Utf8Length = needed - 1;
        FilterRuns++;
    }

    void RefreshDays()
    {
        if (!_daysThrottle.Due()) return;
        try
        {
            _days = LogFilter.FileDays(LogStore.Days(), DateOnly.FromDateTime(Now().Date));
        }
        catch (Exception)
        {
            return;
        }
        _dayLabels = _days.Select(day => day.ToString("yyyy-MM-dd")).Prepend("Today").ToArray();
        if (Day is { } current && !_days.Contains(current))
        {
            Day = null;
            ClearedAt = DateTime.MinValue;
        }
    }

    public override void Render()
    {
        if (!Settings.ShowPanel) return;
        Refresh();
        RefreshDays();

        ImGui.SetNextWindowPos(new Vector2(40, 40), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSize(new Vector2(900, 360), ImGuiCond.FirstUseEver);
        var visible = ImGui.Begin(WindowName, ref Settings.ShowPanel);
        try
        {
            if (!visible) return;
            FilterStrip();
            ImGui.Separator();
            TextArea();
            FramesDrawn++;
        }
        finally
        {
            ImGui.End();
        }
    }

    void FilterStrip()
    {
        var level = Level;
        if (Combo.Enum(ref level, "level", 80)) Level = level;
        ImGui.SameLine();
        SourceCombo();
        ImGui.SameLine();
        ImGui.SetNextItemWidth(180);
        var search = Search;
        if (ImGui.InputTextWithHint("##search", "search", ref search, 256)) Search = search;
        ImGui.SameLine();
        DayCombo();
        ImGui.SameLine();
        var paused = Paused;
        if (ImGui.Checkbox("Pause", ref paused)) Paused = paused;
        ImGui.SameLine();
        if (ImGui.Button("Clear")) Clear();
        ImGui.SameLine();
        if (ImGui.Button("Copy")) ImGui.SetClipboardText(CopyText);
    }

    void SourceCombo()
    {
        ImGui.SetNextItemWidth(140);
        if (!ImGui.BeginCombo("##sources", _sourcePreview)) return;
        try
        {
            if (ImGui.Selectable("All", _sources.Count == 0, ImGuiSelectableFlags.NoAutoClosePopups)) ClearSources();
            foreach (var name in _sourceNames)
                if (ImGui.Selectable(name.Length == 0 ? "(none)" : name, _sources.Contains(name), ImGuiSelectableFlags.NoAutoClosePopups)) ToggleSource(name);
        }
        finally
        {
            ImGui.EndCombo();
        }
    }

    void DayCombo()
    {
        var index = Day is { } current ? Math.Max(0, Array.IndexOf(_dayLabels, current.ToString("yyyy-MM-dd"))) : 0;
        if (!Combo.Option(ref index, _dayLabels, "day", 110)) return;
        Day = index == 0 ? null : _days[index - 1];
        ClearedAt = DateTime.MinValue;
    }

    void TextArea()
    {
        _scrollWindowName ??= $"{WindowName}/{TextLabel}_{ImGui.GetID(TextLabel):X8}";
        using var _ = Fonts.Use(FontFace.Mono);
        ImGui.InputTextMultiline(TextLabel, ref _utf8[0], (nuint)_utf8.Length, Vector2.Max(ImGui.GetContentRegionAvail(), new Vector2(50, 40)), ImGuiInputTextFlags.ReadOnly);
        FollowBottom();
    }

    void FollowBottom()
    {
        var window = ImGuiP.FindWindowByName(_scrollWindowName!);
        ScrollWindowFound = !window.IsNull;
        if (window.IsNull || Paused) return;
        if (window.Scroll.Y >= window.ScrollMax.Y - 4) ImGuiP.SetScrollY(window, 1e9f);
    }
}
