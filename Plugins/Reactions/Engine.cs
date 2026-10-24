using System;
using System.Collections.Generic;
using System.Linq;
using EmberHUD;

namespace Reactions;

public sealed class Engine
{
    const int MinCooldownMs = 50;

    const int MaxLog = 200;

    readonly Dictionary<Reaction, long> _readyAt = [];
    List<Reaction>? _list;
    Reaction? _refused;
    readonly Dictionary<Reaction, string> _diagnosis = [];
    string? _gate;
    long _globalReadyAt;

    public Reaction? Step(Settings settings, GameState state, long nowMs, Func<Key, bool> press)
    {
        if (!(state.Status.StartsWith("live") || state.Status == "fake game") || state.Paused != false || state.Ui.AnyFullScreen || state.Player is null)
        {
            if (settings.Debug) LogOnChange(settings, ref _gate, $"gate closed - status={state.Status} paused={state.Paused?.ToString() ?? "unknown"} fullScreen={state.Ui.AnyFullScreen} player={state.Player is not null}");
            return null;
        }
        _gate = null;
        if (nowMs < _globalReadyAt) return null;
        if (!ReferenceEquals(_list, settings.Reactions))
        {
            _list = settings.Reactions;
            _readyAt.Clear();
            _diagnosis.Clear();
        }
        foreach (var reaction in settings.Reactions)
        {
            if (!reaction.Enabled || reaction.Trigger.Key == Key.None) continue;
            if (_readyAt.TryGetValue(reaction, out var readyAt) && nowMs < readyAt) continue;
            if (!Conditions.AllMet(reaction, state))
            {
                if (settings.Debug) Diagnose(settings, reaction, state);
                continue;
            }
            _diagnosis.Remove(reaction);
            if (!press(reaction.Trigger.Key))
            {
                if (!ReferenceEquals(_refused, reaction)) Log(settings, $"{reaction.Name}: {reaction.Trigger.Key} refused - game not focused or chat open");
                _refused = reaction;
                continue;
            }
            _refused = null;
            Log(settings, $"{reaction.Name}: pressed {reaction.Trigger.Key}");
            _readyAt[reaction] = nowMs + Math.Max(reaction.CooldownMs, MinCooldownMs);
            _globalReadyAt = nowMs + Math.Max(settings.GlobalCooldownMs, MinCooldownMs);
            return reaction;
        }
        return null;
    }

    void Diagnose(Settings settings, Reaction reaction, GameState state)
    {
        var parts = reaction.Conditions.Count == 0 ? "no conditions" : string.Join("; ", reaction.Conditions.Where(condition => condition is not null).Select(condition => Conditions.Describe(condition, state)));
        var text = $"{reaction.Name}: not met - {parts}";
        _diagnosis.TryGetValue(reaction, out var last);
        LogOnChange(settings, ref last, text);
        _diagnosis[reaction] = last!;
    }

    static void LogOnChange(Settings settings, ref string? last, string line)
    {
        if (line == last) return;
        last = line;
        Log(settings, line);
    }

    static void Log(Settings settings, string line)
    {
        settings.Log.Add($"{DateTime.Now:HH:mm:ss.fff}  {line}");
        if (settings.Log.Count > MaxLog) settings.Log.RemoveRange(0, settings.Log.Count - MaxLog);
    }
}
