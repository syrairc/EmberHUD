using System;
using System.Collections.Generic;
using System.Threading;
using EmberHUD;

namespace Tarot;

public sealed class SolverWorker : IDisposable
{
    sealed record Job(SimState Sim, IReadOnlyList<Move>? Moves, SolverOptions Options, string Signature, int Generation, CancellationToken Token);

    readonly object gate = new();
    readonly SemaphoreSlim wake = new(0);
    Thread? thread;
    CancellationTokenSource? cancel;
    Job? pending;
    string? lastSignature;
    SolverOptions? lastOptions;
    (string Signature, ActionResult[] Results)? latest;
    int generation;
    bool disposed;

    public (string Signature, ActionResult[] Results)? Latest
    {
        get
        {
            lock (gate) return latest;
        }
    }

    public int Jobs => Volatile.Read(ref generation);

    public bool Running => thread?.IsAlive == true;

    public void Request(TarotState state, SolverOptions options, string? signature = null)
    {
        signature ??= StateMapper.Signature(state);
        lock (gate)
        {
            if (disposed || signature == lastSignature && options == lastOptions) return;
        }
        var sim = StateMapper.ToSim(state);
        var moves = sim is null ? null : StateMapper.Pending(state, sim);
        lock (gate)
        {
            if (disposed || signature == lastSignature && options == lastOptions) return;
            lastSignature = signature;
            lastOptions = options;
            cancel?.Cancel();
            var next = Interlocked.Increment(ref generation);
            if (sim is null)
            {
                latest = null;
                pending = null;
                return;
            }
            cancel = new CancellationTokenSource();
            pending = new Job(sim, moves, options, signature, next, cancel.Token);
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
        var created = new Thread(Loop) { IsBackground = true, Name = "Tarot solver" };
        created.Start();
        return created;
    }

    void Loop()
    {
        while (true)
        {
            wake.Wait();
            Job job;
            lock (gate)
            {
                if (disposed) return;
                if (pending is null) continue;
                job = pending;
                pending = null;
            }
            ActionResult[] results;
            try { results = Evaluate(job); }
            catch (Exception error)
            {
                Log.Error(error, "tarot solver");
                continue;
            }
            lock (gate)
            {
                if (job.Token.IsCancellationRequested || job.Generation != generation) continue;
                latest = (job.Signature, results);
            }
        }
    }

    static ActionResult[] Evaluate(Job job) =>
        job.Moves is null ? Solver.Evaluate(job.Sim, job.Options, job.Token) : Solver.Evaluate(job.Sim, job.Moves, job.Options, job.Token);
}
