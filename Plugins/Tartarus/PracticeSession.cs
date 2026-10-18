using System;
using System.Collections.Generic;
using Tartarus.Sim;

namespace Tartarus;

public sealed class PracticeSession : IDisposable
{
    readonly Stack<Run> undo = [];
    readonly SolverWorker worker = new();
    int hintGeneration;
    bool hintAsked;

    public PracticeSession(Rules rules, ulong seed)
    {
        Rules = rules;
        Restart(seed);
    }

    public Rules Rules { get; }

    public ulong Seed { get; private set; }

    public Run Run { get; private set; } = null!;

    public IReadOnlyList<Move> Legal => Run.Legal();

    public bool CanUndo => undo.Count > 0;

    public int Playouts
    {
        get => worker.Playouts;
        set => worker.Playouts = Math.Clamp(value, 1, 2000);
    }

    public Ranked[]? Hint
    {
        get
        {
            if (!hintAsked) return null;
            var (ranked, generation) = worker.Latest;
            return generation == hintGeneration ? ranked : null;
        }
    }

    public bool HintPending => hintAsked && Hint is null;

    public void Restart(ulong seed)
    {
        Seed = seed;
        Run = Run.Start(Rules, seed);
        undo.Clear();
        hintAsked = false;
    }

    public void Play(Move move)
    {
        var before = Run.Clone();
        Run.Apply(move);
        undo.Push(before);
        hintAsked = false;
    }

    public bool Undo()
    {
        if (!undo.TryPop(out var previous)) return false;
        Run = previous;
        hintAsked = false;
        return true;
    }

    public void RequestHint()
    {
        hintAsked = true;
        worker.Submit(Run, ++hintGeneration);
    }

    public void Dispose() => worker.Dispose();
}
