using System;
using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using EmberHUD;
using EmberHUD.Ui;

namespace XpTrackerPlugin;

public sealed class Main : Plugin<Settings>
{
    const string Missing = "--";

    readonly XpSession _session = new();
    readonly Stopwatch _clock = Stopwatch.StartNew();

    static string Number(double? value, string format) => value is { } v ? v.ToString(format, CultureInfo.InvariantCulture) : Missing;

    static string Eta(double? hours) => hours switch
    {
        null => Missing,
        < 1 => $"{Math.Round(hours.Value * 60):0}m",
        _ => $"{hours.Value:0.0}h",
    };

    internal static string Render(string template, XpSession session)
    {
        var latest = session.Latest;
        return template
            .Replace("{xphr}", session.XpPerHour is { } xphr ? Numbers.Compact((long)xphr) : Missing)
            .Replace("{xpmap}", session.XpPerMap is { } xpmap ? Numbers.Compact((long)xpmap) : Missing)
            .Replace("{mapshr}", Number(session.MapsPerHour, "0.0"))
            .Replace("{etah}", Eta(session.EtaHours))
            .Replace("{etamaps}", session.EtaMaps is { } etamaps ? Math.Ceiling(etamaps).ToString("0", CultureInfo.InvariantCulture) : Missing)
            .Replace("{maps}", session.CompletedMaps.ToString(CultureInfo.InvariantCulture))
            .Replace("{level}", latest is { } l ? l.Level.ToString(CultureInfo.InvariantCulture) : Missing)
            .Replace("{pct}", latest is { } p ? (100.0 * p.Exp / p.ToNext).ToString("0.0", CultureInfo.InvariantCulture) : Missing);
    }

    internal static bool Visible(GameState state, Settings settings) =>
        settings.Show && state.XpBar is not null && !(settings.HideBehindPanels && state.Ui.AnyFullScreen);

    public override void Initialize()
    {
        Settings.Normalize();
        Bind();
    }

    internal void Bind() => Settings.ResetSession = _session.Reset;

    internal static XpSample Sample(GameState state, bool paused, double seconds)
    {
        var area = state.Area;
        var xp = area is { Name.Length: > 0 } ? state.Xp : null;
        return new XpSample(xp, state.AreaId, area?.Name ?? "", area?.IsTown ?? false, paused, seconds);
    }

    XpState? _aliveXp;

    public override void Tick()
    {
        Game.WantXp();
        if (Game.State is { Death: null, Xp: { } alive }) _aliveXp = alive;
        Bind();
        _session.MapTimeOnly = Settings.MapTimeOnly;
        _session.Add(Sample(Game.State, Game.State.Paused == true, _clock.Elapsed.TotalSeconds));
    }

    readonly Overlay _overlay = new();
    readonly TextStyle _deathStyle = TextStyle.Hud();
    int _unusedWidth, _unusedHeight;

    void RenderDeath(GameState state)
    {
        if (!Settings.ShowDeathPanel || DeathInfo.From(state, _aliveXp, _session) is not { } death) return;
        const float Pad = 8;
        const float BarHeight = 10;
        const float MinWidth = 260;
        var title = death.TokenInUse ? "No XP will be lost" : $"-{Numbers.Compact(death.Loss)} XP ({death.Percent.ToString("0.#", CultureInfo.InvariantCulture)}% of the level)";
        var detail = death.TokenInUse ? "Revival token in use" : $"~{Eta(death.Hours)} of farming to earn it back";
        var caption = $"{Numbers.Compact(death.Xp.Exp)} / {Numbers.Compact(death.Xp.ToNext)}  (level {death.Xp.Level})";
        var width = MathF.Max(MinWidth, MathF.Max(Draw.MeasureText(title, _deathStyle).X, MathF.Max(Draw.MeasureText(detail, _deathStyle).X, Draw.MeasureText(caption, _deathStyle).X)));
        var rowHeight = Draw.RowHeight(_deathStyle);
        _overlay.Locked = Settings.Lock;
        _overlay.MoveHeld = Settings.MoveKey.Key != Key.None && Input.IsDown(Settings.MoveKey.Key);
        var origin = new Vector2(Settings.PanelX, Settings.PanelY);
        var size = new Vector2(width + Pad * 2, rowHeight * 3 + BarHeight + 4 + Pad * 2);
        var (hovered, _, active) = _overlay.Handle("xp-death", ref origin, size, ref Settings.PanelX, ref Settings.PanelY, ref _unusedWidth, 60, 4000, ref _unusedHeight, 0, 0, Overlay.ResizeEdge.None);
        if (hovered || active) Overlay.ClickBlocker("xp-death", origin, size);
        if (Settings.BackgroundOpacity > 0)
            Draw.RectFilled(origin, origin + size, System.Drawing.Color.FromArgb(Math.Clamp(Settings.BackgroundOpacity, 0, 100) * 255 / 100, GameColors.LabelBackground), 4);
        if (_overlay.Movable) Overlay.DragHint(origin, origin + size, active, hovered, Overlay.ResizeEdge.None);

        var text = _deathStyle.Clone();
        var at = new Vector2(origin.X + Pad, origin.Y + Pad);
        text.Color = death.TokenInUse ? GameColors.Positive : GameColors.Negative;
        Draw.Text(title, at, text);
        at.Y += rowHeight;
        text.Color = GameColors.TextDim;
        Draw.Text(detail, at, text);
        at.Y += rowHeight + 2;

        var barMax = at + new Vector2(width, BarHeight);
        float X(float fraction) => at.X + width * Math.Clamp(fraction, 0, 1);
        Draw.RectFilled(at, barMax, System.Drawing.Color.FromArgb(90, GameColors.TextDim), 2);
        Draw.RectFilled(at, new Vector2(X(death.Kept), barMax.Y), GameColors.Xp, 2);
        if (death.Loss > 0) Draw.RectFilled(new Vector2(X(death.Kept), at.Y), new Vector2(X(death.Current), barMax.Y), GameColors.Negative);
        at.Y += BarHeight + 2;
        Draw.Text(caption, at, text);
    }

    public override void Render()
    {
        var state = Game.State;
        RenderDeath(state);
        if (!Visible(state, Settings) || state.XpBar is not { } bar) return;
        var y = bar.Min.Y - Settings.OffsetY;
        DrawLine(Settings.Left, bar.Min.X, y);
        DrawLine(Settings.Center, (bar.Min.X + bar.Max.X) / 2, y);
        DrawLine(Settings.Right, bar.Max.X, y);
    }

    void DrawLine(TextLine line, float x, float y)
    {
        if (!line.Enabled) return;
        var text = Render(line.Template, _session);
        if (text.Length == 0) return;
        var horizontal = line.Align switch { HAlign.Right => Align.Right, HAlign.Center => Align.Center, _ => Align.TopLeft };
        Draw.Text(text, new Vector2(x, y), line.Style, horizontal | Align.Bottom);
    }
}
