using System.Linq;

namespace Tarot;

public enum EffectKind { Move, Life, GiveCards, RarityUpCollected, RarityUpAhead, SetAheadRarity, TileChange, Unknown }

public readonly record struct Effect(EffectKind Kind, int Min, int Max, int RangeMin, int RangeMax, int[] Set, int Target);

public static class Rules
{
    public const int HandCap = 11;
    public const int ChestSlots = 7;
    public const int MaxLife = 6;
    public const int StartLife = 2;

    const int EmptyTile = 10000;
    const int TrapTile = 1000;
    const int Everything = 99;

    static readonly Effect Unknown = new(EffectKind.Unknown, 0, 0, 0, 0, [], 0);
    static readonly int[] NamedStarts = [1012, 1011, 1014, 1013, 1012, 1013, 1015, 1012, 1016];
    static readonly int[] TrapChanges = [2010, 1051, 1052, 1053, 1021, 1022, 1023, 1101, 1102, 1103, 1104];
    static readonly int[] AllChests = [2001, 2002, 2003, 2004, 2005, 2006, 2007, 2010, 2011, 2012, 2013, 2014, 2015];
    static readonly int[] AllCardIds = [.. Enumerable.Range(1, 1009).Where(id => Tag(id) != 0 && DrawWeight(id) > 0)];

    public static bool IsFallback(int cardCfg) => cardCfg is >= 101 and <= 106;

    public static bool IsUnknown(int cardCfg) => OfCard(cardCfg) is [{ Kind: EffectKind.Unknown }];

    public static bool IsUpgradeCard(int cardCfg) => Tag(cardCfg) == 6;

    public static (int Count, int MaxRarity) Upgrade(int cardCfg) =>
        cardCfg is >= 201 and <= 224 && OfCard(cardCfg) is [{ Kind: EffectKind.RarityUpCollected } up] ? (up.Min, up.Target) : (0, 0);

    public static int DrawWeight(int cardCfg) => cardCfg switch
    {
        >= 1 and <= 6 => 50000,
        >= 11 and <= 16 => 36667,
        21 or 22 or 24 => 100000,
        23 => 80000,
        >= 101 and <= 106 or >= 1001 and <= 1009 => 1,
        >= 201 and <= 204 => new[] { 35000, 21000, 10500, 3500 }[cardCfg - 201],
        >= 211 and <= 214 => new[] { 12500, 7500, 3750, 1250 }[cardCfg - 211],
        >= 221 and <= 224 => new[] { 2500, 1500, 750, 250 }[cardCfg - 221],
        _ => 0,
    };

    public static int[] DrawPool(int[] tags) => [.. AllCardIds.Where(id => tags.Contains(Tag(id)))];

    public static Effect[] OfCard(int cardCfg) => cardCfg switch
    {
        >= 1 and <= 6 => Event(1000 + cardCfg),
        >= 11 and <= 16 => Event(1090 + cardCfg),
        >= 21 and <= 24 => [],
        >= 31 and <= 33 => Event(2069 + cardCfg),
        >= 41 and <= 47 => Event(2960 + cardCfg),
        >= 60 and <= 69 => Event(1141 + cardCfg),
        >= 101 and <= 106 => [.. Event(5000), .. Event(910 + cardCfg)],
        (>= 201 and <= 204) or (>= 211 and <= 214) or (>= 221 and <= 224) => Event(4110 + (cardCfg / 10 - 20) * 10 + cardCfg % 10 - 1),
        >= 1001 and <= 1009 => [.. Event(8000 + cardCfg), .. Event(NamedStarts[cardCfg - 1001])],
        _ => [Unknown],
    };

    public static Effect[] OfTile(int tileCfg) => tileCfg switch
    {
        TrapTile => Event(2000),
        EmptyTile or 1002 or (>= 2001 and <= 2015) or (>= 3001 and <= 3003) => [],
        (>= 1011 and <= 1013) or (>= 1021 and <= 1023) or (>= 1031 and <= 1033) or (>= 1041 and <= 1043) => Event(tileCfg + 5990),
        >= 1051 and <= 1053 => Event(tileCfg + 1049),
        >= 1101 and <= 1104 => Event(tileCfg + 6000),
        1201 or 1202 => Event(tileCfg + 6000),
        >= 4001 and <= 4011 => Event(tileCfg + 4000),
        5001 => Event(9103),
        5002 => Event(9102),
        5003 => Event(9101),
        (>= 9201 and <= 9225) or (>= 9301 and <= 9312) => Event(tileCfg),
        _ => [Unknown],
    };

