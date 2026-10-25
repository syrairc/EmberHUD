namespace Tartarus;

public abstract class Strategy
{
    public abstract string Name { get; }

    public virtual string Description => "";

    internal virtual Sim.Chance Chance => new();

    public virtual StrategyPick? Choose(StrategyContext c) => null;
}
