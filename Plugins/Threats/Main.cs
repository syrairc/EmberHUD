using System;
using System.Collections.Generic;
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
    long _groundSince;
    readonly HashSet<(int, int)> _rings = [];

    public override void Initialize() => RegisterHotkey("Toggle", "Shows or hides the telegraphs", () => Settings.Show = !Settings.Show, new Hotkey(Key.F9));

    public override void Render()
    {
        var state = Game.State;
        if (!Settings.Show || Settings.HideBehindPanels && state.Ui.AnyFullScreen || state.Camera is null || state.Player is not { } player) return;
        using (EmberHUD.Draw.Layer(DrawLayer.Back)) Groups.Declare(Settings);
        Game.WantCasts();
        Game.WantProjectiles();
        Game.WantGroundHazards();
        var at = new Vector2(player.Position.X, player.Position.Y);
        var now = Clock();
        var inDanger = false;
        var inArea = false;
        if (Settings.GroundAreas)
        {
            var hazards = state.GroundHazards;
            for (var i = 0; i < hazards.Length; i++)
            {
                ref readonly var hazard = ref hazards[i];
                if (!GroundHazards.Draw(hazard, Settings, at)) continue;
                if (hazard.Kind == HazardKind.Damage) inDanger = true;
                else inArea = true;
            }
        }
        if (!inArea) _groundSince = 0;
        else if (_groundSince == 0) _groundSince = now;
        inDanger |= inArea && now - _groundSince >= Settings.GroundAlertSeconds * Stopwatch.Frequency;
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
            inDanger |= ProjectileLine.Draw(projectile, Settings, at, seconds, _rings);
        }
        _rings.Clear();
        if (inDanger) _lastDanger = now;
        if (Settings.MoveAlert && _lastDanger != 0 && now - _lastDanger <= AlertHoldSeconds * Stopwatch.Frequency)
            EmberHUD.Draw.TextOutlined(Settings.MoveAlertText, ImGui.GetIO().DisplaySize / 2, Settings.DangerColour, Color.Black, 2, Align.Center | Align.Middle, Settings.MoveAlertSize);
    }
}
