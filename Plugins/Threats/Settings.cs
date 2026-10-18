using System;
using System.Drawing;
using EmberHUD;

namespace Threats;

public enum LineStyle { Solid, Gradient, Animated }

public sealed partial class Settings : PluginSettings
{
    public bool Show = true;
    public bool HideBehindPanels = true;

    public bool TelegraphNormal;
    public bool TelegraphMagic;
    public bool TelegraphRare = true;
    public bool TelegraphBoss = true;
    public bool LinesNormal;
    public bool LinesMagic;
    public bool LinesRare = true;
    public bool LinesBoss = true;

    public float PlaceholderRadius = 250;
    public Color TelegraphColour = Color.FromArgb(230, 255, 70, 30);
    public Color TelegraphFillColour = Color.FromArgb(255, 255, 140, 30);
    public float FillOpacity = 0.45f;
    public bool ShowCountdown = true;
    public float CountdownSize = 120;
    public bool HideShortCasts;
    public float MinCastSeconds = 1.0f;

    public LineStyle Style = LineStyle.Gradient;
    public float LineLength = 1500;
    public float LineWidth = 4;
    public float RadiusScale = 0.25f;
    public bool ShowHitWidth = true;
    public Color LineColour = Color.FromArgb(230, 60, 220, 255);
    public float AnimationSpeed = 600;

    public bool GroundAreas = true;
    public Color AreaColour = Color.FromArgb(200, 230, 120, 40);
    public float GroundAlertSeconds = 1.0f;

    public Color DangerColour = Color.FromArgb(255, 255, 40, 120);
    public bool MoveAlert;
    public string MoveAlertText = "MOVE";
    public float MoveAlertSize = 48;

    public bool TooShort(Cast cast)
    {
        var total = cast.Progress < 1 ? cast.SecondsLeft / (1 - cast.Progress) : cast.SecondsLeft;
        return HideShortCasts && total > 0 && MathF.Round(total, 2) < MinCastSeconds;
    }

    public bool Allows(Rarity rarity, bool telegraphs) => rarity switch
    {
        Rarity.Normal => telegraphs ? TelegraphNormal : LinesNormal,
        Rarity.Magic => telegraphs ? TelegraphMagic : LinesMagic,
        Rarity.Rare => telegraphs ? TelegraphRare : LinesRare,
        _ => telegraphs ? TelegraphBoss : LinesBoss,
    };
}
