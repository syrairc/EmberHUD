using System.Collections.Generic;

namespace Vorax.Sim;

public sealed class Rng(ulong seed)
{
    ulong _state = seed;

    public double NextDouble()
    {
        _state += 0x9E3779B97F4A7C15UL;
        var z = _state;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        z ^= z >> 31;
        return (z >> 11) * (1.0 / (1UL << 53));
    }

    public int Next(int exclusive) => (int)(NextDouble() * exclusive);

    public T Pick<T>(IReadOnlyList<T> items) => items[Next(items.Count)];

    public IReadOnlyList<T> Sample<T>(IReadOnlyList<T> items, int count)
    {
        var pool = new List<T>(items);
        var picked = new List<T>();
        while (picked.Count < count && pool.Count > 0)
        {
            var i = Next(pool.Count);
            picked.Add(pool[i]);
            pool.RemoveAt(i);
        }
        return picked;
    }
}
