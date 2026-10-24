using System;
using System.Diagnostics;

namespace Vorax;

// Time based, the engine says how long each phase usually takes and the bar creeps toward the phase end, never claiming it before the phase is over
public sealed class Progress
{
    const double Ceiling = 0.95;

    readonly object _gate = new();
    readonly Stopwatch _clock = new();
    bool _active;
    double _from;
    double _to;
    double _expectedMilliseconds = 1;

    public bool Active
    {
        get
        {
            lock (_gate) return _active;
        }
    }

    public double Fraction
    {
        get
        {
            lock (_gate) return _active ? Current() : 1;
        }
    }

    public void Begin()
    {
        lock (_gate)
        {
            _active = true;
            _from = _to = 0;
            _expectedMilliseconds = 1;
            _clock.Restart();
        }
    }

    public void Phase(double toFraction, double expectedMilliseconds)
    {
        lock (_gate)
        {
            _from = Current();
            _to = Math.Max(_from, toFraction);
            _expectedMilliseconds = Math.Max(1, expectedMilliseconds);
            _clock.Restart();
        }
    }

    public void End()
    {
        lock (_gate) _active = false;
    }

    double Current() => _from + (_to - _from) * Math.Min(Ceiling, _clock.ElapsedMilliseconds / _expectedMilliseconds);
}
