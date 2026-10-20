using System.Collections.Generic;
using EmberHUD;

namespace Prices;

// base ids live in two loc tables: item_base (currency, skills, most things) and item_gold (gear bases)
public static class ItemNames
{
    const string Probe = "item_base|name|100300";

    static readonly Dictionary<int, string> Known = [];

    public static string Of(int baseId)
    {
        if (Known.TryGetValue(baseId, out var known)) return known;
        var name = Loc.Get($"item_base|name|{baseId}") ?? Loc.Get($"item_gold|name|{baseId}");
        if (name is null && Loc.Get(Probe) is null) return "";
        return Known[baseId] = name ?? "";
    }

    public static int Rarity(int id) => Items.IsUnique(id) ? 100 : Items.TryGet(id, out var info) ? info.Rarity : 0;

    internal static void Forget() => Known.Clear();
}
