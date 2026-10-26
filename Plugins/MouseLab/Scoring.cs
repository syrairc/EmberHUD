using EmberHUD;

namespace MouseLab;

public sealed record Thresholds(double MaxBacktrack = 3, double Overshoot = 3, double FinalError = 6, double LongestStallMs = 40);

public readonly record struct Verdict(bool BacktrackOk, bool OvershootOk, bool FinalErrorOk, bool StallOk)
{
    public bool All => BacktrackOk && OvershootOk && FinalErrorOk && StallOk;
}

public static class Scoring
{
    public static Verdict Judge(PathScores scores, Thresholds thresholds) => new(
        scores.MaxBacktrack <= thresholds.MaxBacktrack,
        scores.Overshoot <= thresholds.Overshoot,
        scores.FinalError <= thresholds.FinalError,
        scores.LongestStallMs <= thresholds.LongestStallMs);
}
