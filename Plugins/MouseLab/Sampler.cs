using System;
using System.Collections.Generic;
using System.Threading;
using EmberHUD;

namespace MouseLab;

public sealed class Sampler(Func<(int X, int Y)> cursor, Func<double> clockMs, Action<int> wait) : IDisposable
{
    const int MaxSamples = 5000;

    readonly List<PathSample> _samples = [];
    Thread? _thread;
    volatile bool _stop;

    public void Start()
    {
        Stop();
        lock (_samples) _samples.Clear();
        _stop = false;
        Take();
        _thread = new Thread(Poll) { IsBackground = true, Name = "MouseLab sampler" };
        _thread.Start();
    }

    public IReadOnlyList<PathSample> Stop()
    {
        _stop = true;
        if (_thread is not null)
        {
            _thread.Join();
            _thread = null;
            Take();
        }
        lock (_samples) return _samples.ToArray();
    }

    public void Dispose() => Stop();

    void Poll()
    {
        try
        {
            while (!_stop && Take()) wait(1);
        }
        catch (Exception)
        {
        }
    }

    bool Take()
    {
        try
        {
            var (x, y) = cursor();
            lock (_samples)
            {
                if (_samples.Count >= MaxSamples) return false;
                _samples.Add(new PathSample(clockMs(), x, y));
                return true;
            }
        }
        catch (Exception)
        {
            return false;
        }
    }
}
