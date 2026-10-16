using System;
using System.Numerics;
using Hexa.NET.ImGui;
using EmberHUD;

namespace GroundRing;

public sealed class Main : Plugin<Settings>
{
    const int Segments = 48;

    public override void Initialize() => RegisterHotkey("Toggle", "Shows or hides the rings", () => Settings.Show = !Settings.Show, new Hotkey(Key.F9));

    public override void Render()
    {
        var state = Game.State;
        if (!Settings.Show || state.Camera is not { } camera) return;
        var viewport = ImGui.GetIO().DisplaySize;
        Span<Vector2> ring = stackalloc Vector2[Segments];
        foreach (var entity in state.Entities)
        {
            if (entity.Kind != EntityKind.Monster || entity.Friendly || !Project(camera, viewport, entity.Position, ring)) continue;
            Draw.Polyline(ring, Settings.Colour, Settings.Thickness, true);
        }
    }

    bool Project(Camera camera, Vector2 viewport, Vector3 centre, Span<Vector2> into)
    {
        for (var i = 0; i < into.Length; i++)
        {
            var angle = i * MathF.Tau / into.Length;
            var world = centre + new Vector3(MathF.Cos(angle), MathF.Sin(angle), 0) * Settings.WorldRadius;
            if (camera.ToScreen(world, viewport) is not { } at) return false;
            into[i] = at;
        }
        return true;
    }
}
