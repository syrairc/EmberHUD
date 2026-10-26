using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Numerics;
using EmberHUD;
using EmberHUD.Ui;

namespace MapInfoPlugin;

internal enum RowKind { Title, Line, Bar, Content, Modifier }

internal readonly record struct Row(RowKind Kind, string Text, MapContent Content = default, int Remaining = 0, int Max = 0);

internal readonly record struct Laid(Row Row, string[] Lines, float LineHeight, float Height);

public sealed class Main : Plugin<Settings>
{
    internal const float Gap = 4;
    internal const int VoraxBuffId = 43022;
    const float UnderlineGap = 4;

    internal static List<Row> Rows(MapInfo map, SectionsTab show, int? enemyMax = null, int? voraxLeft = null)
    {
        List<Row> rows = [];
        if (show.Title && map.Name.Length > 0) rows.Add(new Row(RowKind.Title, map.Name));
        List<string> parts = [];
        if (show.Level) parts.Add($"Monster level {map.MonsterLevel}");
        var bar = show.Enemies && show.EnemiesBar;
        if (show.Enemies && !bar && map.Remaining is { } remaining) parts.Add($"{remaining} enemies");
        if (parts.Count > 0) rows.Add(new Row(RowKind.Line, string.Join(" · ", parts)));
        if (bar && map.Remaining is { } enemiesLeft)
        {
            var max = Math.Max(enemyMax ?? 0, enemiesLeft);
            rows.Add(new Row(RowKind.Bar, $"{enemiesLeft} / {max}", Remaining: enemiesLeft, Max: max));
        }
        if (show.Xp && map.XpGain is { } xp) rows.Add(new Row(RowKind.Line, $"Map Base XP gain: {xp}"));
        if (show.Content) rows.AddRange(map.Content.Select(c => new Row(RowKind.Content, c.Total > 1 ? $"{c.Name}  {c.Done}/{c.Total}" : c.Name, c)));
        if (show.Vorax && voraxLeft is { } left)
            rows.Add(new Row(RowKind.Content, left > 0 ? $"Vorax Elites  {left} left" : "Vorax Elites  cleared", new MapContent("Vorax Elites", "game:UI_MiniMap_S13jingyingguai", left > 0 ? ContentState.Active : ContentState.Done)));
        if (show.Modifiers) rows.AddRange(map.Modifiers.Select(m => new Row(RowKind.Modifier, m)));
        return rows;
    }

    internal static bool Hidden(Settings settings, GameState state) =>
        !settings.General.Show || state.Area is not { } map ||
        settings.General.HideBehindPanels && state.Ui.AnyFullScreen;

    internal static bool BehindBag(GameUi ui, Vector2 origin, Vector2 size) =>
        ui.Panels.Any(p => p.Kind == PanelKind.Inventory && origin.X < p.Max.X && p.Min.X < origin.X + size.X && origin.Y < p.Max.Y && p.Min.Y < origin.Y + size.Y);

    internal static TextStyle StyleOf(RowKind kind, LookTab look) => kind switch
    {
        RowKind.Title => look.Title,
        RowKind.Line => look.Lines,
        RowKind.Bar => look.Bar,
        RowKind.Content => look.ContentText,
        _ => look.Modifiers,
    };

    internal static List<Laid> Layout(List<Row> rows, LookTab look, float width)
    {
        List<Laid> laid = [];
        foreach (var row in rows)
        {
            var style = StyleOf(row.Kind, look);
            var lineHeight = Draw.MeasureText("Ag", style).Y;
            var textWidth = row.Kind == RowKind.Content ? width - 2 * (look.IconSize + Gap) : width;
            string[] lines = row.Kind is RowKind.Title or RowKind.Bar ? [row.Text] : Text.Wrap(row.Text, Math.Max(1, textWidth), style);
            var height = row.Kind switch
            {
                RowKind.Title => lineHeight + UnderlineGap + 2,
                RowKind.Bar => look.BarHeight,
                RowKind.Content => Math.Max(look.IconSize, lineHeight * lines.Length),
                _ => lineHeight * lines.Length,
            };
            laid.Add(new Laid(row, lines, lineHeight, height + Gap));
        }
        return laid;
    }

    readonly Overlay _panel = new();
    int _maxArea = -1;
    int _max;
    MapInfo? _laidMap;
    int _laidMax;
    int? _laidVorax;
    float _laidWidth;
    long _laidAt;
    float _laidHeight;
    List<Laid> _laid = [];

    int _voraxArea = -1;
    bool _voraxSeen;
    int _voraxLeft;
    int? _vorax;

    public override void Tick()
    {
        var state = Game.State;
        EnemyMax(state.AreaId, state.Area?.Remaining);
        if (state.Area is null || !Settings.Sections.Vorax) return;
        Game.WantCharacter();
        _vorax = VoraxLeft(state.AreaId, state.Buffs);
    }

