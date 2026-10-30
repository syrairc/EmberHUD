using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using EmberHUD;
using EmberHUD.Ui;
using Hexa.NET.ImGui;

namespace EmberAuras;

static class SettingsUI
{
    const string BuffTip = "Matched against the buff name and id as a regex, e.g. shield|1234 - if it does not compile it matches the name or id exactly";
    const string FightTip = "Holds while you stand inside a hostile ground effect / buff area, the text is the time it has left.";

    static int selectedGroup;
    static int selectedAura;
    static PickerState<Buff>? buffPicker;
    static Controls.IconSheet[]? iconSheets;

    static readonly PickerOpts<Buff> BuffPickerOpts = new()
    {
        Primary = buff => buff.Name ?? "(no name)",
        Secondary = buff => buff.Remaining < buff.Total ? $"id {buff.Id} - {buff.Remaining:0.0} / {buff.Total:0.0}s left" : $"id {buff.Id} - untimed",
        Key = buff => buff.Id.ToString(),
        Size = new Vector2(360, 300),
    };

    public static void Draw(Settings s)
    {
        s.Normalize();
        Setting.Checkbox("Lock", ref s.Lock, "Unlocked panels can be dragged");
        Setting.Hotkey("Move key", ref s.MoveKey, "Hold to move a locked panel");
        Setting.Checkbox("Hide on full screen panels", ref s.HideOnFullScreen, "Hide every group while a full screen panel (map, inventory) is open");
        selectedGroup = Math.Clamp(selectedGroup, 0, Math.Max(0, s.Groups.Count - 1));
        Layout.MasterDetail("groups", 220, () => DrawGroupList(s), () => DrawGroupEditor(s));
    }

    internal static void Select(int group, int aura)
    {
        selectedGroup = group;
        selectedAura = aura;
    }

    internal static int AddGroup(Settings s)
    {
        s.Groups.Add(new Group { Name = $"Group {s.Groups.Count + 1}" });
        return s.Groups.Count - 1;
    }

    internal static void DeleteGroup(Settings s, ref int index)
    {
        if (index >= 0 && index < s.Groups.Count) s.Groups.RemoveAt(index);
        index = Math.Clamp(index, 0, Math.Max(0, s.Groups.Count - 1));
    }

    internal static int AddAura(Group group)
    {
        var holder = new Settings { Groups = [new Group { Auras = [new Aura { Name = $"Aura {group.Auras.Count + 1}" }] }] };
        holder.Normalize();
        group.Auras.Add(holder.Groups[0].Auras[0]);
        return group.Auras.Count - 1;
    }

    internal static void DeleteAura(Group group, ref int index)
    {
        if (index >= 0 && index < group.Auras.Count) group.Auras.RemoveAt(index);
        index = Math.Clamp(index, 0, Math.Max(0, group.Auras.Count - 1));
    }

    internal static Condition NewCondition() => new() { Kind = ConditionKind.Health };

    internal static string AddExactId(string text, int id) => string.IsNullOrEmpty(text) ? $"^{id}$" : $"{text}|^{id}$";

    static void DrawGroupList(Settings s)
    {
        if (ImGui.Button("+ Add")) { selectedGroup = AddGroup(s); selectedAura = 0; }
        ImGui.SameLine();
        if (ImGui.Button("Delete")) { DeleteGroup(s, ref selectedGroup); selectedAura = 0; }
        var before = selectedGroup;
        ListEditor.ReorderableList("list", s.Groups, ref selectedGroup, (group, _) => EnabledRow(ref group.Enabled, group.Name), new ListOpts { ShowIndex = false });
        if (before != selectedGroup) selectedAura = 0;
    }

    static void EnabledRow(ref bool enabled, string name)
    {
        ImGui.Checkbox("##enabled", ref enabled);
        ImGui.SameLine();
        ImGui.TextUnformatted(name.Length == 0 ? "(unnamed)" : name);
    }

