using System;
using System.Linq;
using EmberHUD;

namespace Pickit;

sealed class Autoloot
{
    public bool Holding { get; private set; }

    public void Step(Settings s, GameState state, Func<Key, bool> isDown, Func<Key, bool> down, Func<Key, bool> up, bool sendAllowed)
    {
        if (!sendAllowed)
        {
            Holding = false;
            return;
        }
        var key = s.PickupKey.Key;
        if (!Holding && isDown(key)) return;
        var wanted = state.Paused != true && ((s.HoldKeyEnabled && isDown(s.HoldKey.Key)) || (s.NearEnabled && NearOwnedItem(s, state)));
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
