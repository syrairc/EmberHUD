using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EmberHUD;

namespace MouseLab;

public sealed class Main : Plugin<Settings>
{
    readonly Panel _panel = new();
    CancellationTokenSource? _cancel;
    volatile RunResult[] _results = [];
    volatile string? _running;
    volatile string? _note;
    volatile Step? _step;
    long _finishedAt;

    internal string OutputDirectory { get; init; } = Path.Combine(AppContext.BaseDirectory, "Config", "MouseLab");

    internal Func<int, CancellationToken, Task> Delay { get; init; } = Task.Delay;

    internal Task Finished { get; private set; } = Task.CompletedTask;

    internal bool Running => !Finished.IsCompleted;

    internal IReadOnlyList<RunResult> Results => _results;

    internal string? Note => _note;

    public override void Initialize()
    {
        RegisterHotkey("Run", "Runs the mouse suite on the stash with the preset from the settings, press again to cancel", () => Toggle(false), new Hotkey(Key.F4, KeyModifiers.Ctrl));
        RegisterHotkey("RunAll", "Runs the mouse suite once per named preset and compares them, press again to cancel", () => Toggle(true), new Hotkey(Key.F4, KeyModifiers.Ctrl | KeyModifiers.Shift));
    }

    public override void Tick()
    {
        Game.WantInventory();
        if (Running || Game.State.Stash is not null) Game.WantItemMenu();
    }

    public override void OnDisable() => Stop();

    public override void Dispose() => Stop();

    public override void Render()
    {
        var results = _results;
        var running = _running;
        var note = _note;
        if (Running && _step is { } step) Panel.DrawTarget(step);
        else if (Settings.ShowTrail && results.LastOrDefault(result => result.Moves.Count > 0) is { } last && Environment.TickCount64 - Interlocked.Read(ref _finishedAt) < Settings.TrailSeconds * 1000L) Panel.DrawTrail(last);
        if (results.Length == 0 && running is null && note is null) return;
        _panel.Show(Settings, results, running, note);
    }

    internal static HumanizerPreset Humanizer(LabPreset preset) => Enum.Parse<HumanizerPreset>(preset.ToString());

    internal void Toggle(bool all)
    {
        if (Running)
        {
            _cancel?.Cancel();
            return;
        }
        if (Game.State.Stash is not { } stash)
        {
            _note = "No stash open - open the stash first";
            return;
        }
        var build = Suite.Build(stash, Game.State.Bag, menuOption: Settings.MenuOption);
        var presets = all ? Enum.GetValues<LabPreset>() : new[] { Settings.Preset };
        var thresholds = Settings.ToThresholds();
        _cancel = new CancellationTokenSource();
        var cancel = _cancel.Token;
        _results = [];
        _note = null;
        Interlocked.Exchange(ref _finishedAt, 0);
        Finished = Task.Run(() => RunAsync(build, presets, thresholds, cancel));
    }

    void Stop()
    {
        if (!Running) return;
        _cancel?.Cancel();
        Send.ReleaseAll();
    }

    async Task RunAsync(SuiteBuild build, LabPreset[] presets, Thresholds thresholds, CancellationToken cancel)
    {
        var done = new List<RunResult>();
        try
        {
            foreach (var preset in presets)
            {
                _running = preset.ToString();
                var runner = new SuiteRunner(NewSampler, Delay, thresholds, step => _step = step);
                var result = await runner.RunAsync(preset.ToString(), build, HumanizerPresets.Get(Humanizer(preset)), cancel);
                done.Add(result);
                _results = [.. done];
                if (result.Cancelled || result.Refused is not null) break;
            }
        }
        catch (Exception exception)
        {
            Log.Warn($"MouseLab run failed: {exception.Message}");
            _note = $"Run failed - {exception.Message}";
        }
        finally
        {
            Send.ReleaseAll();
            _running = null;
            _step = null;
            Interlocked.Exchange(ref _finishedAt, Environment.TickCount64);
        }
        if (done.Any(result => result.Refused is null || result.Moves.Count > 0)) Save(done);
    }

    void Save(List<RunResult> done)
    {
        try
        {
            _note = $"Saved {Path.GetFileName(RunWriter.Write(OutputDirectory, done, DateTimeOffset.Now))} in Config\\MouseLab";
        }
        catch (Exception exception)
        {
            Log.Warn($"MouseLab could not save the run: {exception.Message}");
            _note = $"Not saved - {exception.Message}";
        }
    }

    static Sampler NewSampler() => new(Send.CursorScreen, ClockMs, WaitMs);

    static double ClockMs() => Stopwatch.GetTimestamp() * 1000.0 / Stopwatch.Frequency;

    static void WaitMs(int milliseconds)
    {
        var end = Stopwatch.GetTimestamp() + Stopwatch.Frequency * milliseconds / 1000;
        while (Stopwatch.GetTimestamp() < end) Thread.Yield();
    }
}
