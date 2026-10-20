using System;
using System.Linq;
using EmberHUD;

namespace Pickit;

sealed class Autoloot
{
    public bool Holding { get; private set; }

    long _pausedUntil;

    public void Step(Settings s, GameState state, Func<Key, bool> isDown, Func<Key, bool> down, Func<Key, bool> up, bool sendAllowed, long nowMs = 0)
    {
        if (!sendAllowed)
        {
            Holding = false;
            return;
        }
        if (s.PauseEnabled && s.PauseKey.Key != Key.None && isDown(s.PauseKey.Key)) _pausedUntil = nowMs + (long)(s.PauseSeconds * 1000);
        var key = s.PickupKey.Key;
        if (!Holding && isDown(key)) return;
        var wanted = nowMs >= _pausedUntil && state.Paused != true && ((s.HoldKeyEnabled && isDown(s.HoldKey.Key)) || (s.NearEnabled && NearOwnedItem(s, state)));
        if (wanted && !Holding) Holding = down(key);
        else if (!wanted && Holding)
        {
            up(key);
            Holding = false;
        }
    }

    public void Release(Settings s, Func<Key, bool> up)
    {
        if (!Holding) return;
        up(s.PickupKey.Key);
        Holding = false;
    }

    static bool NearOwnedItem(Settings s, GameState state) =>
        state.Player is { } player && state.GroundItems is { } items && items.Any(item => item.Owned && player.FlatDistance(item.Position) <= s.NearRadius);
}
