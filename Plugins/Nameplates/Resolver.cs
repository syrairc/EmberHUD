using System;
using EmberHUD;

namespace NameplatesPlugin;

public static class Resolver
{
    public static Bucket? BucketOf(in Entity entity)
    {
        if (entity.Hidden) return null;
        switch (entity.Kind)
        {
            case EntityKind.Monster when entity.Friendly:
            case EntityKind.Ally:
            case EntityKind.Player:
                return Bucket.Ally;
            case EntityKind.Monster:
                return entity.Rarity switch
                {
                    Rarity.Magic => Bucket.Magic,
                    Rarity.Rare => Bucket.Rare,
                    Rarity.Boss => Bucket.Boss,
                    _ => Bucket.Normal,
                };
            case EntityKind.Npc:
                return Bucket.Npc;
            default:
                return null;
        }
    }

    public static Entity? BossOnBar(Entity[] entities, float percent) => GameState.BossOnBar(entities, percent);

    public static PlateStyle? Resolve(Settings settings, in Entity entity, float distance)
    {
        var bucket = BucketOf(entity);
        return bucket is null ? null : ResolveBucket(settings, bucket.Value, entity.Name, entity.CfgId, entity.Life, entity.MaxLife, distance);
    }

    public static PlateStyle? ResolveBucket(Settings settings, Bucket bucket, string name, int cfgId, double life, double maxLife, float distance)
    {
        var bucketSettings = settings.Buckets[(int)bucket];
        if (!bucketSettings.Show || !(distance <= settings.MaxDistance)) return null;
        if (bucketSettings.DamagedOnly && (!(maxLife > 0) || !(life < maxLife))) return null;

        var overrides = settings.Overrides;
        for (var i = 0; i < overrides.Count; i++)
        {
            var candidate = overrides[i];
            if (!candidate.Enabled) continue;
            var matches = candidate.Kind == MatchKind.Name
                ? candidate.Name.Length > 0 && string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase)
                : candidate.CfgId != 0 && candidate.CfgId == cfgId;
            if (matches) return candidate.Style;
        }
        return bucketSettings.Style;
    }
}
