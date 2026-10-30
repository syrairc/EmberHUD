using System;
using System.Numerics;
using System.Text.RegularExpressions;
using EmberHUD;

namespace Reactions;

public static class Conditions
{
    public static bool AllMet(Reaction reaction, GameState state)
    {
        if (reaction.Conditions is not { Count: > 0 } conditions) return false;
        foreach (var condition in conditions)
            if (condition is null || !Met(condition, state)) return false;
        return true;
    }

    public static bool Met(Condition condition, GameState state) => condition.Kind switch
    {
        ConditionKind.Health => state.Player is { } p && Below(p.Life, p.MaxLife, condition.Percent),
        ConditionKind.Shield => state.Player is { } p && Below(p.Shield, p.MaxShield, condition.Percent),
        ConditionKind.HealthAndShield => state.Player is { } p && Below(p.Life + p.Shield, p.MaxLife + p.MaxShield, condition.Percent),
        ConditionKind.Buff => BuffMet(condition, state),
        ConditionKind.SkillCooldown => FindSkill(state, condition.SkillCfgId) is { } skill && skill.Ready == condition.Ready,
        ConditionKind.SkillCharges => FindSkill(state, condition.SkillCfgId) is { MaxCharges: > 0 } skill && skill.Charges >= condition.Charges,
        ConditionKind.NearbyEnemies => state.CountNear(condition.Radius, condition.MinRarity) >= Math.Max(condition.Count, 1),
        ConditionKind.HomingProjectile => HomingMet(condition, state),
        ConditionKind.GroundEffect => GroundEffectMet(condition, state),
        ConditionKind.CrowdControlled => (state.Control & condition.Control) != 0,
        _ => false,
    };

    public static string Describe(Condition condition, GameState state)
    {
        var detail = condition.Kind switch
        {
            ConditionKind.NearbyEnemies => $" r{condition.Radius} min {condition.MinRarity} need {Math.Max(condition.Count, 1)} got {state.CountNear(condition.Radius, condition.MinRarity)}",
            ConditionKind.SkillCooldown or ConditionKind.SkillCharges => FindSkill(state, condition.SkillCfgId) is { } skill ? $" {condition.SkillCfgId} ready={skill.Ready} cd={skill.CooldownRemaining:0.0}/{skill.CooldownTotal:0.0} charges={skill.Charges}" : $" {condition.SkillCfgId} missing",
            ConditionKind.Buff => $" '{condition.Text}'",
            ConditionKind.HomingProjectile => $" r{condition.Radius} need {Math.Max(condition.Count, 1)} got {HomingCount(condition, state)}{(condition.OnlyTargetingMe ? " at me" : "")}",
            ConditionKind.GroundEffect => $"{(condition.DamageOnly ? " damage only" : "")}",
            ConditionKind.CrowdControlled => $" want {condition.Control} got {state.Control}",
            _ => "",
        };
        return $"{condition.Kind}{detail} {(Met(condition, state) ? "ok" : "FAIL")}";
    }

    // untimed buffs read 1.0 / 1.0 forever, so Ending needs a match that is actually ticking down, gone counts as 0 s left
    static bool BuffMet(Condition condition, GameState state)
    {
        if (string.IsNullOrEmpty(condition.Text) || state.Buffs is not { } buffs) return false;
        var found = false;
        var timed = false;
        var longest = 0.0;
        foreach (var buff in buffs)
        {
            if (!BuffMatches(condition.Text, buff)) continue;
            found = true;
            timed |= buff.Remaining < buff.Total;
            longest = Math.Max(longest, buff.Remaining);
        }
        return condition.Ending ? !found || timed && longest < condition.Seconds : found != condition.Missing;
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

    static bool HomingMet(Condition condition, GameState state) => HomingCount(condition, state) >= Math.Max(condition.Count, 1);

    static int HomingCount(Condition condition, GameState state)
    {
        if (state.Player is not { } player || float.IsNaN(condition.Radius) || condition.Radius < 0) return 0;
        if (state.Fighters is not { } fighters) return 0;
        var projectiles = state.Projectiles;
        var playerId = 0;
        if (condition.OnlyTargetingMe && (playerId = PlayerFighterId(fighters, new Vector2(player.Position.X, player.Position.Y))) == 0) return 0;

        var reach = condition.Radius * condition.Radius;
        var count = 0;
        foreach (var projectile in projectiles)
        {
            if (Vector2.DistanceSquared(projectile.Position, new Vector2(player.Position.X, player.Position.Y)) > reach) continue;
            foreach (var fighter in fighters)
            {
                if (fighter.Id != projectile.Id) continue;
                if (fighter.Track is { HasTarget: true } track && (!condition.OnlyTargetingMe || track.TargetId == playerId)) count++;
                break;
            }
        }
        return count;
    }

    const float PlayerReach = 50;

    static int PlayerFighterId(FightEntity[] fighters, Vector2 player)
    {
        var best = 0;
        var nearest = PlayerReach;
        foreach (var fighter in fighters)
        {
            if (fighter.Archetype != FightArchetype.Player) continue;
            var distance = Vector2.Distance(player, new Vector2(fighter.Position.X, fighter.Position.Y));
            if (!(distance <= nearest)) continue;
            nearest = distance;
            best = fighter.Id;
        }
        return best;
    }

    static bool GroundEffectMet(Condition condition, GameState state)
    {
        if (state.Player is not { } player) return false;
        var position = new Vector2(player.Position.X, player.Position.Y);
        foreach (var hazard in state.GroundHazards)
        {
            if (condition.DamageOnly && hazard.Kind != HazardKind.Damage) continue;
            var distance = Vector2.Distance(position, hazard.Centre);
            if (distance <= hazard.Radius && distance >= hazard.InnerRadius) return true;
        }
        return false;
    }

    static bool Below(double value, double max, float percent) => max > 0 && value / max * 100 < percent;

    internal static Skill? FindSkill(GameState state, int cfgId)
    {
        if (state.Skills is not { } skills) return null;
        foreach (var skill in skills)
            if (skill.CfgId == cfgId && skill.Slot >= 0) return skill;
        return null;
    }
}