    static void DrawGroupEditor(Settings s)
    {
        if (s.Groups.Count == 0)
        {
            ImGui.TextDisabled("No groups yet - use + Add on the left");
            return;
        }
        var group = s.Groups[selectedGroup];
        Setting.Input("Name", ref group.Name);
        Setting.Slider("X", ref group.X, 0, 4000, "Pixels from the left of the screen");
        Setting.Slider("Y", ref group.Y, 0, 4000, "Pixels from the top of the screen");
        Setting.Choice("Align", ref group.Align, "Start = left or top edge sits at the anchor, End = right or bottom edge, Center = centred on it. Outward growth is always centred");
        Setting.Choice("Growth", ref group.Growth);
        Setting.Slider("Spacing", ref group.Spacing, 0, 100);
        ImGui.SeparatorText("Auras");
        selectedAura = Math.Clamp(selectedAura, 0, Math.Max(0, group.Auras.Count - 1));
        Layout.MasterDetail($"auras{selectedGroup}", 200, () => DrawAuraList(group), () => DrawAuraEditor(group));
    }

    static void DrawAuraList(Group group)
    {
        if (ImGui.Button("+ Add")) selectedAura = AddAura(group);
        ImGui.SameLine();
        if (ImGui.Button("Delete")) DeleteAura(group, ref selectedAura);
        ListEditor.ReorderableList("list", group.Auras, ref selectedAura, (aura, _) => EnabledRow(ref aura.Enabled, aura.Name), new ListOpts { ShowIndex = false });
    }

    static void DrawAuraEditor(Group group)
    {
        if (group.Auras.Count == 0)
        {
            ImGui.TextDisabled("No auras yet - use + Add on the left");
            return;
        }
        var aura = group.Auras[selectedAura];
        var id = $"aura{selectedGroup}_{selectedAura}";
        Layout.TabBar(id, ("General", () => DrawGeneral(aura, id)), ("Conditions", () => DrawConditions(aura, id)), ("Text", () => DrawTexts(aura, id)));
    }

    static void DrawGeneral(Aura aura, string id)
    {
        Setting.Input("Name", ref aura.Name);
        Setting.Choice("Style", ref aura.Style);
        if (aura.Style == AuraStyle.Icon) Setting.Slider("Icon size", ref aura.IconSize, 8, 300);
        else
        {
            Setting.Choice("Bar icon", ref aura.BarIcon);
            Setting.Slider("Bar width", ref aura.BarWidth, 20, 800);
            Setting.Slider("Bar height", ref aura.BarHeight, 4, 200);
        }
        Setting.Color("Fill colour", ref aura.FillColor);
        if (aura.Style == AuraStyle.Icon || aura.BarIcon != BarIcon.None)
        {
            ImGui.SeparatorText("Icon style");
            Setting.Choice("Shape", ref aura.Shape);
            if (aura.Shape == IconShape.Rounded) Setting.Slider("Corner radius", ref aura.CornerRadius, 0, 40);
            Setting.Slider("Border width", ref aura.BorderWidth, 0, 8);
            Setting.Color("Border colour", ref aura.BorderColor);
            Setting.Color("Backdrop colour", ref aura.BackdropColor);
        }
        Setting.Choice("When inactive", ref aura.WhenInactive, "Hide removes the aura, Dim keeps it drawn faded");
        ImGui.SeparatorText("Fallback icon");
        ImGui.TextDisabled("Used when the buff or skill has no icon of its own");
        iconSheets ??= [Controls.IconSheet.GameIcons(), Controls.IconSheet.BuffIcons(), Controls.IconSheet.SkillIcons(), Controls.IconSheet.SvgIcons()];
        Controls.IconPicker($"icon{id}", iconSheets, ref aura.Icon, System.Drawing.Color.White);
        if (aura.Icon.Length > 0)
        {
            ImGui.SameLine();
            ImGui.TextUnformatted(aura.Icon);
            ImGui.SameLine();
            if (ImGui.SmallButton("Clear")) aura.Icon = "";
        }
    }

    static void DrawConditions(Aura aura, string id)
    {
        ImGui.TextDisabled("All must hold, none means it never shows");
        ListEditor.Compact($"conditions{id}", aura.Conditions, DrawCondition, AddSource<Condition>.Button(NewCondition, "+ Add condition"));
    }

    static void DrawTexts(Aura aura, string id)
    {
        foreach (var text in aura.Texts)
        {
            ImGui.PushID((int)text.Field);
            ImGui.Checkbox("##on", ref text.Enabled);
            ImGui.SameLine();
            ImGui.TextUnformatted(text.Field.ToString());
            ImGui.SameLine();
            Controls.AnchorPicker($"anchor{id}", ref text.Anchor);
            ImGui.PopID();
        }
    }

