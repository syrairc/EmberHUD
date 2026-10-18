using EmberHUD;
using EmberHUD.Ui;
using Hexa.NET.ImGui;

namespace Pickit;

static class SettingsUI
{
    public static void Draw(Settings s)
    {
        s.Normalize();
        Layout.TabBar("pickit", ("Autoloot", () => Autoloot(s)), ("Affixes", () => Affixes(s)), ("Bag Space", () => SpaceTab(s)));
    }

    static void SpaceTab(Settings s)
    {
        Setting.Checkbox("Bag space overlay", ref s.SpaceEnabled, "Shows used and free slots per bag");
        for (var bag = 0; bag < Settings.BagCount; bag++) Setting.Checkbox(Space.BagName(bag), ref s.SpaceBags[bag], "Show this bag", $"space-bag-{bag}");
        Setting.Checkbox("Bar graph", ref s.SpaceBar, "A segment per slot, green when empty to red when full - off shows text only");
        if (s.SpaceBar) Setting.Checkbox("Labels", ref s.BarLabels, "Bag name, right aligned, in front of each bar");
        Setting.Style("Text style", s.SpaceText, "Style of the bag text and bar labels", "space-text");
        Setting.Checkbox("Lock", ref s.SpaceLock, "A locked overlay ignores the mouse - hold the move key to drag it anyway");
        Setting.Hotkey("Move key", ref s.SpaceMoveKey, "Hold to move a locked overlay", "space-movekey");
        Setting.Slider("X", ref s.SpaceX, 0, 7680);
        Setting.Slider("Y", ref s.SpaceY, 0, 4320);
        if (!s.SpaceBar) return;
        Setting.Slider("Segment width", ref s.BarSegmentWidth, 1, 50, "Pixels", fmt: "%.0f");
        Setting.Slider("Segment height", ref s.BarSegmentHeight, 1, 100, "Pixels", fmt: "%.0f");
        Setting.Slider("Segment spacing", ref s.BarSpacing, 0, 20, "Pixels between segments", fmt: "%.0f");
        Setting.Slider("Segments per row", ref s.BarRowLength, 0, 200, "0 keeps each bag on one row");
        Setting.Color("Empty segment", ref s.BarEmpty, "Colour of segments that are free");
    }

    static void Autoloot(Settings s)
    {
        Setting.Checkbox("Hold key", ref s.HoldKeyEnabled, "Presses the pickup key for you while this key is held");
        if (s.HoldKeyEnabled) Setting.Hotkey("Hold key", ref s.HoldKey, "Modifiers are ignored", "holdkey");
        Setting.Checkbox("Near items", ref s.NearEnabled, "Presses the pickup key while an item you can pick up is within the radius");
        if (s.NearEnabled) Setting.Slider("Radius", ref s.NearRadius, 50, 3000, "Ground distance from the player");
        Setting.Hotkey("Pickup key", ref s.PickupKey, "The key the game uses to pick up - modifiers are ignored", "pickupkey");
    }

    static void Affixes(Settings s)
    {
        Setting.Checkbox("Affixes (ground)", ref s.GemsEnabled, "Draws an icon per prefix (left) and suffix (right) on the item labels the game shows");
        Setting.Checkbox("Tier number (ground)", ref s.GroundTierNumbers, "Draws the affix tier number in the centre of each ground icon");
        if (s.GroundTierNumbers) Setting.Style("Ground number style", s.GroundNumbers, "Tier number text on ground icons - size scales with the icon", "ground-number");
        Setting.Checkbox("Affixes (inventory and stash)", ref s.InventoryEnabled, "Draws an icon per prefix (left) and suffix (right) on each item in the open bag and stash");
        Setting.Checkbox("Tier number (inventory and stash)", ref s.InventoryTierNumbers, "Draws the affix tier number in the centre of each bag and stash icon");
        if (s.InventoryTierNumbers) Setting.Style("Inventory number style", s.InventoryNumbers, "Tier number text on bag and stash icons - size scales with the icon", "inventory-number");
        Setting.Slider("Global scale", ref s.GlobalScale, 0.25f, 3, "Multiplies every icon size, on top of the ground and inventory scales", fmt: "%.2f");
        Setting.Slider("Ground scale", ref s.GroundScale, 0.25f, 3, "Multiplies the icon size on ground labels", fmt: "%.2f");
        Setting.Slider("Inventory scale", ref s.InventoryScale, 0.25f, 3, "Multiplies the icon size in the bag and stash", fmt: "%.2f");
        Setting.Slider("Spacing", ref s.GemSpacing, 0, 2, "Fraction of the icon size", fmt: "%.2f");
        Setting.Slider("Anchor Y", ref s.AnchorY, 0, 1, "0 is the top edge of the label, 1 the bottom edge", fmt: "%.2f");
        Setting.Slider("Inset X", ref s.InsetX, 0, 200, "Pixels inward from the label corner");
        Setting.Checkbox("Debug rects", ref s.DebugRects, "Outlines every label rectangle");
        for (var i = 0; i < s.Tiers.Count; i++)
        {
            var tier = s.Tiers[i];
            var reset = Settings.DefaultTier(i);
            ImGui.PushID(i);
            Controls.IconPicker("icon", ref tier.Icon, ref tier.Color, ref tier.Scale, 0.1f, 10, reset.Icon, reset.Color, reset.Scale, baseSize: 24);
            ImGui.SameLine();
            ImGui.TextUnformatted($"T{(i == 0 ? "0 / unknown" : i)}");
            ImGui.PopID();
        }
    }
}
