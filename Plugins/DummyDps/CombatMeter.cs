using System;
using System.Collections.Generic;

namespace DummyDpsPlugin;

internal sealed class CombatMeter
{
    internal const double IdleSeconds = 5;

    readonly List<(double Time, double Total)> _samples = [];
    int _area = int.MinValue;
    double _last;
    double _fightStart;
    double _fightBase;
    double _lastHit;
    double _now;

    internal int SampleCount => _samples.Count;

    public bool HasFight { get; private set; }
    public double Dps { get; private set; }
    public double Peak { get; private set; }
    public bool Active => HasFight && _now - _lastHit <= IdleSeconds;
    public double Total => HasFight ? _last - _fightBase : 0;
    public double Average => HasFight ? Total / Math.Max(_lastHit - _fightStart, 1) : 0;
    public int FightSeconds => HasFight ? (int)(_lastHit - _fightStart) : 0;

    public void Reset()
    {
        _samples.Clear();
        _area = int.MinValue;
        HasFight = false;
        Dps = Peak = 0;
    }

    public void Add(double seconds, double total, int area, double window)
    {
        if (area == _area && seconds == _now) return;
        _now = seconds;
        if (area != _area || total < _last)
        {
            Reset();
            _area = area;
            _last = total;
            _samples.Add((seconds, total));
            return;
        }

        if (total > _last)
        {
            if (!HasFight || seconds - _lastHit > IdleSeconds)
            {
                HasFight = true;
                _fightStart = seconds;
                _fightBase = _last;
                Peak = 0;
            }

            _lastHit = seconds;
        }

        _last = total;
        _samples.Add((seconds, total));
        var keepFrom = _samples.FindLastIndex(s => s.Time <= seconds - window);
        if (keepFrom > 0) _samples.RemoveRange(0, keepFrom);
        if (!HasFight) return;

        var from = Math.Max(seconds - window, _fightStart);
        var index = from > _fightStart ? _samples.FindLastIndex(s => s.Time <= from) : -1;
        var (baseTime, baseTotal) = index >= 0 ? _samples[index] : (_fightStart, _fightBase);
        Dps = (total - baseTotal) / Math.Max(seconds - baseTime, 1);
        if (Active && Dps > Peak) Peak = Dps;
    }
}
