using System;
using System.Collections.Generic;
using System.Drawing;
using System.Numerics;
using System.Threading;
using Hexa.NET.ImGui;
using EmberHUD;

namespace Minimap;

public sealed class Main : Plugin<Settings>
{
    internal BackgroundJob<Walls> WallJob = new();
    internal BackgroundJob<Region> RegionJob = new();
    internal Func<long> Clock = () => Environment.TickCount64;
    Walls _walls = Walls.Empty;
    Region _region = Region.Empty;
    string _regionKey = "";
    long _regionAt;
    byte[]? _spare;
    int _warnedArea = -1;
    Vector2[] _screen = [];
    readonly HashSet<int> _overridden = [];
    readonly HashSet<int> _iconEntities = [];
    MapIcon[]? _indexedIcons;
    internal const float MarkerRadius = 4;
    const float GameIconSize = 24;

    bool WallsStyle => Settings.Map.DrawStyle == MapStyle.Walls;

    public override void Initialize()
    {
        RegisterHotkey("Toggle map", "Shows or hides the minimap", () => Settings.General.Show = !Settings.General.Show, new Hotkey(Key.F10));
        RegisterHotkey("Zoom in", "Zooms the map in", () => SetZoom(Settings.General.Zoom * 1.25f), new Hotkey(Key.PageUp));
        RegisterHotkey("Zoom out", "Zooms the map out", () => SetZoom(Settings.General.Zoom / 1.25f), new Hotkey(Key.PageDown));
        RegisterHotkey("Re-check rotation", "Drops the cached game minimap widget and finds it again, use when the map stops following the game's rotation", Game.RescanMinimap, new Hotkey(Key.F9));
    }

    void SetZoom(float value) => Settings.General.Zoom = Math.Clamp(value, GeneralTab.MinZoom, GeneralTab.MaxZoom);

    public override void Tick()
    {
        var view = Settings.General;
        var state = Game.State;
        Settings.Icons.Discover(state.MapIcons);
        if (WallJob.TakeFinished() is { } done) _walls = done.GetAwaiter().GetResult();
        if (_walls.AreaId != state.AreaId) _walls = Walls.Empty;
        if (!WallJob.Running && !_walls.Matches(state.AreaId, state.Terrain))
        {
            var area = state.AreaId;
            var terrain = state.Terrain;
            WallJob.Start(() => Guarded(area, "walls", () => Walls.Build(area, terrain), () => Walls.Empty with { AreaId = area, Source = terrain }));
        }
        if (view.Show && !WallsStyle) UpdateRegion(state);

        Map.View = Hidden(Settings, state.Ui) ? null : ViewFor(Settings, state.Player?.Position, _walls, ImGui.GetIO().DisplaySize, state.MinimapRotation);
    }

    void UpdateRegion(GameState state)
    {
        if (RegionJob.TakeFinished() is { } done)
        {
            var built = done.GetAwaiter().GetResult();
            if (built.Matches(state.AreaId, state.Terrain)) Show(built);
        }
        if (RegionJob.Running || state.Terrain is not { } terrain) return;

        var region = Settings.Map.Region;
        var player = state.Player is { } at ? new Vector2(at.Position.X, at.Position.Y) : (Vector2?)null;
        var now = Clock();
        var current = _region.Matches(state.AreaId, terrain);
        var builtRecently = now - _regionAt < 1000;
        if (current && (builtRecently || !_region.Stale(state.Explored, Region.CellOf(terrain, player), region.Fill, region.ExploredFill))) return;

        _regionAt = now;
        var area = state.AreaId;
        var trail = state.Explored;
        var regionFill = region.Fill;
        var exploredFill = region.ExploredFill;
        var previous = current ? _region : null;
        var reuse = _spare;
        _spare = null;
        RegionJob.Start(() => Guarded(area, "region", () => Region.Build(area, terrain, trail, player, regionFill, exploredFill, previous, reuse), () => Region.Blank(area, terrain, trail, player, regionFill, exploredFill)));
    }

    T Guarded<T>(int area, string what, Func<T> build, Func<T> fallback)
    {
        try
        {
            return build();
        }
        catch (Exception error)
        {
            if (Interlocked.Exchange(ref _warnedArea, area) != area) Log.Warn($"Minimap {what} build failed - {error.Message}");
            return fallback();
        }
    }

    void Show(Region built)
    {
        var key = $"region-{built.AreaId}";
        var newArea = _regionKey != key;
        if (_regionKey.Length > 0 && newArea) Textures.Release(_regionKey);
        var uploaded = built.Pixels.Length > 0;
        if (uploaded) Textures.Upload(key, built.Pixels, built.Width, built.Height);
        if (newArea) _spare = null;
        else if (uploaded && _region.Pixels.Length > 0) _spare = _region.Pixels;
        _regionKey = key;
        _region = built;
    }