    static int Tag(int cardCfg) => cardCfg switch
    {
        >= 1 and <= 6 => 1,
        >= 11 and <= 16 => 2,
        >= 21 and <= 24 => 3,
        >= 31 and <= 33 => 4,
        >= 41 and <= 47 => 5,
        >= 51 and <= 59 => 6,
        >= 60 and <= 69 => 7,
        (>= 101 and <= 106) or (>= 1001 and <= 1009) => 99,
        (>= 201 and <= 204) or (>= 211 and <= 214) or (>= 221 and <= 224) => 6,
        >= 301 and <= 307 => 8,
        308 or (>= 311 and <= 318) => 9,
        _ => 0,
    };

    static Effect[] One(Effect effect) => [effect];

    static Effect[] Move(params int[] steps) => One(new(EffectKind.Move, 0, 0, 0, 0, steps, 0));

    static Effect[] Life(int delta) => One(new(EffectKind.Life, delta, delta, 0, 0, [], 0));

    static Effect[] Give(int min, int max, int[] cards) =>
        cards.Length == 0 ? [Unknown] : One(new(EffectKind.GiveCards, min, max, 0, 0, cards, 0));

    static Effect[] RarityUp(int count, int maxRarity) => One(new(EffectKind.RarityUpCollected, count, count, 0, 0, [], maxRarity));

    static Effect[] Change(int min, int max, int rangeMin, int rangeMax, int[] from, int target) =>
        One(new(EffectKind.TileChange, min, max, rangeMin, rangeMax, from, target));

    static Effect[] Event(int id) => id switch
    {
        >= 1001 and <= 1006 => Move(id - 1000),
        >= 1011 and <= 1016 => Move(id - 1010),
        >= 1101 and <= 1106 => Move(-(id - 1100)),
        1201 => Move(1, 2, 3, 4, 5, 6),
        1202 => Move(1, 2, 3),
        1203 => Move(4, 5, 6),
        1204 => Move(1, 3, 5),
        1205 => Move(2, 4, 6),
        1206 => Move(-1, -2, -3, -4, -5, -6),
        1207 => Move(-1, -2, -3),
        1208 => Move(-4, -5, -6),
        1209 => Move(-1, -3, -5),
        1210 => Move(-2, -4, -6),
        2000 => Life(-1),
        >= 2100 and <= 2102 => Life(id - 2099),
        >= 9205 and <= 9207 => Life(id - 9204),
        >= 3001 and <= 3007 => Change(1, 1, 1, 8, [EmptyTile], id - 1000),
        (>= 4110 and <= 4113) or (>= 4120 and <= 4123) or (>= 4130 and <= 4133) => RarityUp((id - 4100) / 10, 3 + id % 10),
        5000 => Give(1, 1, [101, 102, 103, 104, 105, 106]),
        >= 7001 and <= 7003 => Give(id - 7000, id - 7000, DrawPool([1, 2, 3, 4, 5, 6])),
        >= 7011 and <= 7013 => Give(id - 7010, id - 7010, DrawPool([1, 2, 3])),
        >= 9208 and <= 9210 => Give(id - 9207, id - 9207, DrawPool([1, 2, 3])),
        >= 7021 and <= 7023 => Give(id - 7020, id - 7020, DrawPool([5])),
        >= 7031 and <= 7033 => Give(id - 7030, id - 7030, DrawPool([6])),
        9220 => Give(1, 1, DrawPool([6])),
        >= 7101 and <= 7104 => Give(1, 1, [200 + id - 7100, 210 + id - 7100, 220 + id - 7100]),
        7201 => Give(1, 1, DrawPool([8])),
        7202 => Give(1, 1, DrawPool([9])),
        >= 8001 and <= 8011 => Change(1, 3, 1, 12, [TrapTile], TrapChanges[id - 8001]),
        >= 9001 and <= 9008 => Give(1, 1, [id - 9001 + 1002]),
        9009 => Give(1, 1, [103]),
        9101 => Give(1, 1, [6]),
        9102 => Give(1, 1, [24]),
        9103 => Change(1, 1, 2, 2, [TrapTile], 5002),
        >= 9201 and <= 9203 => RarityUp(id - 9200, 6),
        9204 => RarityUp(Everything, 6),
        9217 => One(new(EffectKind.RarityUpAhead, Everything, Everything, 0, 0, [], 6)),
        9218 or 9219 => One(new(EffectKind.SetAheadRarity, Everything, Everything, 1, Everything, AllChests, id - 9212)),
        9221 => Change(Everything, Everything, 1, Everything, [TrapTile], EmptyTile),
        9222 => Change(Everything, Everything, 1, Everything, [TrapTile], 2010),
        >= 9223 and <= 9225 => Change(id - 9222, id - 9222, 1, 12, [TrapTile, EmptyTile], 2010),
        >= 9211 and <= 9216 => Give((id - 9211) % 3 + 1, (id - 9211) % 3 + 1, [id < 9214 ? 23 : 24]),
        >= 9301 and <= 9312 => Give(1, 1, [new[] { 201, 211, 221, 202, 212, 222, 203, 213, 223, 204, 214, 224 }[id - 9301]]),
        _ => [Unknown],
    };
}
