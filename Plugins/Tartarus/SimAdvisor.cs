using System;
using System.Globalization;
using System.Linq;
using EmberHUD;
using Tartarus.Sim;

namespace Tartarus;

public readonly record struct ValueRange(double Low, double High);

public sealed record OptionAdvice(int OptionId, ValueRange Value, bool Glow, string Reason, bool Top = false);

public sealed record Advice(OptionAdvice[] Options, string Summary);

public static class SimAdvisor
{
    public static Advice Advise(TartarusState state, MapResult map, Ranked[]? ranked, Settings settings, Move? instant = null)
    {
        if (state?.Choice is not { Options: { } options } || map is null) return new Advice([], "");
        var found = options.Select(option => map.Run is null ? null : ranked?.FirstOrDefault(entry => Matches(entry.Move, option))).ToArray();
        var glow = new bool[options.Length];
        var core = Core(map, ranked, found, glow, options);
        int? top = glow.Any(on => on) ? Array.IndexOf(found, Pick(ranked!)) : null;
        if (ranked is null && instant is { } early && core == "thinking" && Array.FindIndex(options, option => Matches(early, option)) is var guess and >= 0) glow[guess] = true;
        if (ranked is null && glow.Any(on => on)) top = Array.IndexOf(glow, true);
        var advice = options.Select((option, i) => found[i] is { } entry
            ? new OptionAdvice(option.Id, new ValueRange(entry.Value - entry.Spread, entry.Value + entry.Spread), glow[i], entry.Value.ToString("0.00", CultureInfo.InvariantCulture), top == i)
            : new OptionAdvice(option.Id, default, glow[i], "", top == i)).ToArray();
        var notes = settings.ShowAssumed && map.Assumed.Length > 0 ? $"assumed: {string.Join('\n', map.Assumed)}\n" : "";
        return new Advice(advice, notes + core);
    }

    const double SwitchMin = 0.05;

    static bool Close(Ranked best, Ranked other) => best.Value - other.Value <= Math.Max(1.96 * other.GapSe, SwitchMin);

    static Ranked Pick(Ranked[] ranked) => ranked.FirstOrDefault(entry => entry.Strategy) is { } mine && Close(ranked[0], mine) ? mine : ranked[0];

    static string Core(MapResult map, Ranked[]? ranked, Ranked?[] found, bool[] glow, TartarusOption[] options)
    {
        if (map.Mismatch is not null) return $"mismatch: {map.Mismatch}";
        if (map.Unsupported is not null) return map.Unsupported;
        if (map.Run is null || ranked is null) return "thinking";
        var simulated = Enumerable.Range(0, found.Length).Where(i => options[i].Kind != TartarusOptionKind.Other).ToArray();
        if (simulated.Length == 0 || simulated.Any(i => found[i] is null)) return "unsure: not every option is simulated";

        if (simulated.Length == 1) return "only option";

        var pick = Pick(ranked);
        foreach (var i in simulated) glow[i] = found[i] == pick || Close(ranked[0], found[i]!);
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
