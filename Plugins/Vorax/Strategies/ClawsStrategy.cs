using System.Collections.Generic;
using System.Linq;
using EmberHUD;

namespace Vorax;

public sealed class ClawsStrategy : Strategy
{
    static readonly int[] InstrumentPriority = [102, 204, 203];
    static readonly int[] SacrificePotions = [105, 106];
    static readonly int[] FunnelPotions = [207, 214, 213];
    static readonly int[] Awakeners = [210];

    const int BoneSentry = 202;
    const int MoltingSkin = 302;
    const int BoneCleansingOintment = 307;

    public override string Name => "Claws";

    public override string Description => "Remove, fuse and devour groups to funnel everything into one big group, Awaken it late.";

    public override Pick? PickInstrument(StrategyContext c) => StrategyHelpers.PickFirstOffered(c, InstrumentPriority, "Claws build instrument");

    public override Pick? PickPotion(StrategyContext c)
    {
        if (StrategyHelpers.SimulatorWantsAKit(c)) return null;
        if (IsLate(c) && StrategyHelpers.PickFirstOffered(c, Awakeners, "Awaken the big group") is { } awaken) return awaken;
        return StrategyHelpers.PickFirstOffered(c, RemoverPriority(c), "removes a group to funnel into one");
    }

    public override Pick? PickTarget(StrategyContext c)
    {
        var potion = c.Chosen!.Value.Id;
        if (SacrificePotions.Contains(potion)) return Sacrifice(c);
        if (FunnelPotions.Contains(potion) || Awakeners.Contains(potion)) return Biggest(c);
        return null;
    }

    static int[] RemoverPriority(StrategyContext c)
    {
        var priority = new List<int>();
        if (CanRemove(c)) priority.AddRange(FunnelPotions);
        if (CanRemove(c) && IsLate(c)) priority.Add(MoltingSkin);
        if (CanRemove(c) && HasBoneSentry(c)) priority.Add(BoneCleansingOintment);
        if (SacrificeCandidates(c).Length > 0) priority.AddRange(SacrificePotions);
        return [.. priority];
    }

    static bool IsLate(StrategyContext c) => c.State.Round > c.State.Rounds / 2;

    static bool HasBoneSentry(StrategyContext c) => c.State.Monsters.Any(m => m.Species == BoneSentry);

    static bool CanRemove(StrategyContext c) => c.State.Monsters.Length >= 2 && c.State.Monsters.Any(m => m.Species != BoneSentry);

    static Pick? Biggest(StrategyContext c) => c.State.Monsters.Length == 0 ? null : Pick.One(BiggestGroup(c).Slot, "biggest group");

    static VoraxMonster BiggestGroup(StrategyContext c) => c.State.Monsters.MaxBy(m => m.Total);

    // never the biggest group, never a Bone Sentry, so the funnel target survives and there is always one group left
    static VoraxMonster[] SacrificeCandidates(StrategyContext c) =>
        c.State.Monsters.Length < 2 ? [] : [.. c.State.Monsters.Where(m => m.Species != BoneSentry && m.Slot != BiggestGroup(c).Slot)];

    static Pick? Sacrifice(StrategyContext c)
    {
        var candidates = SacrificeCandidates(c);
        return candidates.Length == 0 ? null : Pick.One(candidates.MinBy(m => m.Total).Slot, "smallest group that is not a Bone Sentry");
    }
}
