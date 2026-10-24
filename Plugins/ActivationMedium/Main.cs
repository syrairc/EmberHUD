using System;
using System.Drawing;
using System.Numerics;
using EmberHUD;

namespace ActivationMedium;

public sealed class Main : Plugin<Settings>
{
    readonly Motion _motion = new();
    bool _toggled;
    bool _active;
    Key _held;

    internal static bool Active(GameState state, Settings settings, bool toggled) =>
        (state.Status.StartsWith("live") || state.Status == "fake game") && state.Paused == false && !state.Ui.AnyFullScreen && state.Player is not null &&
        (toggled || settings.NearbyEnabled && EnemyNear(state, settings));

    static bool EnemyNear(GameState state, Settings settings) =>
        state.CountNear(settings.Radius, settings.MinRarity) > 0 && !(settings.TownNeedsDpsMeter && state.Area is { IsTown: true } && state.WoodStake is not { Running: true });

    internal static bool Yields(Hotkey yield, Hotkey hold, bool down) => yield.Key != Key.None && yield.Key != hold.Key && down;

    internal static Key MoveKey(MoveInput button) => button switch { MoveInput.Left => (Key)0x01, MoveInput.Right => (Key)0x02, MoveInput.Middle => (Key)0x04, _ => Key.None };

    internal static ScreenRect? SlotRect(GameState state, int slot) =>
        state.SkillSlots is { } slots && slot >= 1 && slot <= slots.Length ? slots[slot - 1] : null;

    public override void Tick()
    {
        Game.WantCharacter();
        if (Input.Pressed(Settings.ToggleKey)) _toggled = !_toggled;
        var settled = !Settings.WaitForStop || _motion.StillMs(Game.State.Player?.Position, Environment.TickCount64) >= Settings.StopDelayMs;
        _active = settled && !Input.IsDown(MoveKey(Settings.MoveButton)) && !Yields(Settings.YieldKey, Settings.HoldKey, Input.IsDown(Settings.YieldKey.Key)) && Active(Game.State, Settings, _toggled);
        // the host drops every held key when the game loses focus
        if (!Send.Allowed) _held = Key.None;
        if (_held != Key.None && (!_active || _held != Settings.HoldKey.Key)) Release();
        if (_active && _held == Key.None && Send.Allowed && Send.Down(Settings.HoldKey.Key)) _held = Settings.HoldKey.Key;
    }

    public override void Render()
    {
        if (Settings.ShowRadius && Settings.NearbyEnabled && Game.State.Player is { } player)
            Ground.Circle(new Vector2(player.Position.X, player.Position.Y), Settings.Radius, Color.FromArgb(200, Settings.HighlightColor), 2, player.Position.Z);
        if (_active && Settings.ShowHighlight && SlotRect(Game.State, Settings.Slot) is { } rect)
            Hexagon.Glow(rect.Min, rect.Max, Settings.HighlightColor, 3, 10);
    }

    void Release()
    {
        Send.Up(_held);
        _held = Key.None;
    }

    public override void OnDisable() => Release();

    public override void Dispose() => Release();
}
