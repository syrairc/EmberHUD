using System.Drawing;
using EmberHUD;
using EmberHUD.Ui;

namespace NavigationPlugin;

public enum GroundShape { Streak, Chevron, Dot }

public sealed class GeneralTab
{
    public bool Show = true;
    public bool Ground = true;
    public bool Minimap = true;
    public bool AvoidMapObjects = true;
    public float ObjectRadius = 110;
}

public sealed class SurfaceStyle
{
    public float Size = 3;
    public float Spacing = 400;
    public float Speed = 300;
    public float Opacity = 0.9f;
    public float Trail = 150;
    public float BaseLine = 1;
    public bool OverrideColour;
    public Color Colour = Color.White;

    internal Color ColourFor(PathState path) => OverrideColour ? Colour : path.Colour;
}

public sealed class LegendTab
{
    internal const int MinWidth = 120;
    internal const int MaxWidth = 800;

    public bool Show = true;
    public bool Lock = true;
    public Hotkey MoveKey = new(Key.Alt);
    public bool HideBehindPanels = true;
    public int X = 1640;
    public int Y = 520;
    public int Width = 240;
    public TextStyle Text = TextStyle.Hud();
}

public sealed class CompassTab
{
    public bool Show = true;
    public float Radius = 250;
    public float Size = 80;
    public float Opacity = 0.9f;
    public float Lookahead = 600;
}

public sealed class Settings : PluginSettings
{
    public GeneralTab General = new();
    public GroundShape GroundShape = GroundShape.Streak;
    public SurfaceStyle GroundStyle = new() { Size = 25, Spacing = 250, Speed = 400, Trail = 200, BaseLine = 2 };
    public SurfaceStyle MinimapStyle = new();
    public LegendTab Legend = new();
    public CompassTab Compass = new();

    public override void Draw() => Layout.TabBar("navigation",
        ("General", DrawGeneral),
        ("Ground", DrawGround),
        ("Minimap", DrawMinimap),
        ("Compass", DrawCompass),
        ("Legend", DrawLegend),
        ("Hotkeys", Controls.PluginHotkeys));

    internal void DrawGround()
    {
        Setting.Choice("Shape", ref GroundShape, "Streak = glowing comet, Chevron = flat arrowhead, Dot = round particle. All lie flat on the ground", id: "GroundShape");
        DrawStyle("Ground", GroundStyle, "Size of the shape in world units");
    }

    internal void DrawMinimap() => DrawStyle("Minimap", MinimapStyle, "Particle radius in pixels");

    internal void DrawLegend()
    {
        Setting.Checkbox("Show", ref Legend.Show, "A movable list of every path with its colour, name and distance", id: "LegendShow");
        Setting.Checkbox("Lock", ref Legend.Lock, "A locked panel ignores the mouse - hold the move key to drag or resize it anyway");
        Setting.Hotkey("Move key", ref Legend.MoveKey, "Hold to move or resize a locked panel");
        Setting.Checkbox("Hide behind full-screen panels", ref Legend.HideBehindPanels, "Nothing draws while the game has a full-screen panel open, e.g. the big map");
        Setting.Slider("X", ref Legend.X, 0, 7680, id: "LegendX");
        Setting.Slider("Y", ref Legend.Y, 0, 4320, id: "LegendY");
        Setting.Slider("Width", ref Legend.Width, LegendTab.MinWidth, LegendTab.MaxWidth, id: "LegendWidth");
        Setting.Style("Text", Legend.Text);
    }

    internal void DrawCompass()
    {
        Setting.Checkbox("Show", ref Compass.Show, "A ring of arrows around you, one per objective in its path colour, pointing along the route (dimmed when no path was found)", id: "CompassShow");
        Setting.Slider("Radius", ref Compass.Radius, 50, 1000, "World units from you to the arrows, also the distance from an objective where its arrow hides", id: "CompassRadius");
        Setting.Slider("Size", ref Compass.Size, 10, 300, "Length of each arrow in world units", id: "CompassSize");
        Setting.Slider("Opacity", ref Compass.Opacity, 0.05f, 1, "How solid the arrows are", id: "CompassOpacity");
        Setting.Slider("Lookahead", ref Compass.Lookahead, 1, 3000, "World units along the route the arrows aim at, so they follow bends instead of pointing straight at the objective", id: "CompassLookahead");
    }

    internal void DrawGeneral()
    {
        Setting.Checkbox("Show", ref General.Show, "Master switch for everything this plugin draws, the legend and compass included", id: "GeneralShow");
        Setting.Checkbox("Ground", ref General.Ground, "Particles flow along the path on the ground in the game world");
        Setting.Checkbox("Minimap", ref General.Minimap, "Particles flow along the path on the Minimap plugin's map");
        Setting.Checkbox("Avoid map objects", ref General.AvoidMapObjects, "Map objects such as the road blockers are invisible to the walkability grid, so each one is treated as a disc the route goes around. Off = ignore them", id: "AvoidMapObjects");
        Setting.Slider("Object radius", ref General.ObjectRadius, 0, 400, "How wide the disc around each map object is, in world units. 0 = ignore map objects", id: "ObjectRadius");
    }

    static void DrawStyle(string name, SurfaceStyle style, string sizeTip)
    {
        Setting.Slider("Size", ref style.Size, 0.5f, 100, sizeTip, id: name + "Size");
        Setting.Slider("Spacing", ref style.Spacing, 20, 2000, "World units between particles", id: name + "Spacing");
        Setting.Slider("Speed", ref style.Speed, 0, 2000, "World units per second", id: name + "Speed");
        Setting.Slider("Opacity", ref style.Opacity, 0.05f, 1, "How solid the particles and line are", id: name + "Opacity");
        Setting.Slider("Trail", ref style.Trail, 0, 1000, "Length in world units of the fading tail behind each particle, 0 = off", id: name + "Trail");
        Setting.Slider("Base line", ref style.BaseLine, 0, 10, "Thickness in pixels of the faint line under the particles, 0 = off", id: name + "BaseLine");
        Controls.OverrideRow(name + "Colour", "Colour", ref style.OverrideColour, () => Controls.Color("##colour", ref style.Colour, name + "ColourPick", alpha: false), "Draw every path in this one colour instead of its own. Opacity still applies.");
    }
}
