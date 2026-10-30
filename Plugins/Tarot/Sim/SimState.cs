using System.Collections.Generic;

namespace Tarot;

public sealed class Rng(ulong seed)
{
    ulong state = seed == 0 ? 0x9E3779B97F4A7C15 : seed;

    ulong NextBits()
    {
        state ^= state >> 12;
        state ^= state << 25;
        state ^= state >> 27;
        return state * 0x2545F4914F6CDD1D;
    }

    public int Next(int exclusive) => (int)((NextBits() >> 33) % (ulong)exclusive);

    public double NextDouble() => (NextBits() >> 11) * (1.0 / (1UL << 53));
}

public sealed class SimState
{
    public int Position;
    public int Hp;
    public int[] TileCfg = [];
    public int[] TileChest = [];
    public List<int> Hand = [];
    public List<int> Taken = [];
    public bool Ended;
    public bool Dead;

    public SimState Clone() => new()
    {
        Position = Position,
        Hp = Hp,
        TileCfg = (int[])TileCfg.Clone(),
        TileChest = (int[])TileChest.Clone(),
        Hand = [.. Hand],
        Taken = [.. Taken],
        Ended = Ended,
        Dead = Dead,
    };
}
