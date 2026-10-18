using System;
using System.IO;
using EmberHUD;
using Hexa.NET.ImGui;

namespace NameplatesPlugin;

public sealed class Main : Plugin<Settings>
{
    public override void Initialize()
    {
        Settings.Normalize();
        BarTextures.Load(BarTextures.DropInFolder);
    }

    public override void Render()
    {
        var state = Game.State;
        if (Settings.HideBehindPanels && state.Ui.AnyFullScreen || state.Camera is not { } camera) return;
        var viewport = ImGui.GetIO().DisplaySize;
        if (Settings.AnyCastBar()) Game.WantCasts();
        var player = state.Player;
        var entities = state.Entities;
        for (var i = 0; i < entities.Length; i++)
        {
            ref readonly var entity = ref entities[i];
            var distance = Settings.Displayed(player?.FlatDistance(entity.Position) ?? 0);
            if (Resolver.BucketOf(entity) is not { } bucket || Resolver.ResolveBucket(Settings, bucket, entity.Name, entity.CfgId, entity.Life, entity.MaxLife, distance) is not { } style) continue;
            if (Anchor.Rect(Settings, entity, style, camera.ToScreen(entity.Position, viewport)) is not { } rect) continue;
            var castBar = Settings.Buckets[(int)bucket].CastBar!;
            var cast = castBar.Enabled ? entity.Cast : null;
            var data = new PlateData(entity.Name, entity.Life, entity.MaxLife, distance, entity.Rarity.ToString(), entity.Kind.ToString(), cast?.Name ?? "", cast?.SecondsLeft ?? 0);
            if (PlateRenderer.DrawPlate(data, style, rect, viewport) && cast is { } active) PlateRenderer.DrawCast(castBar, data, active.Progress, rect);
        }
        if (player is null) return;
        if (Resolver.ResolveBucket(Settings, Bucket.Self, "", 0, player.Life, player.MaxLife, 0) is not { } selfStyle) return;
        if (camera.ToScreen(player.Position, viewport) is not { } selfAnchor) return;
        PlateRenderer.DrawPlate(new PlateData("", player.Life, player.MaxLife, 0, "", "Player"), selfStyle, Anchor.Projected(selfStyle, selfAnchor), viewport);
    }
}
