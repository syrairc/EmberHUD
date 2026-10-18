using System;
using System.Threading;
using EmberHUD;
using Tartarus.Sim;

namespace Tartarus;

public sealed class SolverWorker : IDisposable
{
    readonly object gate = new();
    readonly SemaphoreSlim wake = new(0);
    Thread? thread;
    CancellationTokenSource? cancel;
    Run? pending;
    int generation = -1;
    int finished = -1;
    Ranked[]? ranked;
    bool disposed;

    public int Playouts { get; set; } = 300;

    public (Ranked[]? Ranked, int Generation) Latest
    {
        get
        {
            lock (gate) return (finished == generation ? ranked : null, generation);
        }
    }

    public void Submit(Run run, int generation)
    {
        var copy = run.Clone();
        lock (gate)
        {
            if (disposed || generation == this.generation) return;
            this.generation = generation;
            ranked = null;
            pending = copy;
            cancel?.Cancel();
            cancel = new CancellationTokenSource();
            thread ??= StartThread();
        }
        wake.Release();
    }

    public void Dispose()
    {
        Thread? stopped;
        lock (gate)
        {
            if (disposed) return;
            disposed = true;
            cancel?.Cancel();
            stopped = thread;
        }
        wake.Release();
        stopped?.Join(2000);
    }

    Thread StartThread()
    {
        var created = new Thread(Loop) { IsBackground = true, Name = "Tartarus solver" };
        created.Start();
        return created;
    }

    void Loop()
    {
        while (true)
        {
            wake.Wait();
            Run job;
            int jobGeneration;
            CancellationToken token;
            lock (gate)
            {
                if (disposed) return;
                if (pending is null) continue;
                job = pending;
                pending = null;
                jobGeneration = generation;
                token = cancel!.Token;
            }
            Ranked[] result;
            try { result = Solver.Rank(job, Playouts, new Rng((ulong)jobGeneration + 1), token); }
            catch (Exception error)
            {
                Log.Error(error, "tartarus solver");
                continue;
            }
            lock (gate)
            {
                if (token.IsCancellationRequested || jobGeneration != generation) continue;
                ranked = result;
                finished = jobGeneration;
            }
        }
    }
}
