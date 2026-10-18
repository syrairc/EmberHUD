using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Tartarus;

public sealed record KillEntry(DateTime When, string Area, int CfgId, InmateSpecies? Species, int Step, string Outcome, double LastLife, double MaxLife, float Distance, double SecondsSeen, int? Nameplates, string? Source = null);

public sealed record RunCell(int Index, int Beast, int Tier);

public sealed record RunEntry(DateTime Start, DateTime? End, int? NameplatesBefore, int? NameplatesAfter, RunCell[] Cells);

public sealed class InmateStore
{
    const int KillCap = 2000;
    const int RunCap = 50;
    const string FileName = "inmates.json";

    sealed record Stored(int[][] Grid, int Unclassified, int Unsure, List<KillEntry> Kills, List<RunEntry> Runs);

    public int[,] Grid { get; private set; } = new int[4, 4];
    public int Unclassified { get; private set; }
    public int Unsure { get; private set; }
    public List<KillEntry> Kills { get; private set; } = [];
    public List<RunEntry> Runs { get; private set; } = [];
    public bool Dirty { get; private set; }
    public bool Loaded { get; private init; } = true;

    public void Add(Sighting sighting, string area, int? nameplates, DateTime when)
    {
        var killed = sighting.End == SightingEnd.Killed;
        if (!killed) Unsure++;
        else if (sighting.Kind.Step is >= 1 and <= 4) Grid[(int)sighting.Kind.Species, sighting.Kind.Step - 1]++;
        else Unclassified++;
        Kills.Add(new(when, area, sighting.CfgId, sighting.Kind.Species, sighting.Kind.Step, killed ? "killed" : "unsure", Finite(sighting.LastLife), Finite(sighting.MaxLife), (float)Finite(sighting.Distance), Finite(sighting.SecondsSeen), nameplates, sighting.Source switch { SightingSource.SceneDead => "scene-dead", SightingSource.SceneAlive => "scene-alive", _ => "life" }));
        if (Kills.Count > KillCap) Kills.RemoveRange(0, Kills.Count - KillCap);
        Dirty = true;
    }

    static double Finite(double value) => double.IsFinite(value) ? value : 0;

    public void AddRun(RunEntry run)
    {
        Runs.Add(run);
        if (Runs.Count > RunCap) Runs.RemoveRange(0, Runs.Count - RunCap);
        Dirty = true;
    }

    public void Clear()
    {
        Grid = new int[4, 4];
        Unclassified = 0;
        Unsure = 0;
        Kills = [];
        Runs = [];
        Dirty = true;
    }

    public static InmateStore Load(string directory)
    {
        var path = Path.Combine(directory, FileName);
        if (!File.Exists(path)) return new();
        string text;
        try { text = File.ReadAllText(path); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return new() { Loaded = false }; }
        try
        {
            var stored = JsonSerializer.Deserialize<Stored>(text) ?? throw new JsonException();
            if (stored.Grid is not { Length: 4 } || stored.Grid.Any(row => row is not { Length: 4 })) throw new JsonException();
            if (stored.Kills is null || stored.Kills.Any(kill => kill is null)) throw new JsonException();
            if (stored.Runs is null || stored.Runs.Any(run => run?.Cells is null)) throw new JsonException();
            if (stored.Kills.Count > KillCap) stored.Kills.RemoveRange(0, stored.Kills.Count - KillCap);
            if (stored.Runs.Count > RunCap) stored.Runs.RemoveRange(0, stored.Runs.Count - RunCap);
            var store = new InmateStore { Unclassified = stored.Unclassified, Unsure = stored.Unsure, Kills = stored.Kills, Runs = stored.Runs };
            for (var s = 0; s < 4; s++)
                for (var step = 0; step < 4; step++) store.Grid[s, step] = stored.Grid[s][step];
            return store;
        }
        catch (Exception e) when (e is JsonException or NotSupportedException)
        {
            try { File.Move(path, path + ".bad", true); } catch (Exception) { }
            return new();
        }
    }

    public void Absorb(InmateStore pending)
    {
        for (var s = 0; s < 4; s++)
            for (var step = 0; step < 4; step++) Grid[s, step] += pending.Grid[s, step];
        Unclassified += pending.Unclassified;
        Unsure += pending.Unsure;
        Kills.AddRange(pending.Kills);
        if (Kills.Count > KillCap) Kills.RemoveRange(0, Kills.Count - KillCap);
        Runs.AddRange(pending.Runs);
        if (Runs.Count > RunCap) Runs.RemoveRange(0, Runs.Count - RunCap);
        Dirty |= pending.Dirty;
    }

    public void Save(string directory)
    {
        if (!Loaded) return;
        var grid = Enumerable.Range(0, 4).Select(s => Enumerable.Range(0, 4).Select(step => Grid[s, step]).ToArray()).ToArray();
        var path = Path.Combine(directory, FileName);
        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(new Stored(grid, Unclassified, Unsure, Kills, Runs)));
            File.Move(path + ".tmp", path, true);
            Dirty = false;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            try { File.Delete(path + ".tmp"); } catch (Exception) { }
        }
    }
}
