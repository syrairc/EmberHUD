using System;
using System.Collections.Generic;
using System.Drawing;
using System.Numerics;
using EmberHUD;
using EmberHUD.Ui;

namespace EmberAuras;

public sealed class Main : Plugin<Settings>
{
    const float DimOpacity = 0.35f;
    const float PlaceholderSize = 24f;
    const float TextPad = 2f;
    const int MaxBorder = 16;
    static readonly Color Back = Color.FromArgb(170, 20, 20, 20);
    static readonly Color Sweep = Color.FromArgb(170, 0, 0, 0);

    readonly Overlay _overlay = new();
    readonly List<(Aura Aura, AuraView View)> _visible = [];
    int _unusedWidth, _unusedHeight;

    public override void Initialize()
    {
        Settings.Normalize();
    }

    public override void Tick()
    {
        Settings.Normalize();
        if (NeedsCharacter()) Game.WantCharacter();
        if (NeedsFighters(Settings)) Game.WantFighters();
    }

    public static bool NeedsFighters(Settings settings)
    {
        foreach (var group in settings.Groups)
        {
            if (!group.Enabled) continue;
            foreach (var aura in group.Auras)
                if (aura.Enabled && aura.Conditions.Exists(condition => Conditions.IsFightKind(condition.Kind))) return true;
        }
        return false;
    }

    bool NeedsCharacter()
    {
        foreach (var group in Settings.Groups)
        {
            if (!group.Enabled) continue;
            foreach (var aura in group.Auras)
            {
                if (!aura.Enabled) continue;
                foreach (var condition in aura.Conditions)
                    if (condition.Kind is ConditionKind.Buff or ConditionKind.SkillCooldown or ConditionKind.SkillCharges) return true;
            }
        }
        return false;
    }

    public static bool ShouldDraw(GameState state, Settings settings) =>
        state.Player is not null && !(settings.HideOnFullScreen && state.Ui.AnyFullScreen);

    public override void Render()
    {
        var state = Game.State;
        if (!ShouldDraw(state, Settings))
        {
            for (var i = 0; i < Settings.Groups.Count; i++) _overlay.Release(IdOf(i));
            return;
        }
        _overlay.Locked = Settings.Lock;
        _overlay.MoveHeld = Settings.MoveKey.Key != Key.None && Input.IsDown(Settings.MoveKey.Key);
        for (var i = 0; i < Settings.Groups.Count; i++)
        {
            if (Settings.Groups[i].Enabled) RenderGroup(Settings.Groups[i], i, state);
            else _overlay.Release(IdOf(i));
        }
    }

    static string IdOf(int index) => $"emberauras{index}";

    void RenderGroup(Group group, int index, GameState state)
    {
        _visible.Clear();
        foreach (var aura in group.Auras)
        {
            AuraView view;
            try
            {
                view = AuraEval.Evaluate(aura, state);
            }
            catch (Exception)
            {
                continue;
            }
            if (view.Visibility != Visibility.Hidden) _visible.Add((aura, view));
        }

        var placeholder = _visible.Count == 0;
        if (placeholder && !_overlay.Movable)
        {
            _overlay.Release(IdOf(index));
            return;
        }

        var sizes = new Vector2[placeholder ? 1 : _visible.Count];
        for (var i = 0; i < sizes.Length; i++) sizes[i] = placeholder ? new Vector2(PlaceholderSize) : SizeOf(_visible[i].Aura);
        var positions = placeholder ? [new Vector2(group.X, group.Y)] : GroupLayout.Place(group, sizes);
        var (min, max) = GroupLayout.Bounds(positions, sizes);

        var id = IdOf(index);
        var origin = min;
        int x = (int)Math.Round(min.X), y = (int)Math.Round(min.Y);
        int startX = x, startY = y;
        var (hovered, _, active) = _overlay.Handle(id, ref origin, max - min, ref x, ref y, ref _unusedWidth, 60, 4000, ref _unusedHeight, 0, 0, Overlay.ResizeEdge.None);
        group.X += x - startX;
        group.Y += y - startY;
        var shift = origin - min;
        if (hovered || active) Overlay.ClickBlocker(id, origin, max - min);

        if (placeholder) Draw.RectFilled(origin, origin + sizes[0], Back);
        else
            for (var i = 0; i < positions.Length; i++)
                DrawAura(_visible[i].Aura, _visible[i].View, positions[i] + shift);

        if (_overlay.Movable) Overlay.DragHint(origin, origin + (max - min), active, hovered, Overlay.ResizeEdge.None);
    }

