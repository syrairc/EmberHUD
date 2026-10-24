namespace Vorax;

public abstract class Strategy
{
    public abstract string Name { get; }

    public virtual string Description => "";

    public virtual Pick? PickInstrument(StrategyContext c) => null;

    public virtual Pick? PickPotion(StrategyContext c) => null;

    public virtual Pick? PickTarget(StrategyContext c) => null;

    public virtual bool? ShouldReroll(StrategyContext c) => null;
}
