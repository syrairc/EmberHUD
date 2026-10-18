using System;

namespace Tartarus.Sim;

public sealed class Rng(ulong seed)
{
    ulong state = seed;

    public uint Next()
    {
        state += 0x9E3779B97F4A7C15UL;
        var z = state;
        z = (z ^ z >> 30) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ z >> 27) * 0x94D049BB133111EBUL;
        return (uint)((z ^ z >> 31) >> 32);
    }

    public double NextDouble() => Next() / 4294967296.0;

    public int Next(int maxExclusive)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(maxExclusive, 0);
        return (int)((ulong)Next() * (ulong)maxExclusive >> 32);
    }

    public Rng Clone() => new(state);
}
