using System;
using EmberHUD;

namespace XpTrackerPlugin;

public readonly record struct DeathInfo(XpState Xp, float Percent, long Loss, bool TokenInUse, double? Hours)
{
    public float Current => (float)Xp.Exp / Xp.ToNext;

    public float Kept => (float)(Xp.Exp - Loss) / Xp.ToNext;

    public static DeathInfo? From(GameState state, XpState? aliveXp, XpSession session)
    {
        if (state.Death is not { } death || aliveXp is not { ToNext: > 0 } xp) return null;
        var token = death.TokenChecked == true;
        var loss = token ? 0 : Math.Min(xp.Exp, (long)Math.Round(xp.ToNext * death.XpLossPercent / 100.0));
        return new DeathInfo(xp, death.XpLossPercent, loss, token, session.XpPerHour is { } rate ? loss / rate : null);
    }
}
