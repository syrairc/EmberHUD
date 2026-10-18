using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using EmberHUD;

namespace Pickit;

static class Gems
{
    const float GroundSize = 0.33f;

    public readonly record struct Gem(Vector2 Centre, float Radius, int Tier, bool Prefix);

    public static Gem[] Layout(GroundItem item, Settings s)
    {
        var height = item.LabelMax.Y - item.LabelMin.Y;
        if (item.LabelMax.X <= item.LabelMin.X || height <= 0) return [];
        var gems = new List<Gem>();
        Side(item.PrefixIds, true, item.LabelMin.X + s.InsetX, 1);
        Side(item.SuffixIds, false, item.LabelMax.X - s.InsetX, -1);
        return [.. gems];

        void Side(int[] ids, bool prefix, float x, float direction)
        {
            var ordered = ids.OrderBy(GroundItem.Tier);
            foreach (var id in ordered.Take(GroundItem.MaxAffixes))
            {
                var tier = GroundItem.Tier(id);
                var radius = GroundSize * height / 2 * s.GlobalScale * s.GroundScale * s.Tiers[Math.Clamp(tier, 0, s.Tiers.Count - 1)].Scale;
                gems.Add(new Gem(new Vector2(x, item.LabelMin.Y + s.AnchorY * height), radius, tier, prefix));
                x += direction * radius * 2 * (1 + s.GemSpacing);
            }
        }
    }

    public static Gem[] CellLayout(Vector2 cellMin, Vector2 cellMax, int[] prefixIds, int[] suffixIds, Settings s)
    {
        var height = cellMax.Y - cellMin.Y;
        if (cellMax.X <= cellMin.X || height <= 0) return [];
        var gems = new List<Gem>();
        Side(prefixIds, true);
        Side(suffixIds, false);
        return [.. gems];

        void Side(int[] ids, bool prefix)
        {
            var tiers = ids.Select(GroundItem.Tier).Order().Take(GroundItem.MaxAffixes).ToArray();
            var radii = tiers.Select(t => 0.16f * height / 2 * s.GlobalScale * s.InventoryScale * s.Tiers[Math.Clamp(t, 0, s.Tiers.Count - 1)].Scale).ToArray();
            var y = 0f;
            var ys = new float[tiers.Length];
            for (var i = 0; i < tiers.Length; i++)
            {
                ys[i] = y;
                y += radii[i] * 2 * (1 + s.GemSpacing);
            }
            var shift = cellMin.Y + height / 2 - (tiers.Length == 0 ? 0 : (ys[0] + ys[^1]) / 2);
            for (var i = 0; i < tiers.Length; i++)
            {
                var x = prefix ? cellMin.X + radii[i] + s.InsetX : cellMax.X - radii[i] - s.InsetX;
                gems.Add(new Gem(new Vector2(x, ys[i] + shift), radii[i], tiers[i], prefix));
            }
        }
    }
}
