using System;
using EmberHUD;

namespace XpTrackerPlugin;

public readonly record struct XpSample(XpState? Xp, int AreaId, string AreaName, bool IsTown, bool Paused, double Seconds);

public sealed class XpSession
{
    const double MinClockSeconds = 60;

    double? _lastSeconds;
    XpState? _previous;
    (int Id, string Name)? _openMap;
    long _completedMapXp;

    public bool MapTimeOnly { get; set; }
    public long Gained { get; private set; }
    public double ClockSeconds { get; private set; }
    public int CompletedMaps { get; private set; }
    public long CurrentMapXp { get; private set; }
    public XpState? Latest { get; private set; }

    double Hours => ClockSeconds / 3600;
    bool RatesReady => ClockSeconds >= MinClockSeconds;

    public double? XpPerHour => RatesReady && Gained > 0 ? Gained / Hours : null;

    public double? MapsPerHour => RatesReady && CompletedMaps > 0 ? CompletedMaps / Hours : null;

    public double? XpPerMap => !RatesReady ? null
        : CompletedMaps > 0 ? (_completedMapXp > 0 ? (double)_completedMapXp / CompletedMaps : null)
        : _openMap is not null && CurrentMapXp > 0 ? CurrentMapXp
        : null;

    public double? EtaHours => Latest is { } latest && XpPerHour is { } rate ? (latest.ToNext - latest.Exp) / rate : null;

    public double? EtaMaps => Latest is { } latest && XpPerMap is { } perMap ? (latest.ToNext - latest.Exp) / perMap : null;

    public void Reset()
    {
        _lastSeconds = null;
        _previous = null;
        _openMap = null;
        _completedMapXp = 0;
        Gained = 0;
        ClockSeconds = 0;
        CompletedMaps = 0;
        CurrentMapXp = 0;
        Latest = null;
    }

    public void Add(in XpSample sample)
    {
        var dt = _lastSeconds is { } last ? sample.Seconds - last : 0;
        _lastSeconds = sample.Seconds;
        if (sample.Xp is not { } xp || !Valid(xp)) return;

        var counting = !sample.Paused && (!MapTimeOnly || _openMap is not null) && dt > 0;
        var delta = _previous is { } previous ? Delta(previous, xp) : 0;
        _previous = xp;
        Latest = xp;
        if (counting)
        {
            ClockSeconds += dt;
            Gained += delta;
            if (_openMap is not null) CurrentMapXp += delta;
        }
        TrackMap(sample);
    }

    static bool Valid(XpState xp) => xp.Level is >= 1 and <= 200 && xp.ToNext > 0 && xp.Exp >= 0 && xp.Exp <= xp.ToNext;

    static long Delta(XpState previous, XpState current) =>
        current.Level == previous.Level ? Math.Max(0, current.Exp - previous.Exp)
        : current.Level > previous.Level ? previous.ToNext - previous.Exp + current.Exp
        : 0;

    void TrackMap(in XpSample sample)
    {
        var key = (sample.AreaId, sample.AreaName);
        if (_openMap is { } open && (sample.IsTown || open != key)) CloseMap();
        if (!sample.IsTown && _openMap is null) _openMap = key;
    }

    void CloseMap()
    {
        CompletedMaps++;
        _completedMapXp += CurrentMapXp;
        CurrentMapXp = 0;
        _openMap = null;
    }
}
