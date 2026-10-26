using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using EmberHUD;

namespace Tartarus;

public sealed class TrackedRun
{
    public int Day { get; set; }
    public int Size { get; set; }
    public List<string> Assumed { get; set; } = [];
    public int[] ToolsHeld { get; set; } = [];
    public bool SwapUsedToday { get; set; }
    public HashSet<int> SeenCells { get; set; } = [];
    public int ChainArmedAt { get; set; } = -1;
    public int Usurped { get; set; }
    public int UsurpCell { get; set; } = -1;
    public int UsurpNext { get; set; }
    public int[] Marked { get; set; } = [];
}

public sealed class RunTracker : IDisposable
{
    const int ChainReactionId = 242, UsurpationFirst = 252, UsurpationLast = 255;

    readonly string path;
    readonly Func<double> seconds;
    TrackedRun run;
    bool unverified;
    bool started;
    double lastSave = double.NegativeInfinity;
    string? lastWritten;

    public RunTracker(string dataDirectory, Func<double> seconds)
    {
        path = Path.Combine(dataDirectory, "run.json");
        this.seconds = seconds;
        run = Load() ?? new TrackedRun();
        unverified = run.Size > 0;
    }

    public TrackedRun Update(TartarusState? state)
    {
        if (!Valid(state)) return run;
        if (unverified && !Matches(run, state)) run = new TrackedRun();
        unverified = false;
        if (run.Size == 0 || NewRun(state)) Begin(state);
        started = true;
        Track(state);
        if (seconds() - lastSave >= 5) Save();
        return run;
    }

    public void Save()
    {
        if (!started) return;
        lastSave = seconds();
        try
        {
            var text = JsonSerializer.Serialize(run);
            if (text == lastWritten) return;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path + ".tmp", text);
            File.Move(path + ".tmp", path, true);
            lastWritten = text;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            try { File.Delete(path + ".tmp"); } catch (Exception) { }
        }
    }

    public void Dispose() => Save();

    static bool Valid([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] TartarusState? state) =>
        state is { Cells: not null, Instruments: not null } && state.Size is > 0 and <= 64 && state.Cells.Length == state.Size && state.Day is >= 1 and <= 100;

    static bool Matches(TrackedRun saved, TartarusState state) =>
        saved.Day <= state.Day && saved.Size == state.Size && saved.SeenCells.All(cell => cell >= 0 && cell < state.Size) && saved.ToolsHeld.All(state.Instruments.Contains);

    bool NewRun(TartarusState state) =>
        state.Day < run.Day || (state.Day == 1 && run.SeenCells.Count > 0 && state.Cells.All(cell => !cell.Open && cell.Tier <= 0));

    void Begin(TartarusState state)
    {
        run = new TrackedRun { Day = state.Day, Size = state.Size };
        if (state.Day == 1) return;
        run.Assumed =
        [
            $"Joined on day {state.Day}, so the first {state.Day - 1} days were not seen.",
            "Tools picked earlier are taken from the ones held now.",
            "Scratch marks are assumed to be none.",
            "The swap is assumed unused today unless it was seen.",
        ];
    }

    void Track(TartarusState state)
    {
        if (state.Day > run.Day) run.SwapUsedToday = false;
        run.Day = state.Day;
        run.ToolsHeld = [.. run.ToolsHeld.Union(state.Instruments).Order()];
        run.SeenCells.UnionWith(state.Cells.Where(cell => cell.Open || cell.Tier >= 0).Select(cell => cell.Index).Where(index => index >= 0 && index < state.Size));
        if (run.ChainArmedAt >= 0 && state.Position != run.ChainArmedAt) run.ChainArmedAt = -1;
        if (state.Choice is { Kind: TartarusChoiceKind.Event, Options: not null } happening && happening.Options.Any(option => option.Param == ChainReactionId)) run.ChainArmedAt = state.Position;
        if (state.Choice is { Kind: TartarusChoiceKind.Event, Options: not null } usurping && usurping.Options.Select(option => option.Param).Where(id => id is >= UsurpationFirst and <= UsurpationLast).DefaultIfEmpty().Max() is var level and > 0)
            run.Usurped = Math.Max(run.Usurped, level);
        var marked = state.Cells.Where(cell => cell.EventMarker).Select(cell => cell.Index).ToArray();
        if (run.Usurped is > 0 and < UsurpationLast && run.UsurpNext <= run.Usurped && marked.Except(run.Marked ?? []).ToArray() is [var placed])
        {
            run.UsurpCell = placed;
            run.UsurpNext = run.Usurped + 1;
        }
        run.Marked = marked;
        if (state.Choice is { Kind: TartarusChoiceKind.Operation, Complete: true, Options: not null } operation)
            run.SwapUsedToday = operation.Options.All(option => option.Kind != TartarusOptionKind.Replace);
    }

    TrackedRun? Load()
    {
        try
        {
            if (!File.Exists(path)) return null;
            var saved = JsonSerializer.Deserialize<TrackedRun>(File.ReadAllText(path));
            return saved is { Assumed: not null, ToolsHeld: not null, SeenCells: not null } ? saved : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or NotSupportedException) { return null; }
    }
}
