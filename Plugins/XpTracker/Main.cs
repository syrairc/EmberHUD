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

    public override void Tick()
    {
        Game.WantXp();
        Bind();
        _session.MapTimeOnly = Settings.MapTimeOnly;
        _session.Add(Sample(Game.State, Game.State.Paused == true, _clock.Elapsed.TotalSeconds));
    }

    public override void Render()
    {
        var state = Game.State;
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
