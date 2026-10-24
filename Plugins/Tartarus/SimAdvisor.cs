using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using EmberHUD;
using Tartarus.Sim;

namespace Tartarus;

public readonly record struct ValueRange(double Low, double High);

public sealed record OptionAdvice(int OptionId, ValueRange Value, bool Glow, string Reason, bool Top = false);

public sealed record Advice(OptionAdvice[] Options, string Summary, string Source = "");

public static class SimAdvisor
{
    public static Advice Advise(TartarusState state, MapResult map, Ranked[]? ranked, Settings settings, Move? instant = null, IReadOnlyList<Strategy>? strategies = null)
    {
        if (state?.Choice is not { Options: { } options } || map is null) return new Advice([], "");
        var found = options.Select(option => map.Run is null ? null : ranked?.FirstOrDefault(entry => Matches(entry.Move, option))).ToArray();
        var glow = new bool[options.Length];
        Resolved? resolved = null;
        Resolved Resolve() => resolved ??= StrategyEngine.Resolve(settings.Mode, map.Run, ranked, instant, strategies);
        var core = Core(map, ranked, found, glow, options, settings, Resolve);
        int? top = null;
        if (core == "thinking" && ranked is null && Resolve().Move is { } guess && Array.FindIndex(options, option => Matches(guess, option)) is var at and >= 0) glow[at] = true;
        if (glow.Any(on => on)) top = ranked is null ? Array.IndexOf(glow, true) : Array.FindIndex(found, entry => entry is not null && entry.Move == resolved?.Move);
        var advice = options.Select((option, i) => found[i] is { } entry
            ? new OptionAdvice(option.Id, new ValueRange(entry.Value - entry.Spread, entry.Value + entry.Spread), glow[i], entry.Value.ToString("0.00", CultureInfo.InvariantCulture), top == i)
            : new OptionAdvice(option.Id, default, glow[i], "", top == i)).ToArray();
        var notes = settings.ShowAssumed && map.Assumed.Length > 0 ? $"assumed: {string.Join('\n', map.Assumed)}\n" : "";
        return new Advice(advice, notes + core, resolved is { Move: not null } ? resolved.Source : "");
    }

    const double SwitchMin = 0.05;

    internal static bool Close(Ranked best, Ranked other) => best.Value - other.Value <= Math.Max(1.96 * other.GapSe, SwitchMin);

    static string Core(MapResult map, Ranked[]? ranked, Ranked?[] found, bool[] glow, TartarusOption[] options, Settings settings, Func<Resolved> resolve)
    {
        if (map.Mismatch is not null) return $"mismatch: {map.Mismatch}";
        if (map.Unsupported is not null) return map.Unsupported;
        if (map.Run is null || ranked is null) return "thinking";
        var simulated = Enumerable.Range(0, found.Length).Where(i => options[i].Kind != TartarusOptionKind.Other).ToArray();
        if (simulated.Length == 0 || simulated.Any(i => found[i] is null)) return "unsure: not every option is simulated";

        if (simulated.Length == 1) return "only option";

        var pick = resolve().Move;
        foreach (var i in simulated) glow[i] = found[i]?.Move == pick || !settings.OnlyBest && Close(ranked[0], found[i]!);
        return $"{glow.Count(on => on)} best";
    }

    static bool Matches(Move move, TartarusOption option) => option.Kind switch
    {
        TartarusOptionKind.Door => move.Kind == MoveKind.Door && move.Cell == option.Cell,
        TartarusOptionKind.Consume => move.Kind == MoveKind.Consume && move.Cell == option.Cell,
        TartarusOptionKind.Replace => move.Kind == MoveKind.Replace,
        TartarusOptionKind.End => move.Kind == MoveKind.End,
        TartarusOptionKind.StopChain => move.Kind == MoveKind.StopChain,
        TartarusOptionKind.Instigate => move.Kind == MoveKind.Instigate,
        TartarusOptionKind.Bestow => move.Kind == MoveKind.Bestow,
        TartarusOptionKind.Instrument => move.Kind == MoveKind.Instrument && StateMapper.ToolOf(option.Param) is { } tool && move.Param == (int)tool,
        TartarusOptionKind.Reward => move.Kind == MoveKind.Reward && move.Param == option.Param - 1,
        _ => false,
    };
}
