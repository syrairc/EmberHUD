using System.Collections.Generic;

namespace Vorax.Sim;

public readonly record struct KitDef(int Amount, int[] RarityWeights);

public static class GameIds
{
    static readonly Dictionary<int, int> Potion = new()
    {
        [101] = 30, [102] = 23, [103] = 29, [104] = 24, [105] = 31, [106] = 32, [107] = 25, [108] = 27, [109] = 26, [110] = 28,
        [201] = 9, [202] = 11, [203] = 8, [204] = 10, [205] = 19, [206] = 14, [207] = 17, [208] = 21, [209] = 16, [210] = 20, [211] = 7, [212] = 12, [213] = 15, [214] = 18, [215] = 22, [216] = 13,
        [301] = 4, [302] = 1, [303] = 2, [304] = 6, [305] = 0, [306] = 5, [307] = 3,
        [401] = 37, [402] = 35, [403] = 38, [404] = 39, [405] = 36, [406] = 41,
    };

    static readonly Dictionary<int, int> Instrument = new()
    {
        [101] = 7, [102] = 6, [103] = 1, [104] = 4, [105] = 5, [106] = 2, [107] = 3, [108] = 8,
        [201] = 12, [202] = 21, [203] = 10, [204] = 14, [205] = 9, [206] = 24, [207] = 11, [208] = 13, [209] = 15, [210] = 18, [211] = 17, [212] = 16, [213] = 22, [214] = 20, [215] = 23, [216] = 19, [217] = 25,
    };

    static readonly Dictionary<int, KitDef> Kits = new()
    {
        [2] = new(3, [40, 44, 15, 1]),
        [4] = new(3, [40, 44, 18, 2]),
        [6] = new(5, [40, 44, 15, 1]),
    };

    // bundle id -> the sim id of the same kit in a drawn hand (2 Small, 4 Rare Small, 6 Large)
    static readonly Dictionary<int, int> KitSimIds = new() { [2] = 34, [4] = 40, [6] = 33 };

    public static bool TryKitSimId(int bundleId, out int simId) => KitSimIds.TryGetValue(bundleId, out simId);

    public static bool TryPotion(int gameId, out int simId) => Potion.TryGetValue(gameId, out simId);

    public static bool TryInstrument(int gameId, out int simId) => Instrument.TryGetValue(gameId, out simId);

    public static bool IsStartSetup(int gameId) => gameId is >= 1 and <= 4;

    public static bool TryKit(int bundleId, out KitDef kit) => Kits.TryGetValue(bundleId, out kit);
}
