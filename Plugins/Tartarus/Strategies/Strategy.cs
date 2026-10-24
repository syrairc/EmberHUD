namespace Tartarus;

public abstract class Strategy
{
    public abstract string Name { get; }

    public virtual string Description => "";

    public virtual StrategyPick? Choose(StrategyContext c) => null;
}
