using System;
using System.Drawing;
using System.Linq;
using System.Numerics;
using System.Text.RegularExpressions;
using EmberHUD;
using EmberHUD.Ui;
using Hexa.NET.ImGui;
using Tartarus.Sim;

namespace Tartarus;

static class PracticeUI
{
    const float BoardSize = 280;

    static readonly string[] TierNames = ["Normal", "Magic", "Rare", "Legendary", "Ultimate", "Chromatic"];
    static readonly Color[] TierColors = [GameColors.NormalName, GameColors.RareName, GameColors.Positive, GameColors.LegendaryName, GameColors.Gold, GameColors.System];

    static string seedText = "";
    static ulong shownSeed = ulong.MaxValue;

    internal static int MoveButtons { get; private set; }

    public static void Draw(PracticeSession session)
    {
        if (shownSeed != session.Seed)
        {
            shownSeed = session.Seed;
            seedText = session.Seed.ToString();
        }
        DrawSeed(session);
        var run = session.Run;
        ImGui.TextUnformatted($"Day {run.Day}/{run.MaxDay}   Chromatic {run.Chromatics}   {Objective(run.Objective)}");
        if (run.Tools.Count > 0) ImGui.TextUnformatted("Tools: " + string.Join(", ", run.Tools.Select(tool => Spaced(tool.ToString()))));
        DrawBoard(run);
        MoveButtons = 0;
        if (run.Over)
        {
            ImGui.TextColored(EColor.ToVector4(GameColors.Gold), $"Run over - {run.Chromatics} Chromatic");
        }
        else
        {
            ImGui.SeparatorText(Heading(run));
            var legal = session.Legal;
            for (var i = 0; i < legal.Count; i++)
            {
                if (!ImGui.Button($"{Label(legal[i], run)}##move{i}")) continue;
                session.Play(legal[i]);
                break;
            }
            MoveButtons = legal.Count;
        }
        ImGui.Separator();
        DrawHint(session);
    }

    static void DrawSeed(PracticeSession session)
    {
        Setting.Input("Seed", ref seedText, "The same seed and the same moves replay the same run", "practice-seed", 20);
        ImGui.SameLine();
        if (ImGui.Button("New run") && ulong.TryParse(seedText, out var seed)) session.Restart(seed);
        ImGui.SameLine();
        if (ImGui.Button("Random seed")) session.Restart((ulong)Random.Shared.NextInt64());
        ImGui.SameLine();
        ImGui.BeginDisabled(!session.CanUndo);
        if (ImGui.Button("Undo")) session.Undo();
        ImGui.EndDisabled();
    }

    static void DrawHint(PracticeSession session)
    {
        ImGui.BeginDisabled(session.Run.Over);
        if (ImGui.Button("Hint")) session.RequestHint();
        ImGui.EndDisabled();
        if (session.HintPending) ImGui.TextDisabled("thinking");
        if (session.Hint is not { } hint) return;
        foreach (var ranked in hint)
            ImGui.TextUnformatted($"{Label(ranked.Move, session.Run)}   {ranked.Value:0.00} +/- {ranked.Spread:0.00}");
    }

    static void DrawBoard(Run run)
    {
        var origin = ImGui.GetCursorScreenPos();
        var list = ImGui.GetWindowDrawList();
        var centre = origin + new Vector2(BoardSize / 2);
        var count = run.Cells.Length;
        var radius = BoardSize / 2 - 26;
        var node = MathF.Min(22, MathF.PI * radius / count);
        for (var i = 0; i < count; i++)
        {
            var angle = -MathF.PI / 2 + 2 * MathF.PI * i / count;
            var at = centre + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radius;
            var cell = run.Cells[i];
            var color = cell.Open ? TierColors[Math.Clamp(cell.Tier, 0, TierColors.Length - 1)] : GameColors.TextDim;
            EmberHUD.Draw.CircleFilled(at, node, Color.FromArgb(0x60, 0x10, 0x12, 0x18), list: list);
            EmberHUD.Draw.Circle(at, node, i == run.Position ? GameColors.Gold : color, i == run.Position ? 3 : 1.5f, list: list);
            EmberHUD.Draw.Text(i == 0 ? "O" : CellText(cell), at, color, Align.Center | Align.Middle, list: list);
            EmberHUD.Draw.Text((i + 1).ToString(), at + new Vector2(0, node + 8), GameColors.TextDim, Align.Center | Align.Middle, list: list);
        }
        if (run.Doors.Length > 0 && run.Phase == Phase.Patrol)
            foreach (var door in run.Doors)
            {
                var angle = -MathF.PI / 2 + 2 * MathF.PI * door / count;
                EmberHUD.Draw.Circle(centre + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radius, node + 4, GameColors.Positive, 1.5f, list: list);
            }
        ImGui.Dummy(new Vector2(BoardSize));
    }

    static string CellText(Cell cell)
    {
        if (cell.Open) return $"{cell.Beast.ToString()[0]}{cell.Tier}";
        return cell.Known && cell.Tier >= 0 ? $"{cell.Beast.ToString()[0]}{cell.Tier}?" : cell.Tier >= 0 && cell.Markers.HasFlag(Marker.Scratch) ? $"{cell.Tier}?" : "?";
    }

    static string Heading(Run run) => run.Offers.Count > 0 ? "Choose" : run.Phase switch
    {
        Phase.Patrol => "Today's doors",
        Phase.Manage => run.Chaining >= 0 ? "Chain" : "Operation",
        Phase.Office => "Office",
        Phase.Event => run.PendingEvent is { } pending ? Spaced(pending.ToString()) : "Event",
        _ => "",
    };

    static string Objective(Objective objective) => objective.Kind == ObjectiveKind.None ? "no objective" : $"Objective: {Spaced(objective.Kind.ToString())} {objective.Progress}{(objective.Paid ? " (paid)" : "")}";

    internal static string Label(Move move, Run run) => move.Kind switch
    {
        MoveKind.Door => $"Door: {Where(run, move.Cell)}, {Steps(run, move.Cell)} steps",
        MoveKind.Consume => $"Eat {Where(run, move.Cell)}",
        MoveKind.Replace => run.Rules.ReplaceIsExecute ? "Execute" : "Replace",
        MoveKind.End => run.Phase == Phase.Office ? "Leave office" : "End day",
        MoveKind.StopChain => "Stop chain",
        MoveKind.Instigate => "Instigate",
        MoveKind.Bestow => "Bestow",
        MoveKind.Instrument => Spaced(((ToolId)move.Param).ToString()),
        MoveKind.Reward => Spaced(((RewardId)move.Param).ToString()),
        MoveKind.Confirm => "Confirm",
        MoveKind.Skip => "Skip",
        _ => move.Kind.ToString(),
    };

    static int Steps(Run run, int cell) => (cell - run.Position + run.Cells.Length - 1) % run.Cells.Length + 1;

    static string Where(Run run, int index)
    {
        if (index == 0) return "office";
        var cell = run.Cells[index];
        return cell.Open ? $"cell {index + 1} ({cell.Beast} {TierNames[Math.Clamp(cell.Tier, 0, TierNames.Length - 1)]})" : $"cell {index + 1}";
    }

    static string Spaced(string name) => Regex.Replace(name, "(?<=[a-z])([A-Z])", " $1");
}
