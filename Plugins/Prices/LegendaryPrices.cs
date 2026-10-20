using System;
using System.Collections.Generic;
using System.Linq;

namespace Prices;

public readonly record struct LegendaryEntry(int SpecialId, bool Unidentified, Price Price);

public sealed class LegendaryPrices
{
    const int MinVariantRows = 3;

    public static readonly LegendaryPrices Empty = new([], [], []);

    readonly Dictionary<int, Price> _unidentified;
    readonly Dictionary<int, Price> _identified;
    readonly Dictionary<ulong, Price> _variants;

    internal LegendaryPrices(Dictionary<int, Price> unidentified, Dictionary<int, Price> identified, Dictionary<ulong, Price> variants) => (_unidentified, _identified, _variants) = (unidentified, identified, variants);

    public int Count => _unidentified.Count + _identified.Count;

    public IEnumerable<LegendaryEntry> Entries => _identified.Select(pair => new LegendaryEntry(pair.Key, false, pair.Value)).Concat(_unidentified.Select(pair => new LegendaryEntry(pair.Key, true, pair.Value)));

    public static ulong Key(int specialId, IEnumerable<int> prefixAndSuffixIds)
    {
        var hash = 14695981039346656037UL;
        void Mix(int value)
        {
            for (var shift = 0; shift < 32; shift += 8) hash = (hash ^ (byte)(value >> shift)) * 1099511628211UL;
        }
        Mix(specialId);
        foreach (var id in prefixAndSuffixIds.Distinct().Order()) Mix(id);
        return hash;
    }

    public Price? Of(int specialId, bool unidentified, IEnumerable<int> prefixAndSuffixIds)
    {
        if (unidentified) return _unidentified.TryGetValue(specialId, out var hidden) ? hidden : null;
        if (_variants.TryGetValue(Key(specialId, prefixAndSuffixIds), out var variant) && variant.Submissions >= MinVariantRows) return variant;
        return _identified.TryGetValue(specialId, out var known) ? known : null;
    }

    public LegendaryPrices Filtered(int minSamples, DateTimeOffset? notBefore)
    {
        Dictionary<TKey, Price> Keep<TKey>(Dictionary<TKey, Price> table) where TKey : notnull => table.Where(pair => !(pair.Value.Submissions < minSamples) && !(pair.Value.Updated < notBefore)).ToDictionary(pair => pair.Key, pair => pair.Value);
        return new LegendaryPrices(Keep(_unidentified), Keep(_identified), Keep(_variants));
    }
}
