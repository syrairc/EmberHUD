using System;
using System.Collections.Generic;
using System.Drawing;
using System.Numerics;
using Hexa.NET.ImGui;
using EmberHUD;
using EmberHUD.Ui;

namespace Minimap;

public sealed class IconStyle
{
    public string Key = "";
    public string Name = "";
    public bool Show = true;
    public string Icon = "";
    public Color Colour = Color.White;
    public float Scale = 1;
    public bool OverrideGameIcon;
}

public enum MapStyle { Region, Walls }

public sealed class GeneralTab
{
    internal const float DefaultZoom = 0.037f;
    internal const float MinZoom = 0.002f;
    internal const float MaxZoom = 4;

    public bool Show = true;
    public bool FollowPlayer = true;
    public bool HideBehindPanels = true;
    public float Zoom = DefaultZoom;
    public float Rotation;
    public float VerticalScale = 1;
    public float OffsetX;
    public float OffsetY = -50;
}

public sealed class RegionLook
{
    public Color Fill = Color.FromArgb(0x1F, GameColors.MinimapUnexplored);
    public Color Outline = GameColors.MinimapUnexplored;
    public float OutlineWidth = 1.5f;
    public Color ExploredFill = Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF);
    public Color ExploredOutline = GameColors.MinimapOutline;
    public float ExploredOutlineWidth = 2.5f;
}

public sealed class WallLook
{
    public Color Colour = Color.FromArgb(0xE6, 0xD8, 0xD0, 0x90);
    public float Thickness = 1.5f;
}

public sealed class PlayerLook
{
    public Color Colour = Color.FromArgb(0xFF, 0xFF, 0x40, 0x40);
    public float Size = 10;
}

public sealed class MapTab
{
    public MapStyle DrawStyle = MapStyle.Region;
    public RegionLook Region = new();
    public WallLook Walls = new();
    public PlayerLook Player = new();
}

public sealed class IconsTab
{
    public bool UseGameIcons = true;
    public float GameIconScale = 1;
    public bool ScaleWithZoom = true;
    public float ZoomScaleFactor = 0.5f;
    public float ZoomScaleMin = 0.5f;
    public float ZoomScaleMax = 2;
    public IconStyle NormalMonsters = Row("Normal monsters", "marker:Dot", 0xEBEBEB, 1);
    public IconStyle MagicMonsters = Row("Magic monsters", "marker:Dot", 0x598CFF, 1);
    public IconStyle RareMonsters = Row("Rare monsters", "svg:elite", 0xFFD933, 1);
    public IconStyle Bosses = Row("Bosses", "svg:elite", 0xD94DFF, 1);
    public IconStyle Minions = Row("Minions", "marker:Dot", 0x4DE64D, 1, false);
    public IconStyle Npcs = Row("NPCs", "marker:Square", 0x4DFF4D, 1);
    public IconStyle OtherPlayers = Row("Other players", "marker:Dot", 0x4DE6FF, 1);
    public IconStyle MapObjects = Row("Map objects", "marker:Diamond", 0xC0C0C0, 1);
    public IconStyle Destructibles = Row("Destructibles", "marker:Triangle", 0xA0703C, 1);
    public List<IconStyle> Found = [];

    Dictionary<string, IconStyle> _found = new(StringComparer.Ordinal);
    List<IconStyle>? _foundFrom;
    int _foundCount;

    static IconStyle Row(string name, string icon, int rgb, float scale, bool show = true) =>
        new() { Name = name, Icon = icon, Colour = Color.FromArgb(0xFF, Color.FromArgb(rgb)), Scale = scale, Show = show };

    public float ZoomScale(float mapScale) =>
        ScaleWithZoom && mapScale > 0 && float.IsFinite(mapScale)
            ? Math.Clamp(MathF.Pow(mapScale / GeneralTab.DefaultZoom, ZoomScaleFactor), ZoomScaleMin, ZoomScaleMax)
            : 1;

    public IconStyle[] Fixed => [NormalMonsters, MagicMonsters, RareMonsters, Bosses, Minions, Npcs, OtherPlayers, MapObjects, Destructibles];

    public static int Layer(Entity entity) => entity.Kind switch
    {
        EntityKind.Destructible => 0,
        EntityKind.MapObject => 1,
        EntityKind.Monster when entity.Friendly => 7,
        EntityKind.Monster => 2 + (int)entity.Rarity,
        EntityKind.Ally => 6,
        EntityKind.Npc => 7,
        EntityKind.Player => 8,
        _ => 9,
    };

    public IconStyle? FixedFor(Entity entity) => entity.Kind switch
    {
        EntityKind.Monster when entity.Friendly => Npcs,
        EntityKind.Monster => entity.Rarity switch
        {
            Rarity.Normal => NormalMonsters,
            Rarity.Magic => MagicMonsters,
            Rarity.Rare => RareMonsters,
            _ => Bosses,
        },
        EntityKind.Ally => Minions,
        EntityKind.Player => OtherPlayers,
        EntityKind.Npc => Npcs,
        EntityKind.MapObject => MapObjects,
        EntityKind.Destructible => Destructibles,
        _ => null,
    };

