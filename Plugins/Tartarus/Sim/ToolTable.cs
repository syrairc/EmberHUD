namespace Tartarus.Sim;

sealed class ToolTable(double baseline, double[] single, double[] pairs)
{
    public double Base { get; } = baseline;

    public double[] Single { get; } = single;

    public double Pair(ToolId first, ToolId second)
    {
        var (a, b) = first < second ? ((int)first, (int)second) : ((int)second, (int)first);
        return pairs[a * (2 * Single.Length - a - 1) / 2 + b - a - 1];
    }
}
