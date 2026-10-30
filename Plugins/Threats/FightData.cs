using System;
using System.Collections.Generic;
using System.Numerics;
using EmberHUD;

namespace Threats;

public static class FightData
{
    const double Limit = 1e7;
    const float PlayerReach = 50;

    static bool Sane(double value) => double.IsFinite(value) && Math.Abs(value) < Limit;

    static bool Sane(Vector3 value) => Sane(value.X) && Sane(value.Y) && Sane(value.Z);

    public static void Index(FightEntity[]? fighters, Dictionary<int, FightEntity> into)
    {
        into.Clear();
        if (fighters is null) return;
        foreach (var fighter in fighters) into[fighter.Id] = fighter;
    }

    public static float? LineLength(in Projectile projectile, FightEntity? fighter, Settings settings)
    {
        if (fighter is not { Bullet: { } bullet, Motion: { } motion } || !Sane(bullet.Range) || bullet.Range <= 0 || !Sane(motion.Travelled)) return null;
        var length = Math.Min(float.IsFinite(settings.LineLength) ? Math.Max(settings.LineLength, 0) : 0, bullet.Range - motion.Travelled);
        if (length <= 0) return 0;
        if (fighter.Lifetime is { } lifetime && Sane(lifetime) && lifetime > 0 && Sane(motion.Speed) && motion.Speed > 0) length = Math.Min(length, lifetime * motion.Speed);
        return (float)length;
    }

    public static (Vector2 Start, Vector2 End)? Segment(in Projectile projectile, FightEntity? fighter, Settings settings)
    {
        if (fighter?.Beam is not { } beam || !Sane(beam.Start) || !Sane(beam.End)) return null;
        var start = new Vector2(beam.Start.X, beam.Start.Y);
        var end = new Vector2(beam.End.X, beam.End.Y);
        return Vector2.Distance(start, end) > 1 ? (start, end) : null;
    }

    public static Vector2? OrbitCentre(in Projectile projectile, FightEntity? fighter, Entity[] entities)
    {
        if (fighter?.Command is not { } command) return projectile.OrbitCentre;
        if (command.Type != MoveCommandType.Circle) return null;
        foreach (var entity in entities)
        {
            if (entity.Id != projectile.OwnerId) continue;
            return Sane(entity.Position) ? new Vector2(entity.Position.X, entity.Position.Y) : projectile.OrbitCentre;
        }
        return projectile.OrbitCentre;
    }

    public static bool Homing(FightEntity? fighter) => fighter?.Track is { HasTarget: true };

    public static int PlayerFighterId(FightEntity[]? fighters, Vector2 player)
    {
        if (fighters is null || !float.IsFinite(player.X) || !float.IsFinite(player.Y)) return 0;
        var best = 0;
        var nearest = PlayerReach;
        foreach (var fighter in fighters)
        {
            if (fighter.Archetype != FightArchetype.Player || !Sane(fighter.Position)) continue;
            var distance = Vector2.Distance(player, new Vector2(fighter.Position.X, fighter.Position.Y));
            if (distance > nearest) continue;
            nearest = distance;
            best = fighter.Id;
        }
        return best;
    }

    public static double? HazardRemaining(FightEntity? fighter)
    {
        if (fighter is null) return null;
        if (fighter.Timed is { } timed && Sane(timed.Duration) && Sane(timed.Elapsed) && timed.Duration > 0 && timed.Elapsed >= 0) return timed.Duration - timed.Elapsed;
        return fighter.Lifetime is { } lifetime && Sane(lifetime) ? lifetime : null;
    }
}