    public IconStyle? FoundFor(string texture)
    {
        if (string.IsNullOrEmpty(texture)) return null;
        if (!ReferenceEquals(_foundFrom, Found) || _foundCount != Found.Count)
        {
            _found.Clear();
            foreach (var row in Found)
                if (row?.Key is { Length: > 0 }) _found.TryAdd(row.Key, row);
            _foundFrom = Found;
            _foundCount = Found.Count;
        }
        return _found.GetValueOrDefault(texture);
    }

    public void Discover(MapIcon[] icons)
    {
        foreach (var icon in icons)
        {
            if (string.IsNullOrEmpty(icon.Texture)) continue;
            Apply(icon.Texture, IconPresets.NameFor(icon.Texture) ?? icon.Texture, icon.Name);
        }
    }

    public void SeedPresets()
    {
        foreach (var (texture, name) in IconPresets.All) Apply(texture, name, "");
    }

    void Apply(string texture, string name, string gameName)
    {
        if (FoundFor(texture) is not { } existing) AddFound(texture, name);
        else if (existing.Name == existing.Key || existing.Name == gameName || IconPresets.IsInternalName(existing.Name)) existing.Name = name;
    }

    void AddFound(string texture, string name)
    {
        var row = new IconStyle { Key = texture, Name = name, Icon = EmberHUD.Draw.GameIconPrefix + texture };
        Found.Add(row);
        _found[row.Key] = row;
        _foundCount = Found.Count;
    }
}

public sealed class Settings : PluginSettings
{
    public GeneralTab General = new();
    public MapTab Map = new();
    public IconsTab Icons = new();

    static readonly IconsTab Defaults = new();
    string _filter = "";
    string? _editing;
    string _editText = "";
    bool _focusEdit;

    internal static bool Matches(IconStyle row, string filter) => Text.Matches(filter, row.Name) || Text.Matches(filter, row.Key);

    internal static void Rename(IconStyle row, string typed)
    {
        var name = typed.Trim();
        row.Name = name.Length == 0 ? row.Key : name;
    }

    public override void Draw() => Layout.TabBar("minimap",
        ("General", DrawGeneral),
        ("Map", DrawMap),
        ("Icons", DrawIcons),
        ("Hotkeys", () => { Controls.PluginHotkeys(); return false; }));

    bool DrawGeneral()
    {
        if (ImGui.Button("Re-check map rotation")) Game.RescanMinimap();
        var changed = Setting.Checkbox("Show", ref General.Show);
        changed |= Setting.Checkbox("Follow Player", ref General.FollowPlayer);
        changed |= Setting.Checkbox("Hide behind full-screen panels", ref General.HideBehindPanels, "Nothing draws while the game has a full-screen panel open, e.g. the big map or the talent tree");
        changed |= Setting.Slider("Zoom", ref General.Zoom, GeneralTab.MinZoom, GeneralTab.MaxZoom, "Pixels per world unit. Real world units are small, about 0.005 to 0.05", log: true);
        changed |= Setting.Slider("Rotation", ref General.Rotation, -180, 180, "Extra clockwise degrees added on top of the game's own minimap rotation, 0 matches the game");
        changed |= Setting.Slider("Vertical scale", ref General.VerticalScale, 0.2f, 1.5f, "Squashes the map vertically after the rotation, the game camera is isometric");
        changed |= Setting.Slider("Offset X", ref General.OffsetX, -960, 960);
        changed |= Setting.Slider("Offset Y", ref General.OffsetY, -960, 960);
        return changed;
    }

    internal bool DrawMap()
    {
        var changed = Setting.Choice("Draw style", ref Map.DrawStyle, "Region fills the walkable area like the game's Tab map - white where you've been, blue where you haven't. Walls draws the old wall lines");
        Controls.Section("Region");
        changed |= Setting.Color("Region fill", ref Map.Region.Fill, "Fill of the walkable area you haven't explored yet");
        changed |= Setting.Color("Region outline", ref Map.Region.Outline, "Outline around the whole walkable area");
        changed |= Setting.Slider("Region outline width", ref Map.Region.OutlineWidth, 0.5f, 6);
        changed |= Setting.Color("Explored fill", ref Map.Region.ExploredFill, "Fill of the area you've been through");
        changed |= Setting.Color("Explored outline", ref Map.Region.ExploredOutline, "Outline around the explored area and the islands inside it");
        changed |= Setting.Slider("Explored outline width", ref Map.Region.ExploredOutlineWidth, 0.5f, 6);
        Controls.Section("Walls");
        changed |= Setting.Color("Wall colour", ref Map.Walls.Colour, "Only used by the Walls draw style");
        changed |= Setting.Slider("Wall thickness", ref Map.Walls.Thickness, 0.5f, 6, "Only used by the Walls draw style");
        Controls.Section("Player");
        changed |= Setting.Color("Colour", ref Map.Player.Colour, id: "player colour");
        changed |= Setting.Slider("Size", ref Map.Player.Size, 4, 30, id: "player size");
        return changed;
    }

