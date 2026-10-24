using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using EmberHUD;
using EmberHUD.Ui;

namespace EmberAuras;

public enum ConditionKind { Buff, SkillCooldown, SkillCharges, Health, Shield, HealthAndShield }

public enum Direction { Below, Above }

public sealed class Condition
{
    public ConditionKind Kind;
    public float Percent = 50;
    public Direction Direction;
    public string Text = "";
    public bool Missing;
    public bool Ending;
    public float Seconds = 1;
    public int MinStacks;
    public int SkillCfgId;
    public bool Ready = true;
    public int Charges = 1;
}

public enum AuraStyle { Icon, Bar }

public enum BarIcon { None, Left, Right }

public enum WhenInactive { Hide, Dim }

public enum TextField { Name, Stacks, Duration, Charges, Percent }

public sealed class AuraText
{
    public TextField Field;
    public bool Enabled;
    public Anchor Anchor;
}

public sealed class Aura
{
    public bool Enabled = true;
    public string Name = "";
    public AuraStyle Style;
    public BarIcon BarIcon = BarIcon.Left;
    public int IconSize = 40;
    public int BarWidth = 200;
    public int BarHeight = 22;
    public string Icon = "";
    public Color FillColor = Color.FromArgb(0xFF, 0xC8, 0x32, 0x32);
    public IconShape Shape;
    public int CornerRadius = 8;
    public int BorderWidth;
    public Color BorderColor = Color.White;
    public Color BackdropColor = Color.Transparent;
    public WhenInactive WhenInactive;
    public List<Condition> Conditions = [];
    public List<AuraText> Texts = [];
}

public enum Growth { Right, Left, Down, Up, OutwardHorizontal, OutwardVertical }

public enum GroupAlign { Start, Center, End }

public sealed class Group
{
    public bool Enabled = true;
    public string Name = "";
    public int X = 400;
    public int Y = 600;
    public Growth Growth;
    public GroupAlign Align;
    public int Spacing = 4;
    public List<Aura> Auras = [];
}

public sealed class Settings : PluginSettings
{
    public bool Lock = true;
    public Hotkey MoveKey = new(Key.Alt);
    public bool HideOnFullScreen = true;
    public List<Group> Groups = [];

    static readonly TextField[] TextFields = Enum.GetValues<TextField>();

    static bool TextsClean(List<AuraText>? texts)
    {
        if (texts is null || texts.Count != TextFields.Length) return false;
        for (var i = 0; i < TextFields.Length; i++)
            if (texts[i] is null || texts[i].Field != TextFields[i]) return false;
        return true;
    }

    static AuraText DefaultText(TextField field) => field switch
    {
        TextField.Name => new AuraText { Field = field, Anchor = Anchor.TopLeft },
        TextField.Stacks or TextField.Charges => new AuraText { Field = field, Enabled = true, Anchor = Anchor.BottomRight },
        _ => new AuraText { Field = field, Enabled = true, Anchor = Anchor.Center },
    };

    public void Normalize()
    {
        Groups ??= [];
        Groups.RemoveAll(group => group is null);
        foreach (var group in Groups)
        {
            group.Name ??= "";
            group.Auras ??= [];
            group.Auras.RemoveAll(aura => aura is null);
            foreach (var aura in group.Auras)
            {
                aura.Name ??= "";
                aura.Icon ??= "";
                aura.Conditions ??= [];
                aura.Conditions.RemoveAll(condition => condition is null);
                foreach (var condition in aura.Conditions) condition.Text ??= "";
                if (!TextsClean(aura.Texts))
                {
                    var texts = (aura.Texts ?? []).Where(text => text is not null).GroupBy(text => text.Field).ToDictionary(g => g.Key, g => g.First());
                    aura.Texts = TextFields.Select(field => texts.TryGetValue(field, out var text) ? text : DefaultText(field)).ToList();
                }
            }
        }
    }

    public override void Draw() => SettingsUI.Draw(this);
}