    static bool DrawCondition(Condition condition, int index)
    {
        if (index > 0) ImGui.SeparatorText("AND");
        var changed = Combo.Enum(ref condition.Kind, "kind", 150);
        ImGui.SameLine();
        switch (condition.Kind)
        {
            case ConditionKind.Health or ConditionKind.Shield or ConditionKind.HealthAndShield:
                var above = condition.Direction == Direction.Above ? 1 : 0;
                if (Combo.Option(ref above, ["Below", "Above"], "direction", 80)) { condition.Direction = above == 1 ? Direction.Above : Direction.Below; changed = true; }
                ImGui.SameLine();
                ImGui.SetNextItemWidth(170);
                changed |= ImGui.SliderFloat("##percent", ref condition.Percent, 1, 100, condition.Direction == Direction.Above ? "above %.0f%%" : "below %.0f%%");
                break;
            case ConditionKind.Buff:
                var presence = condition.Missing ? 1 : condition.Ending ? 2 : 0;
                if (Combo.Option(ref presence, ["Present", "Missing", "Ending"], "presence", 90)) { condition.Missing = presence == 1; condition.Ending = presence == 2; changed = true; }
                ImGui.SameLine();
                if (condition.Ending)
                {
                    ImGui.SetNextItemWidth(120);
                    changed |= ImGui.SliderFloat("##seconds", ref condition.Seconds, 0.1f, 10, "under %.1fs");
                    if (ImGui.IsItemHovered()) ImGui.SetTooltip("Holds while the longest matching buff has less than this many seconds left, or is gone - an untimed buff that is up never counts");
                    ImGui.SameLine();
                }
                ImGui.SetNextItemWidth(170);
                changed |= ImGui.InputText("##text", ref condition.Text, 128);
                if (ImGui.IsItemHovered()) ImGui.SetTooltip(BuffTip);
                ImGui.SameLine();
                Game.WantCharacter();
                BuffPickerOpts.EmptyText = Game.State.Buffs is null ? "buffs could not be read right now." : "no buffs on the player right now.";
                if (Picker.Button("Current buffs", Game.State.Buffs?.DistinctBy(b => b.Id), ref buffPicker, out var buff, BuffPickerOpts, "buffs"))
                {
                    condition.Text = AddExactId(condition.Text, buff.Id);
                    changed = true;
                }
                if (!condition.Missing && !condition.Ending)
                {
                    ImGui.SameLine();
                    ImGui.SetNextItemWidth(170);
                    changed |= ImGui.SliderInt("##stacks", ref condition.MinStacks, 0, 50, condition.MinStacks == 0 ? "any stacks" : "at least %d stacks");
                }
                break;
            case ConditionKind.GroundEffect or ConditionKind.BuffArea:
                ImGui.TextDisabled("player inside a hostile area");
                if (ImGui.IsItemHovered()) ImGui.SetTooltip(FightTip);
                break;
            case ConditionKind.SkillCooldown:
                changed |= SkillPicker(condition);
                ImGui.SameLine();
                var notReady = condition.Ready ? 0 : 1;
                if (Combo.Option(ref notReady, ["Ready", "Not ready"], "ready", 100)) { condition.Ready = notReady == 0; changed = true; }
                break;
            case ConditionKind.SkillCharges:
                changed |= SkillPicker(condition);
                ImGui.SameLine();
                ImGui.SetNextItemWidth(140);
                changed |= ImGui.SliderInt("##charges", ref condition.Charges, 1, 20, "at least %d");
                break;
        }
        return changed;
    }

    static bool SkillPicker(Condition condition)
    {
        Game.WantCharacter();
        var skills = (Game.State.Skills ?? []).Where(skill => skill.Slot is >= 0 and <= 4).OrderBy(skill => skill.Slot).ToArray();
        var labels = skills.Select(skill => $"Slot {skill.Slot} - {skill.Name ?? skill.CfgId.ToString()}").ToList();
        var index = Array.FindIndex(skills, skill => skill.CfgId == condition.SkillCfgId);
        if (index < 0)
        {
            labels.Add($"cfg {condition.SkillCfgId} (not on bar)");
            index = labels.Count - 1;
        }
        ImGui.SetNextItemWidth(170);
        if (!Combo.Option(ref index, [.. labels], "skill", 170) || index >= skills.Length) return false;
        condition.SkillCfgId = skills[index].CfgId;
        return true;
    }
}
