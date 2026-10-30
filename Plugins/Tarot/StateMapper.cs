using System.Collections.Generic;
using System.Linq;
using System.Text;
using EmberHUD;

namespace Tarot;

public static class StateMapper
{
    const int EmptyTile = 10000;

    public static SimState? ToSim(TarotState s)
    {
        if (s.Phase != TarotPhase.Board || s.Tiles.Length == 0) return null;
        var sim = new SimState
        {
            Position = s.Position,
            Hp = s.Hp,
            TileCfg = [.. Enumerable.Repeat(EmptyTile, s.Tiles.Length)],
            TileChest = new int[s.Tiles.Length],
            Hand = [.. s.Hand.Select(card => card.CfgId)],
            Taken = [.. s.Taken],
        };
        foreach (var tile in s.Tiles)
        {
            if (tile.Index < 0 || tile.Index >= s.Tiles.Length) continue;
            sim.TileCfg[tile.Index] = tile.CfgId;
            sim.TileChest[tile.Index] = tile.Chest;
        }
        return sim;
    }

    public static TarotState Normalise(TarotState s, bool pickOpen) => s.Playing != 0 && !pickOpen ? s with { Playing = 0 } : s;

    public static IReadOnlyList<Move>? Pending(TarotState s, SimState sim)
    {
        if (s.Playing == 0) return null;
        var index = sim.Hand.IndexOf(s.Playing);
        if (index < 0)
        {
            sim.Hand.Add(s.Playing);
            index = sim.Hand.Count - 1;
        }
        return [.. Sim.Legal(sim).Where(move => move.HandIndex == index)];
    }

    public static string Signature(TarotState s)
    {
        var text = new StringBuilder();
        text.Append((int)s.Phase).Append('|').Append(s.Position).Append('|').Append(s.Hp).Append('|').Append(s.Playing).Append('|');
        foreach (var card in s.Hand) text.Append(card.CfgId).Append(',');
        text.Append('|');
        foreach (var rarity in s.Taken) text.Append(rarity).Append(',');
        text.Append('|');
        foreach (var tile in s.Tiles) text.Append(tile.Index).Append(':').Append(tile.CfgId).Append(':').Append(tile.Chest).Append(',');
        return text.ToString();
    }
}
