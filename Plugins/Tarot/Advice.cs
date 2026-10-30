using System;
using System.Collections.Generic;
using System.Globalization;
using System.Drawing;
using System.Linq;
using EmberHUD;

namespace Tarot;

public static class Advice
{
    public static ActionResult[]? Usable(TarotState state, string signature, (string Signature, ActionResult[] Results)? latest) =>
        state.Phase == TarotPhase.Board && latest is { Results.Length: > 0 } found && found.Signature == signature ? found.Results : null;

    public static (int[] BestHandIndices, bool EndRunBest) Rank(TarotState state, ActionResult[] results)
    {
        if (results.Length == 0) return ([], false);
        var top = results[0];
        if (top.Move.End) return ([], true);
        var tied = results.TakeWhile(r => !r.Move.End && r.Mean == top.Mean).Select(r => r.Move.HandIndex);
        return ([.. tied.Where(i => i >= 0 && i < state.Hand.Length).Distinct()], false);
    }

    public static ActionResult? BestFor(ActionResult[] results, int handIndex) =>
        results.Cast<ActionResult?>().FirstOrDefault(r => !r!.Value.Move.End && r.Value.Move.HandIndex == handIndex);

    public static string RowText(ActionResult result) =>
        string.Create(CultureInfo.InvariantCulture, $"{result.Mean:0.0}  dead {result.DeadRate * 100:0}%");

    public static Color RowColour(bool solverText, bool best, Color greedy) => !solverText ? greedy : best ? GameColors.Positive : GameColors.Text;

    public static (int Index, bool Good)[] Arrows(TarotState state, ActionResult[]? results)
    {
        if (results is null) return [.. Board.BestCards(state).Select(i => (i, Board.BestMove(state, state.Hand[i])!.Value.Score > 0))];
        return [.. Rank(state, results).BestHandIndices.Select(i => (i, true))];
    }

    public static (int Index, bool Good) Pick(TarotState state, ActionResult[]? results)
    {
        var options = Board.Options(state.Playing);
        if (results is not null)
        {
            var best = results.FirstOrDefault(r => !r.Move.End && Array.IndexOf(options, r.Move.Steps) >= 0);
            if (best.Samples > 0) return (Array.IndexOf(options, best.Move.Steps), true);
        }
        return Board.BestMove(state, new TarotCard(0, state.Playing)) is { } move ? (Array.IndexOf(options, move.Steps), move.Score > 0) : (-1, false);
    }
}
