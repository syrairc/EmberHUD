using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Numerics;
using EmberHUD;
using EmberHUD.Ui;

namespace Pickit;

public sealed class Main : Plugin<Settings>
{
    readonly Autoloot _autoloot = new();
    readonly TextStyle _numberStyle = new();
    readonly Overlay _space = new();
    Vector2 _spaceSize;

    public override void Initialize() => Settings.Normalize();

    public override void Tick()
    {
        if (Settings.NearEnabled || Settings.GemsEnabled) Game.WantGroundItems();
        _autoloot.Step(Settings, Game.State, Input.IsDown, Send.Down, Send.Up, Send.Allowed);
    }

    public override void Render()
    {
        DrawGround();
        DrawInventory();
        DrawSpace();
    }

    void DrawSpace()
    {
        if (!Settings.SpaceEnabled) return;
        Game.WantInventory();
        if (Game.State is not { Inventory: { } items, BagCapacities: { } capacities, Ui.AnyFullScreen: false }) return;
        var rows = Space.Rows(items, capacities, Settings);
        var style = Settings.SpaceText;
        var labels = Settings.SpaceBar && Settings.BarLabels;
        var nameWidth = labels ? rows.Select(r => EmberHUD.Draw.MeasureText(r.Name, style).X).DefaultIfEmpty().Max() : 0;
        var barX = labels ? nameWidth + 8 : 0;
        var rowHeight = EmberHUD.Draw.RowHeight(style);
        _space.Locked = Settings.SpaceLock;
        _space.MoveHeld = Settings.SpaceMoveKey.Key != Key.None && Input.IsDown(Settings.SpaceMoveKey.Key);
        var origin = new Vector2(Settings.SpaceX, Settings.SpaceY);
        var (hovered, edge, active) = (false, Overlay.ResizeEdge.None, false);
        if (_spaceSize != Vector2.Zero)
        {
            var (noWidth, noHeight) = (0, 0);
            (hovered, edge, active) = _space.Handle("pickit-space", ref origin, _spaceSize, ref Settings.SpaceX, ref Settings.SpaceY, ref noWidth, 0, 0, ref noHeight, 0, 0, Overlay.ResizeEdge.None);
            if (hovered || active) Overlay.ClickBlocker("pickit-space", origin, _spaceSize);
        }

        var pos = origin;
        var width = 0f;
        foreach (var row in rows)
        {
            float rowWidth, height;
            if (Settings.SpaceBar)
            {
                var segment = new Vector2(Settings.BarSegmentWidth, Settings.BarSegmentHeight);
                if (labels) EmberHUD.Draw.Text(row.Name, new Vector2(pos.X + nameWidth, pos.Y + segment.Y / 2), style, Align.Right | Align.Middle);
                var barSize = EmberHUD.Draw.SegmentedBar(pos + new Vector2(barX, 0), row.Capacity, row.Used, segment, Settings.BarSpacing, Settings.BarRowLength, Space.Gradient, Settings.BarEmpty);
                height = Math.Max(labels ? rowHeight : 0, barSize.Y);
                rowWidth = barX + barSize.X;
            }
            else
            {
                rowWidth = EmberHUD.Draw.Text(row.Text, pos, style).X;
                height = rowHeight;
            }

            width = Math.Max(width, rowWidth);
            pos.Y += height + 2;
        }

        _spaceSize = new Vector2(width, pos.Y - origin.Y);
        if (_space.Movable) Overlay.DragHint(origin, origin + _spaceSize, active, hovered, edge);
    }

    void DrawGround()
    {
        if (!Settings.GemsEnabled || Game.State.GroundItems is not { } items || Game.State.Ui.AnyFullScreen) return;
        foreach (var item in items)
        {
            if (item.Legendary) continue;
            if (Settings.DebugRects) EmberHUD.Draw.Rect(item.LabelMin, item.LabelMax, Color.Red);
            foreach (var gem in Gems.Layout(item, Settings)) DrawGem(gem, Settings.GroundTierNumbers, Settings.GroundNumbers);
        }
    }

    void DrawInventory()
    {
        if (!Settings.InventoryEnabled) return;
        var state = Game.State;
        var kind = OpenKind(state.Ui);
        if (kind is null) return;
        Game.WantInventory();
        if (kind == PanelKind.Inventory)
        {
            if (state.Bag is { } bag && state.Inventory is { } inventory)
                DrawCells(inventory, bag.ClipMin, bag.ClipMax, bag.Cells, item => item.Bag == bag.Tab);
        }
        else if (state.Stash is { } stash && state.StashItems is { } items)
            DrawCells(items, stash.ClipMin, stash.ClipMax, stash.Cells, _ => true);
    }

    void DrawCells(IReadOnlyList<InventoryItem> items, Vector2 clipMin, Vector2 clipMax, BagCell[] cells, Func<InventoryItem, bool> onTab)
    {
        using var clip = EmberHUD.Draw.Clip(clipMin, clipMax);
        foreach (var item in items)
        {
            if (!onTab(item) || item.Legendary || item.PrefixIds.Length + item.SuffixIds.Length == 0) continue;
            foreach (var cell in cells)
            {
                if (cell.Slot != item.Slot) continue;
                foreach (var gem in Gems.CellLayout(cell.Min, cell.Max, item.PrefixIds, item.SuffixIds, Settings)) DrawGem(gem, Settings.InventoryTierNumbers, Settings.InventoryNumbers);
                break;
            }
        }
    }

    void DrawGem(Gems.Gem gem, bool tierNumber, TextStyle numbers)
    {
        var style = Settings.Tiers[Math.Clamp(gem.Tier, 0, Settings.Tiers.Count - 1)];
        EmberHUD.Draw.Icon(style.Icon, gem.Centre, gem.Radius, style.Color);
        if (!tierNumber) return;
        var text = Math.Min(gem.Tier, 99).ToString();
        _numberStyle.CopyFrom(numbers);
        _numberStyle.Scale = numbers.Scale * Math.Clamp(gem.Radius / 8, 0.75f, 4);
        EmberHUD.Draw.Text(text, gem.Centre, _numberStyle, Align.Center | Align.Middle);
    }

    static PanelKind? OpenKind(GameUi ui)
    {
        foreach (var panel in ui.Panels)
            if (panel.Kind is PanelKind.Inventory or PanelKind.Stash) return panel.Kind;
        return null;
    }

    public override void OnDisable() => _autoloot.Release(Settings, Send.Up);

    public override void Dispose() => _autoloot.Release(Settings, Send.Up);
}
