namespace Vorax;

public readonly record struct Pick(int[] Indexes, string Reason)
{
    public bool IsReroll { get; init; }

    public static Pick One(int index, string reason) => new([index], reason);

    public static Pick Reroll(string reason) => new([], reason) { IsReroll = true };
}
