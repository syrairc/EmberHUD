using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using EmberHUD;

namespace MouseLab;

public sealed record MoveResult(string Scenario, string Label, PathScores Scores, Verdict Verdict, IReadOnlyList<PathSample> Trail);

public sealed record RunResult(string Preset, IReadOnlyList<MoveResult> Moves, IReadOnlyList<string> Skipped, bool Cancelled, string? Refused, string? Note = null);

public sealed class SuiteRunner(Func<Sampler> newSampler, Func<int, CancellationToken, Task> delay, Thresholds thresholds, Action<Step>? stepStarted = null, Func<ItemMenu?>? menu = null)
{
    const string HalfLockNote = "Stopped after one Lock click - the item may now be locked or unlocked, check it";
    const int MenuPollMs = 50;
    const int MenuTimeoutMs = 4000;
    const int AllowedTimeoutMs = 1500;

    const int SettleMs = 150;
    const int StepPauseMs = 250;
    const int ScenarioPauseMs = 500;

    public async Task<RunResult> RunAsync(string presetName, SuiteBuild build, HumanizerConfig config, CancellationToken cancel)
    {
        var moves = new List<MoveResult>();
        string? pending = null;
        RunResult Result(bool cancelled = false, string? refused = null) => new(presetName, moves, build.Skipped, cancelled, refused, pending);

        RunResult Stopped()
        {
            Send.ReleaseAll();
            return Result(cancelled: true);
        }

        RunResult Refused(string reason)
        {
            Send.ReleaseAll();
            return Result(refused: reason);
        }

        async Task<bool> SendAllowed()
        {
            for (var waited = 0; !Send.Allowed; waited += MenuPollMs)
            {
                if (waited >= AllowedTimeoutMs) return false;
                await delay(MenuPollMs, cancel);
            }
            return true;
        }

        var overlapsTakeOut = false;

        ScreenRect? OptionRect(MenuOption option)
        {
            var current = (menu ?? (() => Game.State.ItemMenu))();
            var rect = option == MenuOption.More ? current?.More : current?.Lock;
            overlapsTakeOut = rect is { } chosen && current?.TakeOut is { } takeOut && Overlap(chosen, takeOut);
            return overlapsTakeOut ? null : rect;
        }

        string Gone(string reason) => overlapsTakeOut ? "The menu option overlaps Take Out, stopped" : reason;

        if (!Send.Allowed) return Result(refused: "Send is not allowed: game not focused or chat open");
        if (build.Scenarios.Count == 0) return Result(refused: "Nothing to run: the suite has no scenarios");

        using var sampler = newSampler();
        try
        {
            for (var s = 0; s < build.Scenarios.Count; s++)
            {
                if (s > 0) await delay(ScenarioPauseMs, cancel);
                var scenario = build.Scenarios[s];
                for (var i = 0; i < scenario.Steps.Count; i++)
                {
                    if (i > 0) await delay(StepPauseMs, cancel);
                    var step = scenario.Steps[i];
                    stepStarted?.Invoke(step);
                    var target = step.Target;
                    if (step.Kind == StepKind.Click)
                    {
                        if (!await SendAllowed() || !await Send.ClickAt(CentreX(target), CentreY(target), MouseButton.Left, MouseMode.Humanized, cancel, config) || cancel.IsCancellationRequested) return Stopped();
                        await delay(SettleMs, cancel);
                        continue;
                    }
                    if (step.Kind == StepKind.MenuClick)
                    {
                        ScreenRect? shown = null;
                        for (var waited = 0; waited < MenuTimeoutMs && (shown = OptionRect(step.Option)) is null && !overlapsTakeOut; waited += MenuPollMs) await delay(MenuPollMs, cancel);
                        if (shown is not { } found) return Refused(Gone($"The item menu did not show within {MenuTimeoutMs} ms, stopped"));
                        target = found;
                    }
                    if (step.Kind == StepKind.MenuClick && !await SendAllowed()) return Stopped();
                    sampler.Start();
                    var moved = await Send.MoveTo(CentreX(target), CentreY(target), MouseMode.Humanized, cancel, config);
                    if (moved) await delay(SettleMs, cancel);
                    var trail = sampler.Stop();
                    if (!moved || cancel.IsCancellationRequested) return Stopped();
                    if (step.Kind == StepKind.MenuClick)
                    {
                        var clicks = step.Option == MenuOption.Lock ? 2 : 1;
                        for (var click = 0; click < clicks; click++)
                        {
                            if (click > 0) await delay(StepPauseMs, cancel);
                            if (!await SendAllowed()) return Stopped();
                            if (OptionRect(step.Option) is not { } fresh) return Refused(Gone("The item menu went away mid click, stopped"));
                            if (clicks > 1 && click == 0) pending = HalfLockNote;
                            var landed = await Send.ClickAt(CentreX(fresh), CentreY(fresh), MouseButton.Left, MouseMode.Humanized, cancel, config);
                            if (!landed || cancel.IsCancellationRequested) return Stopped();
                        }
                        pending = null;
                    }
                    if (!step.Scored) continue;
                    if (Send.ClientToScreen((int)target.Min.X, (int)target.Min.Y) is not var (minX, minY) || Send.ClientToScreen((int)target.Max.X, (int)target.Max.Y) is not var (maxX, maxY)) return Stopped();
                    var scores = PathMetrics.Measure(trail, new MetricsRect(minX, minY, maxX, maxY));
                    moves.Add(new MoveResult(scenario.Name, step.Label, scores, Scoring.Judge(scores, thresholds), trail));
                }
            }
        }
        catch (OperationCanceledException)
        {
            return Stopped();
        }
        return Result();
    }

    static bool Overlap(ScreenRect a, ScreenRect b) => a.Min.X < b.Max.X && b.Min.X < a.Max.X && a.Min.Y < b.Max.Y && b.Min.Y < a.Max.Y;

    static int CentreX(ScreenRect rect) => (int)((rect.Min.X + rect.Max.X) / 2);

    static int CentreY(ScreenRect rect) => (int)((rect.Min.Y + rect.Max.Y) / 2);
}
