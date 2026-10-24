namespace Vorax.Sim;

public enum Species { Pests = 201, BoneSentry = 202, Agito = 203, Aemberon = 204 }

public enum Rarity { Normal = 1, Magic = 2, Rare = 3, Boss = 4 }

public readonly record struct Monster(Species Species, Rarity Rarity, int Activity, int Quantity)
{
    public int Total => Activity * Quantity;

    public static Monster Fresh(Species species, Rarity rarity) => rarity switch
    {
        Rarity.Normal => new(species, rarity, 1, 36),
        Rarity.Magic => new(species, rarity, 5, 24),
        Rarity.Rare => new(species, rarity, 15, 12),
        _ => new(species, rarity, 300, 1),
    };
}
