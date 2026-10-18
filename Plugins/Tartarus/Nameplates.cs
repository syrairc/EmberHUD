using System.Linq;
using EmberHUD;

namespace Tartarus;

public static class Nameplates
{
    public const int MoneyId = 828;

    public static int? Count(InventoryItem[]? inventory)
    {
        var money = inventory?.Where(item => item.BaseId == MoneyId).ToArray();
        return money is { Length: > 0 } ? money.Sum(item => item.Count) : null;
    }
}
