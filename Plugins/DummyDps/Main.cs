using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using EmberHUD;
using EmberHUD.Ui;

namespace DummyDpsPlugin;

internal readonly record struct Row(string Label, string Value, bool Note = false);

public sealed class Main : Plugin<Settings>
{
    const float Gap = 4;
    const float Pad = 8;
    internal static string Compact(long value) => Numbers.Compact(value);

    internal static string Exact(long value) => value.ToString("N0", CultureInfo.InvariantCulture);

    internal static string Clock(int seconds) => $"{seconds / 60}:{seconds % 60:00}";

    internal static bool Over(long unlimited, long limited) => unlimited > limited + limited / 1000;

    internal static List<Row> Rows(WoodStakeStats stats, bool exact)
    {
        string Show(long value) => exact ? Exact(value) : Compact(value);
        List<Row> rows =
        [
            new("Average DPS in a span of time", Show(stats.AvgUnlimited)),
            new("Duration", Clock(stats.Seconds)),
            new("Highest DPS in 5s", Show(stats.MaxUnlimited)),
            new("Average DPS in 5s", Show(stats.CurUnlimited)),
        ];
        if (Over(stats.CurUnlimited, stats.Cur) || Over(stats.AvgUnlimited, stats.Avg) || Over(stats.MaxUnlimited, stats.Max))
            rows.Add(new("Game tracker is capped, shows", $"{Show(stats.Avg)} avg", true));
        return rows;
    }

    internal static bool Visible(WoodStakeStats? stats, int areaId, int keptArea, bool keepAfterStop) =>
        stats is not null && (stats.Running || keepAfterStop && stats.Seconds > 0 && areaId == keptArea);

    internal static bool CombatWanted(GameState state, CombatTab combat, bool hideBehindPanels) =>
        (combat.Show || combat.ShowOnBossBar) && !(combat.HideInTown && state.Area is { IsTown: true } && state.WoodStake is not { Running: true }) && !(hideBehindPanels && state.Ui.AnyFullScreen);

    internal static bool CombatVisible(GameState state, CombatTab combat, bool hideBehindPanels) =>
        combat.Show && CombatWanted(state, combat, hideBehindPanels);

    internal static bool BossReadout(GameState state, CombatTab combat, bool hideBehindPanels) =>
        combat.ShowOnBossBar && state.BossBar is not null && !(hideBehindPanels && state.Ui.AnyFullScreen);

    internal static string BossLine(CombatMeter meter, bool exact) =>
        meter.HasFight ? $"DPS {(exact ? Exact((long)meter.Dps) : Compact((long)meter.Dps))}" : "";

    internal static List<Row> CombatRows(CombatMeter meter, bool exact)
    {
        string Show(double value) => exact ? Exact((long)value) : Compact((long)value);
        return
        [
            new("DPS", Show(meter.Dps)),
            new("Peak DPS", Show(meter.Peak)),
            new("Average DPS", Show(meter.Average)),
            new("Total damage", Show(meter.Total)),
            new("Fight time", Clock(meter.FightSeconds)),
        ];
    }

    readonly Overlay _panel = new();
    readonly Overlay _combatPanel = new();
    internal readonly CombatMeter _meter = new();
    internal readonly CombatMeter _bossMeter = new();
    int _bossId;
    bool _countedOverkill;
    int _keptArea = int.MinValue;
    double? _pausedFrom;
    double _pausedTotal;

    public override void Tick()
    {
        if (Game.State.WoodStake is { Running: true })
            _keptArea = Game.State.AreaId;
        if (CombatWanted(Game.State, Settings.Combat, Settings.General.HideBehindPanels)) Game.WantCombat();
        if (_countedOverkill != Settings.Combat.CountOverkill)
        {
            _countedOverkill = Settings.Combat.CountOverkill;
            _meter.Reset();
            _bossMeter.Reset();
        }

        if (Game.State.Combat is not { } combat) return;
        if (Game.State.Paused == true)
        {
            _pausedFrom ??= combat.Seconds;
            return;
        }

        if (_pausedFrom is { } from)
        {
            _pausedTotal += combat.Seconds - from;
            _pausedFrom = null;
        }
        var seconds = combat.Seconds - _pausedTotal;
        var overkill = Settings.Combat.CountOverkill;
        _meter.Add(seconds, overkill ? combat.Raw : combat.Effective, Game.State.AreaId, Settings.Combat.Window);
        TickBoss(seconds, combat, overkill);
    }

