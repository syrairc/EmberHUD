using System;
using System.Diagnostics;
using System.Drawing;
using System.Numerics;
using EmberHUD;
using Hexa.NET.ImGui;

namespace Threats;

public sealed class Main : Plugin<Settings>
{
    const double AlertHoldSeconds = 0.3;

    internal Func<long> Clock = Stopwatch.GetTimestamp;
    long _lastDanger;

    public override void Initialize() => RegisterHotkey("Toggle", "Shows or hides the telegraphs", () => Settings.Show = !Settings.Show, new Hotkey(Key.F9));

    public override void Render()
    {
        var state = Game.State;
        if (!Settings.Show || Settings.HideBehindPanels && state.Ui.AnyFullScreen || state.Camera is null || state.Player is not { } player) return;
        Game.WantCasts();
        Game.WantProjectiles();
        var at = new Vector2(player.Position.X, player.Position.Y);
        var inDanger = false;
        var entities = state.Entities;
        for (var i = 0; i < entities.Length; i++)
        {
            ref readonly var entity = ref entities[i];
            if (entity.Kind != EntityKind.Monster || entity.Friendly || entity.Hidden || entity.Cast is null || !Settings.Allows(entity.Rarity, true)) continue;
            inDanger |= Telegraph.Draw(entity, Settings, at);
        }
        var seconds = Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;
        var projectiles = state.Projectiles;
        for (var i = 0; i < projectiles.Length; i++)
        {
            ref readonly var projectile = ref projectiles[i];
            if (!Settings.Allows(projectile.OwnerRarity, false)) continue;
            inDanger |= ProjectileLine.Draw(projectile, Settings, at, seconds);
        }
        var now = Clock();
        if (inDanger) _lastDanger = now;
        if (Settings.MoveAlert && _lastDanger != 0 && now - _lastDanger <= AlertHoldSeconds * Stopwatch.Frequency)
            EmberHUD.Draw.TextOutlined(Settings.MoveAlertText, ImGui.GetIO().DisplaySize / 2, Settings.DangerColour, Color.Black, 2, Align.Center | Align.Middle, Settings.MoveAlertSize);
    }
}
