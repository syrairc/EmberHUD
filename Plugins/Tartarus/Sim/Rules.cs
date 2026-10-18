namespace Tartarus.Sim;

public sealed record Rules
{
    public int Days { get; init; } = 20;
    public int RingSize { get; init; } = 16;
    public int DoorChoices { get; init; } = 3;
    public int DoorWindow { get; init; } = 6;
    public int SecondMealFromTier { get; init; } = 0;
    public bool ReplaceIsExecute { get; init; }
    public bool ReplaceAfterConsume { get; init; }
    public int ChromaticCap { get; init; } = 10;
    public bool EaterKeepsBeast { get; init; }
    public int EventStartChance { get; init; } = 72;
    public int EventMorningChance { get; init; } = 5;
    public int EventWalkChance { get; init; } = 9;
    public int EventActionChance { get; init; } = 4;
    public int StartTools { get; init; } = 1;
    public StartCard Card { get; init; }
}
