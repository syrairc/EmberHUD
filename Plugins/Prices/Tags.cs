using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using EmberHUD;

namespace Prices;

public readonly record struct Price(float Median, float? Low, float? High, DateTimeOffset? Updated = null, int? Submissions = null, int? Devices = null);

public enum Corner : byte { TopLeft, TopRight, BottomLeft, BottomRight }

public static class Tags
{
    const float Inset = 3;

    public static string? Format(double fe)
    {
        if (!(fe >= 0.05)) return null;
        var invariant = CultureInfo.InvariantCulture;
        if (fe < 10 && fe.ToString("0.0", invariant) is var tenths && tenths != "10.0") return tenths;
        if (fe < 1000 && fe.ToString("0", invariant) is var whole && whole != "1000") return whole;
        if (fe < 1e6 && (fe / 1000).ToString("0.#", invariant) is var thousands && thousands != "1000") return thousands + "k";
        return (fe / 1e6).ToString("0.#", invariant) + "M";
    }

    public static string? Text(InventoryItem item, IReadOnlyDictionary<int, Price> prices) => Text(item.BaseId, item.Count, item.Bag == InventoryBag.Equipment, prices);

    public static string? Text(int baseId, int count, bool gear, IReadOnlyDictionary<int, Price> prices)
    {
        return prices.TryGetValue(baseId, out var price) ? Text(price, count, gear) : null;
    }

    public static string? Text(Price price, int count, bool gear)
    {
        if (gear && price.Low is { } low && price.High is { } high && low != high && Format(low) is { } from && Format(high) is { } to)
            return $"{from}-{to}";
        return Format(price.Median * count);
    }

    public static Price? Find(IReadOnlyDictionary<int, Price> prices, int id) => prices.TryGetValue(id, out var price) ? price : null;

    public static float LowEnd(Price price) => price.Low ?? price.Median;

    public static float HighEnd(Price price) => price.High ?? price.Median;

    public static double Value(InventoryItem item, IReadOnlyDictionary<int, Price> prices, LegendaryPrices? legendary = null, Func<Price, float>? end = null)
    {
        end ??= price => price.Median;
        if (item.SpecialId != 0 && (legendary?.Of(item.SpecialId, item.Unidentified, [.. item.PrefixIds, .. item.SuffixIds]) ?? Find(prices, item.SpecialId)) is { } special) return end(special);
        return prices.TryGetValue(item.BaseId, out var price) ? (double)end(price) * item.Count : 0;
    }

    public static double Total(IEnumerable<InventoryItem> items, IReadOnlyDictionary<int, Price> prices, LegendaryPrices? legendary = null)
    {
        var total = 0.0;
        foreach (var item in items) total += Value(item, prices, legendary);
        return total;
    }

    public static Vector2 Anchor(Corner corner, Vector2 cellMin, Vector2 cellMax, Vector2 tagSize) => new(
        corner is Corner.TopLeft or Corner.BottomLeft ? cellMin.X + Inset : cellMax.X - tagSize.X - Inset,
        corner is Corner.TopLeft or Corner.TopRight ? cellMin.Y + Inset : cellMax.Y - tagSize.Y - Inset);
}
