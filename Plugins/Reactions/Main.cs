using System;
using System.Drawing;
using System.Linq;
using System.Numerics;
using EmberHUD;

namespace Reactions;

public sealed class Main : Plugin<Settings>
{
    readonly Engine _engine = new();

    public override void Initialize()
    {
        Settings.Normalize();
    }

    public override void Tick()
    {
        Settings.Normalize();
        if (Settings.Reactions.Any(reaction => reaction.Enabled && reaction.Conditions.Any(condition => condition.Kind is ConditionKind.Buff or ConditionKind.SkillCooldown or ConditionKind.SkillCharges or ConditionKind.CrowdControlled)))
            Game.WantCharacter();
        _engine.Step(Settings, Game.State, Environment.TickCount64, Send.Press);
    }

    public override void Render()
    {
        if (Game.State.Player is not { } player) return;
        Settings.Normalize();
        foreach (var reaction in Settings.Reactions)
            foreach (var condition in reaction.Conditions)
                if (condition is { Kind: ConditionKind.NearbyEnemies, ShowRange: true })
                    Ground.Circle(new Vector2(player.Position.X, player.Position.Y), condition.Radius, Color.FromArgb(200, 255, 170, 60), 2, player.Position.Z);
    }

    public override void OnDisable() => Send.ReleaseAll();

    public override void Dispose() => Send.ReleaseAll();
}
