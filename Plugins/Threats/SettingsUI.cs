using EmberHUD;
using EmberHUD.Ui;
using Hexa.NET.ImGui;

namespace Threats;

public sealed partial class Settings
{
    public override void Draw()
    {
        Setting.Checkbox("Show", ref Show);
        Setting.Checkbox("Hide behind full screen panels", ref HideBehindPanels);

        Controls.Section("Telegraphs");
        RarityRow("Monsters", ref TelegraphNormal, ref TelegraphMagic, ref TelegraphRare, ref TelegraphBoss, "tg");
        Setting.Slider("Placeholder radius", ref PlaceholderRadius, 50, 1000, "v1 area is a placeholder circle at the locked target point, NOT the real hit box");
        Setting.Color("Outline colour", ref TelegraphColour);
        Setting.Color("Fill colour", ref TelegraphFillColour);
        Setting.Slider("Fill opacity", ref FillOpacity, 0, 1);
        Setting.Checkbox("Show countdown", ref ShowCountdown);
        Setting.Slider("Countdown size", ref CountdownSize, 20, 400, "World units tall");

        Controls.Section("Projectile lines");
        RarityRow("Monsters", ref LinesNormal, ref LinesMagic, ref LinesRare, ref LinesBoss, "ln");
        Setting.Choice("Style", ref Style);
        Setting.Slider("Line length", ref LineLength, 200, 5000);
        Setting.Slider("Line width", ref LineWidth, 1, 20);
        Setting.Slider("Radius scale", ref RadiusScale, 0, 1, "Multiplier on the projectile radius that sizes the danger zone around the line");
        Setting.Checkbox("Show hit width", ref ShowHitWidth, "Faint band under each line, as wide as its danger zone (projectile radius x Radius scale)");
        Setting.Color("Line colour", ref LineColour);
        Setting.Slider("Animation speed", ref AnimationSpeed, 50, 3000, "Only used by the Animated style");

        Controls.Section("Danger");
        Setting.Color("Danger colour", ref DangerColour);
        Setting.Checkbox("Move alert", ref MoveAlert);
        Setting.Input("Move alert text", ref MoveAlertText);
        Setting.Slider("Move alert size", ref MoveAlertSize, 12, 200);

        Controls.Section("Hotkeys");
        Controls.PluginHotkeys();
    }

    static void RarityRow(string label, ref bool normal, ref bool magic, ref bool rare, ref bool boss, string id)
    {
        ImGui.TextUnformatted(label);
        ImGui.SameLine();
        Setting.Checkbox("Normal", ref normal, null, $"{id}n");
        ImGui.SameLine();
        Setting.Checkbox("Magic", ref magic, null, $"{id}m");
        ImGui.SameLine();
        Setting.Checkbox("Rare", ref rare, null, $"{id}r");
        ImGui.SameLine();
        Setting.Checkbox("Boss", ref boss, null, $"{id}b");
    }
}
