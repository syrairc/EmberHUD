using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using EmberHUD;
using EmberHUD.Ui;

namespace Pickit;

public sealed class TierStyle
{
    public string Icon = "svg:gem";
    public Color Color = Color.White;
    public float Scale = 1;
}

public sealed class Settings : PluginSettings
{
    public bool HoldKeyEnabled = true;
    public Hotkey HoldKey = new(Key.Space);
    public bool NearEnabled = true;
    public float NearRadius = 500;
    public Hotkey PickupKey = new(Key.A);
    public bool GemsEnabled = true;
    public List<TierStyle> Tiers = DefaultTiers();
    public float GlobalScale = 1;
    public float GemSpacing = 0.1f;
    public float AnchorY = 0;
    public float InsetX = 0;
    public bool InventoryEnabled = true;
    public float GroundScale = 1;
    public float InventoryScale = 1;
    public bool GroundTierNumbers = false;
    public bool InventoryTierNumbers = false;
    public TextStyle GroundNumbers = TextStyle.Speech();
    public TextStyle InventoryNumbers = TextStyle.Speech();
    public bool DebugRects = false;
    public bool SpaceEnabled = false;
    public bool[] SpaceBags = [true, true, true, true];
    public bool SpaceBar = false;
    public bool BarLabels = true;
    public TextStyle SpaceText = TextStyle.Hud();
    public bool SpaceLock = true;
    public Hotkey SpaceMoveKey = new(Key.Alt);
    public int SpaceX = 20;
    public int SpaceY = 300;
    public float BarSegmentWidth = 4;
    public float BarSegmentHeight = 12;
    public float BarSpacing = 1;
    public int BarRowLength = 0;
    public Color BarEmpty = Color.FromArgb(110, 70, 70, 70);

    public const int TierCount = 10;
    public const int BagCount = 4;

    static readonly uint[] TierColours = [0xFFFF2B2B, 0xFFFF2B2B, 0xFFD44BFF, 0xEBA95CE8, 0xD28A6BC4, 0xFF3DB2FF, 0xD75C86B3, 0xAA5F7185, 0xAA5F7185, 0xAA5F7185];

    public static TierStyle DefaultTier(int tier) => new() { Color = Color.FromArgb((int)TierColours[tier]) };

    static List<TierStyle> DefaultTiers() => [.. Enumerable.Range(0, TierCount).Select(DefaultTier)];

    public void Normalize()
    {
        NearRadius = float.IsFinite(NearRadius) ? Math.Clamp(NearRadius, 50, 3000) : 500;
        GlobalScale = Clamp(GlobalScale, 0.25f, 3, 1);
        GemSpacing = Clamp(GemSpacing, 0, 2, 0.1f);
        AnchorY = Clamp(AnchorY, 0, 1, 0);
        InsetX = Clamp(InsetX, 0, 200, 0);
        GroundScale = Clamp(GroundScale, 0.25f, 3, 1);
        InventoryScale = Clamp(InventoryScale, 0.25f, 3, 1);
        GroundNumbers = Normalized(GroundNumbers);
        InventoryNumbers = Normalized(InventoryNumbers);
        SpaceText = Normalized(SpaceText);
        SpaceBags = [.. (SpaceBags ?? []).Take(BagCount), .. Enumerable.Repeat(true, Math.Max(0, BagCount - (SpaceBags?.Length ?? 0)))];
        SpaceX = Math.Clamp(SpaceX, 0, 7680);
        SpaceY = Math.Clamp(SpaceY, 0, 4320);
        BarSegmentWidth = Clamp(BarSegmentWidth, 1, 50, 4);
        BarSegmentHeight = Clamp(BarSegmentHeight, 1, 100, 12);
        BarSpacing = Clamp(BarSpacing, 0, 20, 1);
        BarRowLength = Math.Clamp(BarRowLength, 0, 1000);
        Tiers = [.. (Tiers ?? []).Take(TierCount).Select((t, i) => t ?? DefaultTier(i))];
        while (Tiers.Count < TierCount) Tiers.Add(DefaultTier(Tiers.Count));
        foreach (var tier in Tiers)
        {
            tier.Icon ??= "svg:gem";
            tier.Scale = Clamp(tier.Scale, 0.1f, 10, 1);
        }
    }

    static TextStyle Normalized(TextStyle? style)
    {
        style ??= TextStyle.Speech();
        style.Scale = Clamp(style.Scale, 0.5f, 3, 1);
        return style;
    }

    static float Clamp(float value, float min, float max, float fallback) => float.IsFinite(value) ? Math.Clamp(value, min, max) : fallback;

    public override void Draw() => SettingsUI.Draw(this);
}