    void TickBoss(double seconds, CombatStats combat, bool overkill)
    {
        if (Game.State.BossOnBar() is not { } boss)
        {
            _bossId = 0;
            _bossMeter.Reset();
            return;
        }

        if (boss.Id != _bossId)
        {
            _bossId = boss.Id;
            _bossMeter.Reset();
        }
        combat.ByVictim.TryGetValue(boss.Id, out var damage);
        _bossMeter.Add(seconds, overkill ? damage.Raw : damage.Effective, Game.State.AreaId, Settings.Combat.Window);
    }

    public override void Initialize()
    {
        RegisterHotkey("Toggle panel", "Shows or hides the dummy DPS panel", () => Settings.General.Show = !Settings.General.Show, new Hotkey(Key.F2));
        RegisterHotkey("Toggle combat panel", "Shows or hides the combat DPS panel", () => Settings.Combat.Show = !Settings.Combat.Show, new Hotkey(Key.F3));
    }

    public override void Render()
    {
        var state = Game.State;
        var general = Settings.General;
        var combat = Settings.Combat;
        if (general.Show && !(general.HideBehindPanels && state.Ui.AnyFullScreen) && Visible(state.WoodStake, state.AreaId, _keptArea, general.KeepAfterStop))
            DrawPanel(_panel, "dummydps", "Training dummy", Rows(state.WoodStake!, general.Exact), ref general.X, ref general.Y, ref general.Width);
        if (CombatVisible(state, combat, general.HideBehindPanels) && _meter.HasFight)
            DrawPanel(_combatPanel, "combatdps", "Combat DPS", CombatRows(_meter, combat.Exact), ref combat.X, ref combat.Y, ref combat.Width);
        if (BossReadout(state, combat, general.HideBehindPanels) && BossLine(_bossMeter, combat.Exact) is { Length: > 0 } line)
        {
            var bar = state.BossBar!.Rect;
            Draw.Text(line, new Vector2((bar.Min.X + bar.Max.X) / 2, bar.Max.Y + combat.BossBarOffsetY), Settings.Look.Values, Align.Center);
        }
    }

    void DrawPanel(Overlay panel, string id, string title, List<Row> rows, ref int x, ref int y, ref int width)
    {
        var general = Settings.General;
        var look = Settings.Look;
        var titleHeight = Draw.MeasureText("Ag", look.Title).Y;
        var lineHeight = Draw.MeasureText("Ag", look.Values).Y;
        var size = new Vector2(width, Pad * 2 + titleHeight + Gap + rows.Count * (lineHeight + Gap));

        panel.Locked = general.Lock;
        panel.MoveHeld = general.MoveKey.Key != Key.None && Input.IsDown(general.MoveKey.Key);
        var origin = new Vector2(x, y);
        var (hovered, edge, active) = panel.Handle(id, ref origin, size, ref x, ref y, ref width, GeneralTab.MinWidth, GeneralTab.MaxWidth);
        if (hovered || active) Overlay.ClickBlocker(id, origin, size);

        Draw.RectFilled(origin, origin + size, look.Background, 6);
        var at = origin + new Vector2(Pad, Pad);
        Draw.Text(title, at, look.Title);
        at.Y += titleHeight + Gap;
        foreach (var row in rows)
        {
            Draw.Text(row.Label, at, row.Note ? look.Note : look.Labels);
            Draw.Text(row.Value, new Vector2(origin.X + size.X - Pad, at.Y), row.Note ? look.Note : look.Values, Align.Right);
            at.Y += lineHeight + Gap;
        }
        if (panel.Movable) Overlay.DragHint(origin, origin + size, active, hovered, edge);
    }
}