    internal bool DrawIcons()
    {
        Icons.SeedPresets();
        var changed = Setting.Checkbox("Game icons", ref Icons.UseGameIcons, "Draw the game's own minimap icons when GameIcons are set up, shapes otherwise");
        changed |= Setting.Slider("Game icon scale", ref Icons.GameIconScale, 0.25f, 4, "Scales every game icon on top of its own row scale");
        changed |= Setting.Checkbox("Scale with zoom", ref Icons.ScaleWithZoom, "Icons grow and shrink with the map zoom, the default zoom keeps them at their own size");
        changed |= Setting.Slider("Zoom scale factor", ref Icons.ZoomScaleFactor, 0, 1, "How strongly icons follow the zoom - 1 matches the map exactly, 0 keeps them a fixed size");
        changed |= Setting.Slider("Zoom scale min", ref Icons.ZoomScaleMin, 0.1f, 1, "Smallest icon multiplier when zoomed out");
        changed |= Setting.Slider("Zoom scale max", ref Icons.ZoomScaleMax, 1, 4, "Largest icon multiplier when zoomed in");
        Controls.Section("Icons");
        Controls.Filter(ref _filter, width: 240);
        ImGui.SameLine();
        ImGui.TextDisabled($"{Icons.Fixed.Length} fixed + {Icons.Found.Count} found");
        if (!ImGui.BeginTable("##icons", 5, ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.ScrollY | ImGuiTableFlags.SizingFixedFit, new Vector2(0, Math.Max(360, ImGui.GetContentRegionAvail().Y)))) return changed;
        ImGui.TableSetupScrollFreeze(0, 1);
        ImGui.TableSetupColumn("Show", ImGuiTableColumnFlags.WidthFixed);
        ImGui.TableSetupColumn("Icon", ImGuiTableColumnFlags.WidthFixed);
        ImGui.TableSetupColumn("Name", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("Scale", ImGuiTableColumnFlags.WidthFixed);
        ImGui.TableSetupColumn("Override game icon", ImGuiTableColumnFlags.WidthFixed);
        ImGui.TableHeadersRow();
        GroupRow("Fixed");
        var fixedRows = Icons.Fixed;
        var fixedDefaults = Defaults.Fixed;
        for (var i = 0; i < fixedRows.Length; i++)
            if (Matches(fixedRows[i], _filter)) changed |= DrawRow(fixedRows[i], "f" + i, fixedDefaults[i]);
        GroupRow("Found in game");
        foreach (var row in Icons.Found)
            if (row?.Key is not null && Matches(row, _filter)) changed |= DrawRow(row, row.Key, null);
        ImGui.EndTable();
        return changed;
    }

    static void GroupRow(string label)
    {
        ImGui.TableNextRow();
        ImGui.TableSetColumnIndex(0);
        ImGui.TextDisabled(label);
    }

    bool DrawRow(IconStyle row, string id, IconStyle? fixedDefault)
    {
        var fixedRow = fixedDefault is not null;
        var changed = false;
        ImGui.PushID(id);
        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        changed |= ImGui.Checkbox("##show", ref row.Show);
        ImGui.TableNextColumn();
        changed |= Controls.IconPicker("icon", ref row.Icon, ref row.Colour, ref row.Scale, 0.1f, 10f, resetIcon: fixedDefault?.Icon ?? EmberHUD.Draw.GameIconPrefix + row.Key, resetColour: fixedDefault?.Colour, resetScale: fixedDefault?.Scale ?? 1, baseSize: fixedRow ? Main.MarkerRadius * 2 : 48);
        using (Layout.Dim(!row.Show))
        {
            ImGui.TableNextColumn();
            if (fixedRow) ImGui.TextUnformatted(row.Name);
            else changed |= DrawName(row);
            ImGui.TableNextColumn();
            ImGui.TextUnformatted($"{row.Scale:0.00}");
            ImGui.TableNextColumn();
            if (fixedRow)
            {
                changed |= ImGui.Checkbox("##override", ref row.OverrideGameIcon);
                Text.Tip("Draw this row instead of the game's own icon for these entities. Hidden + override hides both");
            }
        }
        ImGui.PopID();
        return changed;
    }

    bool DrawName(IconStyle row)
    {
        if (_editing != row.Key)
        {
            if (ImGui.Selectable(row.Name))
            {
                if (_editing is { } prev && Icons.FoundFor(prev) is { } pending) Rename(pending, _editText);
                _editing = row.Key;
                _editText = row.Name;
                _focusEdit = true;
            }
            Text.Tip(row.Key);
            return false;
        }
        if (_focusEdit)
        {
            ImGui.SetKeyboardFocusHere();
            _focusEdit = false;
        }
        var escaped = ImGui.IsKeyPressed(ImGuiKey.Escape);
        var entered = ImGui.InputText("##name", ref _editText, 64, ImGuiInputTextFlags.EnterReturnsTrue | ImGuiInputTextFlags.AutoSelectAll);
        if (escaped)
        {
            _editing = null;
            return false;
        }
        if (!entered && !ImGui.IsItemDeactivated()) return false;
        Rename(row, _editText);
        _editing = null;
        return true;
    }
}