    internal int? VoraxLeft(int areaId, Buff[]? buffs)
    {
        if (areaId != _voraxArea)
        {
            _voraxArea = areaId;
            _voraxSeen = false;
        }
        if (buffs is not null)
        {
            var found = buffs.Where(b => b.Id == VoraxBuffId).Select(b => (int?)b.Stacks).FirstOrDefault();
            if (found is { } stacks)
            {
                _voraxSeen = true;
                _voraxLeft = stacks;
            }
            else if (_voraxSeen) _voraxLeft = 0;
        }
        return _voraxSeen ? _voraxLeft : null;
    }

    internal void EnemyMax(int areaId, int? remaining)
    {
        if (areaId != _maxArea)
        {
            _maxArea = areaId;
            _max = 0;
        }
        _max = Math.Max(_max, remaining ?? 0);
    }

    public override void Initialize() =>
        RegisterHotkey("Toggle panel", "Shows or hides the map info panel", () => Settings.General.Show = !Settings.General.Show, new Hotkey(Key.F4));

    internal List<Laid> LaidFor(MapInfo map, long now)
    {
        var width = Settings.General.Width;
        if (ReferenceEquals(map, _laidMap) && _max == _laidMax && _vorax == _laidVorax && width == _laidWidth && now - _laidAt < 250) return _laid;
        _laidMap = map;
        _laidMax = _max;
        _laidVorax = _vorax;
        _laidWidth = width;
        _laidAt = now;
        _laid = Layout(Rows(map, Settings.Sections, _max, _vorax), Settings.Look, width);
        _laidHeight = _laid.Sum(l => l.Height);
        return _laid;
    }

    public override void Render()
    {
        var state = Game.State;
        if (Hidden(Settings, state)) return;
        var general = Settings.General;
        var look = Settings.Look;
        var laid = LaidFor(state.Area!, Environment.TickCount64);
        if (laid.Count == 0) return;

        _panel.Locked = general.Lock;
        _panel.MoveHeld = general.MoveKey.Key != Key.None && Input.IsDown(general.MoveKey.Key);
        var origin = new Vector2(general.X, general.Y);
        var size = new Vector2(general.Width, _laidHeight);
        if (general.HideBehindBag && BehindBag(state.Ui, origin, size)) return;
        var (hovered, edge, active) = _panel.Handle("mapinfo", ref origin, size, ref general.X, ref general.Y, ref general.Width, GeneralTab.MinWidth, GeneralTab.MaxWidth);
        if (hovered || active) Overlay.ClickBlocker("mapinfo", origin, size);

        var at = origin;
        foreach (var row in laid)
        {
            DrawRow(row, at, size.X, look);
            at.Y += row.Height;
        }
        if (_panel.Movable) Overlay.DragHint(origin, origin + size, active, hovered, edge);
    }

    static void DrawRow(Laid laid, Vector2 at, float width, LookTab look)
    {
        var style = StyleOf(laid.Row.Kind, look);
        if (laid.Row.Kind == RowKind.Content)
        {
            var icon = look.IconSize;
            var middle = at.Y + Math.Max(icon, laid.LineHeight * laid.Lines.Length) / 2;
            Draw.Icon(laid.Row.Content.Icon, new Vector2(at.X + icon / 2, middle), icon / 2, Color.White);
            var textAt = new Vector2(at.X + icon + Gap, middle - laid.LineHeight * laid.Lines.Length / 2);
            foreach (var line in laid.Lines)
            {
                Draw.Text(line, textAt, style);
                textAt.Y += laid.LineHeight;
            }
            DrawMark(new Vector2(at.X + width - icon / 2, middle), icon * 0.4f, laid.Row.Content.State, look);
            return;
        }

        if (laid.Row.Kind == RowKind.Bar)
        {
            var height = look.BarHeight;
            var row = laid.Row;
            Draw.Bar(at, at + new Vector2(width, height), row.Max > 0 ? 1 - row.Remaining / (float)row.Max : 0, look.BarFill, look.BarTrack);
            Draw.Text(row.Text, at + new Vector2(width / 2, height / 2), style, Align.Center | Align.Middle);
            return;
        }

        foreach (var line in laid.Lines)
        {
            Draw.Text(line, at, style);
            at.Y += laid.LineHeight;
        }
        if (laid.Row.Kind == RowKind.Title)
            Draw.RectGradient(new Vector2(at.X, at.Y + UnderlineGap), new Vector2(at.X + width, at.Y + UnderlineGap + 2), look.Underline, Color.FromArgb(0, look.Underline));
    }

    internal static void DrawMark(Vector2 centre, float radius, ContentState state, LookTab look)
    {
        switch (state)
        {
            case ContentState.Done:
                Span<Vector2> tick = [centre + new Vector2(-radius, 0), centre + new Vector2(-radius * 0.3f, radius * 0.7f), centre + new Vector2(radius, -radius * 0.7f)];
                Draw.Polyline(tick, look.Done, 2.5f);
                break;
            case ContentState.Active:
                Draw.Circle(centre, radius, look.Active, 2);
                Draw.CircleFilled(centre, radius * 0.5f, look.Active);
                break;
            default:
                Draw.Circle(centre, radius, look.Pending, 2);
                break;
        }
    }
}