    public static MapView? ViewFor(Settings settings, Vector3? player, Walls walls, Vector2 viewport, float? gameRotation = null)
    {
        var view = settings.General;
        var rotation = (gameRotation ?? 0) + view.Rotation;
        var map = view.FollowPlayer && player is { } at && float.IsFinite(at.X) && float.IsFinite(at.Y)
            ? MapView.Follow(Map.Project(at), viewport, view.Zoom, rotation, view.VerticalScale)
            : MapView.Fit(walls.Min, walls.Max, viewport, 40, rotation, view.VerticalScale);
        return map.Scale > 0 ? map with { Offset = map.Offset + new Vector2(view.OffsetX, view.OffsetY) } : null;
    }

    internal static bool Hidden(Settings settings, GameUi ui) =>
        !settings.General.Show || settings.General.HideBehindPanels && ui.AnyFullScreen;

    public override void Render()
    {
        if (Hidden(Settings, Game.State.Ui) || Map.View is not { } map) return;
        var viewport = ImGui.GetIO().DisplaySize;
        var state = Game.State;

        using (Draw.Layer(DrawLayer.Back))
        {
            if (WallsStyle) DrawOutline(_walls.RegionOutline, map, viewport, Settings.Map.Walls.Colour, Settings.Map.Walls.Thickness);
            else DrawRegion(map, state, viewport);
        }

        _overridden.Clear();
        IndexIcons(state.MapIcons);
        var zoomScale = Settings.Icons.ZoomScale(map.Scale);
        foreach (var entity in state.Entities)
        {
            var row = Settings.Icons.FixedFor(entity);
            if (_iconEntities.Contains(entity.Id))
            {
                if (row is not { OverrideGameIcon: true }) continue;
                _overridden.Add(entity.Id);
            }
            if (row is not { Show: true }) continue;
            var at = map.ToScreen(entity.Position);
            if (!Visible(at, at, viewport)) continue;
            Draw.Icon(row.Icon, at, MarkerRadius * row.Scale * zoomScale, row.Colour);
        }

        foreach (var icon in state.MapIcons) DrawIcon(icon, map, viewport, zoomScale);

        if (state.Player is { } player) DrawPlayer(map.ToScreen(player.Position), map.ToScreenDirection(player.Forward), viewport, zoomScale);
    }

    void DrawRegion(MapView map, GameState state, Vector2 viewport)
    {
        var view = Settings.Map.Region;
        var current = _region.Matches(state.AreaId, state.Terrain);
        if (current && _region.Corners.Length == 4 && Textures.TryGet(_regionKey, out var fill))
            Draw.ImageQuad(fill, map.ToScreen(_region.Corners[0]), map.ToScreen(_region.Corners[1]), map.ToScreen(_region.Corners[2]), map.ToScreen(_region.Corners[3]));
        if (current && _region.Trail is null)
        {
            DrawOutline(_walls.RegionOutline, map, viewport, view.ExploredOutline, view.ExploredOutlineWidth);
            return;
        }
        DrawOutline(_walls.RegionOutline, map, viewport, view.Outline, view.OutlineWidth);
        if (current) DrawOutline(_region.ExploredOutline, map, viewport, view.ExploredOutline, view.ExploredOutlineWidth);
    }

    internal void DrawOutline(Polyline[] lines, MapView map, Vector2 viewport, Color colour, float width)
    {
        foreach (var line in lines)
        {
            var count = line.Points.Length;
            if (_screen.Length <= count) _screen = new Vector2[count + 1];
            for (var i = 0; i < count; i++) _screen[i] = map.ToScreen(line.Points[i]);
            var last = line.Closed ? count : count - 1;
            if (line.Closed && count > 0)
            {
                _screen[count] = _screen[0];
                var hidden = 0;
                while (hidden < count && Visible(_screen[hidden], _screen[hidden + 1], viewport)) hidden++;
                if (hidden < count)
                {
                    var shift = (hidden + 1) % count;
                    _screen.AsSpan(0, shift).Reverse();
                    _screen.AsSpan(shift, count - shift).Reverse();
                    _screen.AsSpan(0, count).Reverse();
                    _screen[count] = _screen[0];
                }
            }
            var start = -1;
            for (var i = 0; i <= last; i++)
            {
                if (i < last && Visible(_screen[i], _screen[i + 1], viewport))
                {
                    if (start < 0) start = i;
                    continue;
                }
                if (start < 0) continue;
                var wholeLoop = line.Closed && start == 0 && i == last;
                Draw.Polyline(_screen.AsSpan(start, wholeLoop ? count : i - start + 1), colour, width, wholeLoop);
                start = -1;
            }
        }
    }

