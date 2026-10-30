using System;
using System.Collections.Generic;
using System.Drawing;
using EmberHUD;
using EmberHUD.Ui;

namespace NameplatesPlugin;

public enum Bucket { Normal, Magic, Rare, Boss, Ally, Minion, Npc, Self }

public enum TextRow { Above, OnBar, Below }

public enum MatchKind { Name, CfgId }

public sealed class BarStyle
{
    public float Width = 100;
    public float Height = 12;
    public float BorderWidth = 1;
    public Color Fill = Color.FromArgb(0xFF, 0xC8, 0x32, 0x32);
    public Color Back = Color.FromArgb(0xA0, 0, 0, 0);
    public Color Border = Color.FromArgb(0xFF, 0, 0, 0);
    public string Texture = "";

    public BarStyle Clone() => (BarStyle)MemberwiseClone();
}

public sealed class PlateStyle
{
    public BarStyle Bar = new();
    public TextLine Above = new() { Enabled = true, Template = "{name}" };
    public TextLine OnBar = new() { Enabled = true, Template = "{hp%}%" };
    public TextLine Below = new();
    public float OffsetY;
    public bool DrawBar = true;

    public PlateStyle Clone() => new() { DrawBar = DrawBar, Bar = Bar.Clone(), Above = Above.Clone(), OnBar = OnBar.Clone(), Below = Below.Clone(), OffsetY = OffsetY };
}

public sealed class BucketSettings
{
    public bool Show = true;
    public bool DamagedOnly;
    public PlateStyle Style = new();
    public CastBarSettings? CastBar;
}

public sealed class CastBarSettings
{
    public bool Enabled;
    public float OffsetY = 4;
    public BarStyle Bar = new() { Width = 100, Height = 14, Fill = Color.FromArgb(0xFF, 0xFF, 0xA0, 0x28) };
    public TextLine Text = new() { Enabled = true, Template = "{cast} {left}s" };

    public CastBarSettings Clone() => new() { Enabled = Enabled, OffsetY = OffsetY, Bar = Bar.Clone(), Text = Text.Clone() };
}

public sealed class StyleOverride
{
    public bool Enabled = true;
    public MatchKind Kind;
    public string Name = "";
    public int CfgId;
    public PlateStyle Style = new();
}

public sealed class Settings : PluginSettings
{
    static readonly Color Hostile = Color.FromArgb(0xFF, 0xC8, 0x32, 0x32);
    static readonly Color Friendly = Color.FromArgb(0xFF, 0x3C, 0xB4, 0x50);
    static readonly Color Neutral = Color.FromArgb(0xFF, 0x96, 0x96, 0x96);

    public float DistanceUnit = 100;
    public float MaxDistance = 25;
    public bool HideBehindPanels = true;
    public bool AnchorToGameBar;
    public bool HideSummoned;
    public bool HideSpecialSpawns;
    public bool HideMinions;
    public TextLine BossBarHp = new() { Template = "{hp} / {hpmax}" };
    public TextRow BossBarHpRow = TextRow.OnBar;
    public CastBarSettings? CastBar;
    public BucketSettings[] Buckets = DefaultBuckets();
    public List<StyleOverride> Overrides = new();

    static BucketSettings[] DefaultBuckets()
    {
        var buckets = new BucketSettings[Enum.GetValues<Bucket>().Length];
        buckets[(int)Bucket.Normal] = Plate(100, 12, Hostile);
        buckets[(int)Bucket.Magic] = Plate(100, 12, Color.FromArgb(0xFF, 0x50, 0x78, 0xE6));
        buckets[(int)Bucket.Rare] = Plate(120, 14, Color.FromArgb(0xFF, 0xE6, 0xC8, 0x32));
        buckets[(int)Bucket.Boss] = Plate(160, 18, Color.FromArgb(0xFF, 0xE6, 0x78, 0x28));
        buckets[(int)Bucket.Ally] = Plate(80, 10, Friendly);
        buckets[(int)Bucket.Minion] = Plate(60, 8, Friendly, show: false);
        buckets[(int)Bucket.Npc] = Plate(80, 10, Neutral, show: false);
        buckets[(int)Bucket.Self] = Plate(80, 10, Friendly);
        return buckets;
    }

    static BucketSettings Plate(float width, float height, Color fill, bool show = true)
    {
        var bucket = new BucketSettings { Show = show, CastBar = new() };
        bucket.Style.Bar.Width = width;
        bucket.Style.Bar.Height = height;
        bucket.Style.Bar.Fill = fill;
        return bucket;
    }

    internal bool AnyCastBar()
    {
        foreach (var bucket in Buckets)
            if (bucket.CastBar?.Enabled == true) return true;
        return false;
    }

    internal float Displayed(float worldDistance) => worldDistance / (float.IsFinite(DistanceUnit) && DistanceUnit > 0 ? DistanceUnit : 100);

    internal void Normalize()
    {
        var defaults = DefaultBuckets();
        var buckets = new BucketSettings[defaults.Length];
        for (var i = 0; i < buckets.Length; i++) buckets[i] = Buckets is not null && i < Buckets.Length && Buckets[i] is { } bucket ? bucket : defaults[i];
        Buckets = buckets;
        foreach (var bucket in Buckets)
        {
            bucket.Style = Normalize(bucket.Style);
            var cast = bucket.CastBar ??= CastBar?.Clone() ?? new();
            cast.Bar ??= new();
            cast.Bar.Texture ??= "";
            cast.Text ??= new();
            cast.Text.Template ??= "";
            cast.Text.Style ??= TextStyle.Hud();
        }
        CastBar = null;
        BossBarHp ??= new() { Template = "{hp} / {hpmax}" };
        BossBarHp.Template ??= "";
        BossBarHp.Style ??= TextStyle.Hud();
        Overrides ??= new();
        Overrides.RemoveAll(row => row is null);
        foreach (var row in Overrides)
        {
            row.Name ??= "";
            row.Style = Normalize(row.Style);
        }
    }

    static PlateStyle Normalize(PlateStyle? style)
    {
        style ??= new();
        style.Bar ??= new();
        style.Bar.Texture ??= "";
        style.Above ??= new();
        style.OnBar ??= new();
        style.Below ??= new();
        foreach (var line in new[] { style.Above, style.OnBar, style.Below })
        {
            line.Template ??= "";
            line.Style ??= TextStyle.Hud();
        }
        return style;
    }

    public override void Draw() => SettingsUI.Draw(this);
}
