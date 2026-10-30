namespace Tarot;

public sealed record Weights
{
    public double Chest { get; init; }
    public double Ticket { get; init; }
    public double LowChestPenalty { get; init; }
    public double Question { get; init; }
    public double UpgradeTile { get; init; }
    public double Card { get; init; }
    public double Life { get; init; }
    public double LowLife { get; init; }
    public double Trap { get; init; }
    public double TrapLowLife { get; init; }
    public double Empty { get; init; }
    public double BackCost { get; init; }
    public double RefillBias { get; init; }
    public double[] Values { get; init; } = new double[8];

    static double[] Rarities(double green, double blue, double purple, double gold, double red, double rainbow) =>
        [0, green * 0.5, green, blue, purple, gold, red, rainbow];

    public static readonly Weights Rarity = new()
    {
        Chest = 0.6252,
        Ticket = 0.8089,
        LowChestPenalty = 2.0484,
        Question = 4.3685,
        UpgradeTile = 3.0439,
        Card = 2.0873,
        Life = 0.2216,
        LowLife = 0.4803,
        Trap = 0.443,
        TrapLowLife = 35.84,
        Empty = 0.023,
        BackCost = 0.003,
        RefillBias = 2.048,
        Values = Rarities(1.421, 4.915, 2.64, 8.265, 10, 17.969),
    };

    public static readonly Weights Good = new()
    {
        Chest = 1,
        Ticket = 0.769,
        LowChestPenalty = 3.52,
        Question = 4.37,
        UpgradeTile = 3.5,
        Card = 2.09,
        Life = 0.31,
        LowLife = 0.36,
        Trap = 1.373,
        TrapLowLife = 14,
        Empty = -0.703,
        BackCost = 0.038,
        RefillBias = 2.05,
        Values = Rarities(1.08, 4.92, 5.2, 10, 16, 25),
    };
}
