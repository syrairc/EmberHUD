using System;
using System.Diagnostics;
using System.Threading;
using EmberHUD;
using Tartarus.Sim;

namespace Tartarus;

public readonly record struct SolverProgress(int Done, int Total, TimeSpan Elapsed, bool Finished)
{
    public TimeSpan Eta => Done > 0 && !Finished ? Elapsed * ((double)(Total - Done) / Done) : TimeSpan.Zero;
}

public sealed class SolverWorker : IDisposable
{
    sealed class Job(int total)
    {
        public readonly int Total = total;
        public readonly long Start = Stopwatch.GetTimestamp();
        public volatile int Done;
        public long End;
    }

    Job? job;
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

    public SolverProgress? Progress
    {
        get
        {
            lock (gate)
            {
                if (job is not { } current) return null;
                var finished = Interlocked.Read(ref current.End) != 0;
                var end = finished ? current.End : Stopwatch.GetTimestamp();
                return new SolverProgress(finished ? current.Total : current.Done, current.Total, Stopwatch.GetElapsedTime(current.Start, end), finished);
            }
        }
    }

    public void Submit(Run run, int generation)
    {
        var copy = run.Clone();
        lock (gate)
        {
            if (disposed || generation == this.generation) return;
            this.generation = generation;
            job = new Job(Playouts);
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
            Run run;
            Job current;
            int jobGeneration;
            CancellationToken token;
            lock (gate)
            {
                if (disposed) return;
                if (pending is null) continue;
                run = pending;
                current = job!;
                pending = null;
                jobGeneration = generation;
                token = cancel!.Token;
            }
            Ranked[] result;
            try { result = Solver.Rank(run, current.Total, new Rng((ulong)jobGeneration + 1), token, done => current.Done = done); }
            catch (Exception error)
            {
                Log.Error(error, "tartarus solver");
                continue;
            }
            Interlocked.Exchange(ref current.End, Stopwatch.GetTimestamp());
            lock (gate)
            {
                if (token.IsCancellationRequested || jobGeneration != generation) continue;
                ranked = result;
                finished = jobGeneration;
            }
        }
    }
}
