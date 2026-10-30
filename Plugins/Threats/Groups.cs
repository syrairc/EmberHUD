using System;
using System.Drawing;
using EmberHUD;

namespace Threats;

public static class Groups
{
    public const string Hazards = "threats.hazards";
    public const string HazardsDanger = "threats.hazards.danger";
    public const string TelegraphFill = "threats.telegraph";
    public const string TelegraphFillDanger = "threats.telegraph.danger";
    public const string Bands = "threats.bands";
    public const string BandsDanger = "threats.bands.danger";
    public const string Lines = "threats.lines";
    public const string LinesDanger = "threats.lines.danger";
    public const string BandsHoming = "threats.bands.homing";
    public const string LinesHoming = "threats.lines.homing";

    public static Color Fill(Color colour, Settings settings)
    {
        var opacity = float.IsFinite(settings.FillOpacity) ? Math.Clamp(settings.FillOpacity, 0f, 1f) : 0f;
        return Color.FromArgb((int)Math.Clamp(colour.A * opacity, 0f, 255f), colour);
    }

    public static Color Band(Color colour) => Color.FromArgb(colour.A / 4, colour);

    public static void Declare(Settings settings)
    {
        var none = Color.Transparent;
        Ground.Group(Hazards, settings.AreaColour, Fill(settings.AreaColour, settings), 3, HazardsDanger);
        Ground.Group(HazardsDanger, settings.DangerColour, Fill(settings.DangerColour, settings), 3);
        Ground.Group(TelegraphFill, none, Fill(settings.TelegraphFillColour, settings), 0, TelegraphFillDanger);
        Ground.Group(TelegraphFillDanger, none, Fill(settings.DangerColour, settings), 0);
        Ground.Group(Bands, none, Band(settings.LineColour), 0, BandsDanger);
        Ground.Group(BandsDanger, none, Band(settings.DangerColour), 0);
        Ground.Group(BandsHoming, none, Band(settings.HomingColour), 0, BandsDanger);
        Ground.Group(LinesHoming, none, settings.HomingColour, 0, LinesDanger);
        Ground.Group(Lines, none, settings.LineColour, 0, LinesDanger);
        Ground.Group(LinesDanger, none, settings.DangerColour, 0);
    }
}
