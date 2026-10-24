using System;
using System.Text.RegularExpressions;
using EmberHUD;

namespace EmberAuras;

public static class Conditions
{
    public static bool AllMet(Aura aura, GameState state)
    {
        if (aura.Conditions is not { Count: > 0 } conditions) return false;
        foreach (var condition in conditions)
            if (condition is null || !Met(condition, state)) return false;
        return true;
    }

    public static bool Met(Condition c, GameState state) => c.Kind switch
    {
        ConditionKind.Health => state.Player is { } p && Pool(p.Life, p.MaxLife, c),
        ConditionKind.Shield => state.Player is { } p && Pool(p.Shield, p.MaxShield, c),
        ConditionKind.HealthAndShield => state.Player is { } p && Pool(p.Life + p.Shield, p.MaxLife + p.MaxShield, c),
        ConditionKind.Buff => BuffMet(c, state),
        ConditionKind.SkillCooldown => FindSkill(state, c.SkillCfgId) is { } skill && skill.Ready == c.Ready,
        ConditionKind.SkillCharges => FindSkill(state, c.SkillCfgId) is { MaxCharges: > 0 } skill && skill.Charges >= c.Charges,
        _ => false,
    };

    // untimed buffs read 1.0 / 1.0 forever, so Ending needs a match that is actually ticking down, gone counts as 0 s left
    static bool BuffMet(Condition condition, GameState state)
    {
        if (string.IsNullOrEmpty(condition.Text) || state.Buffs is not { } buffs) return false;
        var found = false;
        var timed = false;
        var stacked = condition.MinStacks <= 0;
        var longest = 0.0;
        foreach (var buff in buffs)
        {
            if (!BuffMatches(condition.Text, buff)) continue;
            found = true;
            timed |= buff.Remaining < buff.Total;
            stacked |= buff.Stacks >= condition.MinStacks;
            longest = Math.Max(longest, buff.Remaining);
        }
        return condition.Ending ? !found || timed && longest < condition.Seconds : found != condition.Missing && (condition.Missing || stacked);
    }

    public static bool BuffMatches(string text, Buff buff)
    {
        if (string.IsNullOrEmpty(text)) return false;
        var name = buff.Name ?? "";
        var id = buff.Id.ToString();
        try
        {
            const RegexOptions options = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;
            var timeout = TimeSpan.FromMilliseconds(50);
            return Regex.IsMatch(name, text, options, timeout) || Regex.IsMatch(id, text, options, timeout);
        }
        catch (Exception e) when (e is ArgumentException or RegexMatchTimeoutException)
        {
            return name.Equals(text, StringComparison.OrdinalIgnoreCase) || id.Equals(text, StringComparison.OrdinalIgnoreCase);
        }
    }

    static bool Pool(double value, double max, Condition c)
    {
        if (max <= 0) return false;
        var percent = value / max * 100;
        return c.Direction == Direction.Above ? percent >= c.Percent : percent < c.Percent;
    }

    public static Skill? FindSkill(GameState state, int cfgId)
    {
        if (state.Skills is not { } skills) return null;
        foreach (var skill in skills)
            if (skill.CfgId == cfgId && skill.Slot >= 0) return skill;
        return null;
    }
}