    static Vector2 SizeOf(Aura aura)
    {
        if (aura.Style == AuraStyle.Icon) return new Vector2(aura.IconSize);
        var rects = BarLayout(aura, Vector2.Zero);
        return rects.Max - rects.Min;
    }

    static IconBarRects BarLayout(Aura aura, Vector2 origin) =>
        IconBar.Layout(origin, new Vector2(aura.BarWidth, aura.BarHeight), aura.BarIcon == BarIcon.None ? 0 : aura.BarHeight, aura.BarIcon == BarIcon.Right ? Controls.IconSide.Right : Controls.IconSide.Left);

    void DrawAura(Aura aura, AuraView view, Vector2 at)
    {
        try
        {
            var opacity = view.Visibility == Visibility.Dim ? DimOpacity : 1f;
            Vector2 min, max;
            if (aura.Style == AuraStyle.Icon)
            {
                min = at;
                max = at + new Vector2(aura.IconSize);
                var swept = view.Seconds is not null || view.MaxCharges > 0 || view.HasPercent ? (max.Y - min.Y) * (1f - Math.Clamp(view.Fill, 0f, 1f)) : 0f;
                DrawIcon(aura, view, min, max, opacity, swept);
            }
            else
            {
                var rects = BarLayout(aura, at);
                min = rects.Min;
                max = rects.Max;
                IconBar.DrawBar(rects, view.Fill, aura.FillColor, Back, opacity);
                if (rects.IconMax != rects.IconMin) DrawIcon(aura, view, rects.IconMin, rects.IconMax, opacity, 0f);
            }
            foreach (var text in aura.Texts)
            {
                if (!text.Enabled || view.TextFor(text.Field) is not { } value) continue;
                var pos = Anchors.Place(min, max, Draw.MeasureText(value), text.Anchor, TextPad);
                Draw.Text(value, pos, Faded(Color.White, opacity));
            }
        }
        catch (Exception)
        {
            // one bad aura must not take the frame down
        }
    }

    static void DrawIcon(Aura aura, AuraView view, Vector2 min, Vector2 max, float opacity, float swept)
    {
        if (max.X <= min.X || max.Y <= min.Y) return;
        var outline = IconShapes.Outline(aura.Shape, min, max, aura.CornerRadius);
        IconShapes.FillPolygon(outline, Faded(aura.BackdropColor, opacity));
        Sprite sprite = default;
        var found = (view.BuffId is { } buff && Draw.BuffIcon(buff, out sprite)) || (view.SkillCfgId is { } skill && Draw.SkillIcon(skill, out sprite));
        if (found) IconShapes.DrawImage(sprite, outline, min, max, Faded(Color.White, opacity));
        else if (aura.Icon.Length > 0) Draw.Icon(aura.Icon, (min + max) / 2, (max.X - min.X) / 2, Faded(Color.White, opacity));
        else IconShapes.FillPolygon(outline, Faded(aura.FillColor, opacity));
        if (swept > 0) IconShapes.FillPolygon(IconShapes.ClipTop(outline, min.Y + swept), Faded(Sweep, opacity));
        IconShapes.StrokePolygon(outline, Faded(aura.BorderColor, opacity), Math.Clamp(aura.BorderWidth, 0, MaxBorder));
    }

    static Color Faded(Color colour, float opacity) => Color.FromArgb((int)(colour.A * Math.Clamp(opacity, 0f, 1f)), colour);
}