    void IndexIcons(MapIcon[] icons)
    {
        if (ReferenceEquals(icons, _indexedIcons)) return;
        _indexedIcons = icons;
        _iconEntities.Clear();
        foreach (var icon in icons)
            if (icon.EntityId != 0 && float.IsFinite(icon.Position.X) && float.IsFinite(icon.Position.Y)) _iconEntities.Add(icon.EntityId);
    }

    void DrawIcon(MapIcon icon, MapView map, Vector2 viewport, float zoomScale)
    {
        if (icon.EntityId != 0 && _overridden.Contains(icon.EntityId)) return;
        var at = map.ToScreen(icon.Position);
        if (!Visible(at, at, viewport)) return;
        var icons = Settings.Icons;
        var row = icons.FoundFor(icon.Texture);
        if (row is { Show: false }) return;
        var size = GameIconSize * (float.IsFinite(icon.Size) && icon.Size > 0 ? icon.Size : 1) * icons.GameIconScale * (row?.Scale ?? 1) * zoomScale;
        var name = row?.Icon ?? "";
        var colour = row?.Colour ?? Color.White;
        if (name.StartsWith(Draw.GameIconPrefix, StringComparison.Ordinal))
        {
            if (icons.UseGameIcons && Draw.TryGameIcon(name.AsSpan(Draw.GameIconPrefix.Length), out var sprite)) Draw.Image(sprite, at - new Vector2(size / 2), at + new Vector2(size / 2), colour);
            else Draw.Marker(at, size / 4, colour, FallbackShape(icon.Type));
        }
        else if (name.Length == 0) Draw.Marker(at, size / 4, GameColors.MinimapMarker, FallbackShape(icon.Type));
        else Draw.Icon(name, at, size / 2, colour);
    }

    void DrawPlayer(Vector2 at, Vector2 forward, Vector2 viewport, float zoomScale)
    {
        if (!Visible(at, at, viewport)) return;
        var player = Settings.Map.Player;
        var size = player.Size * zoomScale;
        var colour = player.Colour;
        if (ArrowPoints(at, forward, size) is not { } arrow)
        {
            Draw.Marker(at, size / 2, colour);
            return;
        }

        if (Settings.Icons.UseGameIcons && Draw.TryGameIcon("UI_MiniMap_Player1", out var sprite)) Draw.ImageRotated(sprite, at, new Vector2(GameIconSize * Settings.Icons.GameIconScale * zoomScale), new Vector2(forward.Y, -forward.X), Color.White);
        else Draw.TriangleFilled(arrow.Tip, arrow.Left, arrow.Right, colour);
    }

    internal static MarkerShape FallbackShape(int type) => type switch
    {
        2 or (>= 1040 and <= 1060) => MarkerShape.Star,
        10 or 11 or 12 or 14 or 32 or 33 or 1000 or 1022 => MarkerShape.Diamond,
        7 or 8 or 9 or 1010 or 2001 or 2002 => MarkerShape.Hexagon,
        3 or 1006 or 1007 or 1009 or 1011 => MarkerShape.Square,
        (>= 15 and <= 24) or 29 => MarkerShape.Triangle,
        1003 or 1008 or 1023 or 1024 => MarkerShape.Sparkle,
        _ => MarkerShape.Dot,
    };

    internal static bool Visible(Vector2 a, Vector2 b, Vector2 viewport) =>
        float.IsFinite(a.X) && float.IsFinite(a.Y) && float.IsFinite(b.X) && float.IsFinite(b.Y)
        && MathF.Max(a.X, b.X) >= 0 && MathF.Min(a.X, b.X) <= viewport.X
        && MathF.Max(a.Y, b.Y) >= 0 && MathF.Min(a.Y, b.Y) <= viewport.Y;

    internal static (Vector2 Tip, Vector2 Left, Vector2 Right)? ArrowPoints(Vector2 at, Vector2 forward, float size)
    {
        if (!(float.IsFinite(forward.X) && float.IsFinite(forward.Y) && forward.LengthSquared() > 1e-6f)) return null;
        var direction = Vector2.Normalize(forward);
        var side = new Vector2(-direction.Y, direction.X);
        var back = at - direction * size * 0.6f;
        return (at + direction * size, back + side * size * 0.6f, back - side * size * 0.6f);
    }
}
