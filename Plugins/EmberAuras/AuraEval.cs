using System;
using System.Globalization;
using EmberHUD;

namespace EmberAuras;

public enum Visibility { Hidden, Dim, Shown }

public sealed record AuraView(Visibility Visibility, float Fill, int? BuffId, int? SkillCfgId, string Name, int Stacks, double? Seconds, int Charges, int MaxCharges, float Percent)
{
    public bool HasPercent { get; init; }

    public string? TextFor(TextField field) => field switch
    {
        TextField.Name => Name.Length > 0 ? Name : null,
        TextField.Stacks => Stacks > 1 ? Stacks.ToString(CultureInfo.InvariantCulture) : null,
        TextField.Duration => Seconds is { } s ? s.ToString(s < 10 ? "0.0" : "0", CultureInfo.InvariantCulture) : null,
        TextField.Charges => MaxCharges > 0 ? $"{Charges}/{MaxCharges}" : null,
        TextField.Percent => HasPercent ? $"{Math.Round(Percent).ToString(CultureInfo.InvariantCulture)}%" : null,
        _ => null,
    };
}

public static class AuraEval
{
    static readonly AuraView Empty = new(Visibility.Hidden, 0, null, null, "", 0, null, 0, 0, 0);

    public static AuraView Evaluate(Aura aura, GameState state)
    {
        if (!aura.Enabled) return Empty;
        var visibility = Conditions.AllMet(aura, state) ? Visibility.Shown : aura.WhenInactive == WhenInactive.Dim ? Visibility.Dim : Visibility.Hidden;
        var view = aura.Conditions is { Count: > 0 } && aura.Conditions[0] is { } primary ? Values(primary, state) : Empty;
        return view with { Visibility = visibility, Name = aura.Name.Length > 0 ? aura.Name : view.Name };
    }

    static AuraView Values(Condition c, GameState state) => c.Kind switch
    {
        ConditionKind.Buff => BuffValues(c, state),
        ConditionKind.SkillCooldown or ConditionKind.SkillCharges => SkillValues(c, state),
        ConditionKind.Health => state.Player is { } p ? PoolValues(p.Life, p.MaxLife) : Empty,
        ConditionKind.Shield => state.Player is { } p ? PoolValues(p.Shield, p.MaxShield) : Empty,
        ConditionKind.HealthAndShield => state.Player is { } p ? PoolValues(p.Life + p.Shield, p.MaxLife + p.MaxShield) : Empty,
        _ => Empty,
    };

    static AuraView BuffValues(Condition c, GameState state)
    {
        if (state.Buffs is not { } buffs) return Empty;
        foreach (var buff in buffs)
        {
            if (!Conditions.BuffMatches(c.Text, buff)) continue;
            var timed = buff.Remaining < buff.Total;
            var fill = double.IsNaN(buff.Remaining) || !(buff.Total > 0) ? 0 : timed ? buff.Remaining / buff.Total : 1;
            return Empty with { Fill = Clamp(fill), BuffId = buff.Id, Name = buff.Name ?? "", Stacks = buff.Stacks, Seconds = timed ? buff.Remaining : null };
        }
        return Empty;
    }

    static AuraView SkillValues(Condition c, GameState state)
    {
        if (Conditions.FindSkill(state, c.SkillCfgId) is not { } skill) return Empty;
        var cooling = skill.CooldownTotal > 0;
        var fill = c.Kind == ConditionKind.SkillCharges
            ? skill.MaxCharges > 0 ? (double)skill.Charges / skill.MaxCharges : 0
            : cooling ? 1 - skill.CooldownRemaining / skill.CooldownTotal : 1;
        return Empty with
        {
            Fill = Clamp(fill),
            SkillCfgId = skill.CfgId,
            Name = skill.Name ?? "",
            Seconds = cooling && skill.CooldownRemaining > 0 ? skill.CooldownRemaining : null,
            Charges = skill.Charges,
            MaxCharges = skill.MaxCharges,
        };
    }

    static AuraView PoolValues(double value, double max)
    {
        var fraction = max > 0 ? Clamp(value / max) : 0;
        return Empty with { Fill = fraction, Percent = fraction * 100, HasPercent = true };
    }

    static float Clamp(double value) => double.IsNaN(value) ? 0 : (float)Math.Clamp(value, 0, 1);
}
