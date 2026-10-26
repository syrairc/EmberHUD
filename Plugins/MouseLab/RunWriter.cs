using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace MouseLab;

public sealed record RunFile(DateTimeOffset At, IReadOnlyList<RunResult> Results);

public static class RunWriter
{
    const string Header = "Preset,Scenario,Label,DurationMs,Distance,PathLength,MeanSpeed,PeakSpeed,FinalError,Overshoot,MaxBacktrack,HeadingJitter,SpeedRoughness,LongestStallMs,BacktrackOk,OvershootOk,FinalErrorOk,StallOk,Status";

    public static string Write(string directory, IReadOnlyList<RunResult> results, DateTimeOffset at)
    {
        Directory.CreateDirectory(directory);
        var stem = Path.Combine(directory, "run-" + at.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture));
        File.WriteAllText(stem + ".json", JsonSerializer.Serialize(new RunFile(at, results)));
        File.WriteAllText(stem + ".csv", Csv(results));
        return stem + ".json";
    }

    static string Csv(IReadOnlyList<RunResult> results)
    {
        var text = new StringBuilder(Header).AppendLine();
        foreach (var result in results)
        {
            var status = result.Cancelled ? "cancelled" : result.Refused is not null ? "refused" : "complete";
            if (result.Moves.Count == 0) text.AppendLine(string.Join(',', result.Preset, "", "", string.Join(',', Enumerable.Repeat("", 15)), status));
            foreach (var move in result.Moves)
            {
                var scores = move.Scores;
                var verdict = move.Verdict;
                text.AppendLine(string.Join(',', Quote(result.Preset), Quote(move.Scenario), Quote(move.Label),
                    Number(scores.DurationMs), Number(scores.Distance), Number(scores.PathLength), Number(scores.MeanSpeed), Number(scores.PeakSpeed), Number(scores.FinalError), Number(scores.Overshoot), Number(scores.MaxBacktrack), Number(scores.HeadingJitter), Number(scores.SpeedRoughness), Number(scores.LongestStallMs),
                    Flag(verdict.BacktrackOk), Flag(verdict.OvershootOk), Flag(verdict.FinalErrorOk), Flag(verdict.StallOk), status));
            }
        }
        return text.ToString();
    }

    static string Number(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    static string Flag(bool value) => value ? "true" : "false";

    static string Quote(string value) => value.IndexOfAny([',', '"', '\n']) < 0 ? value : "\"" + value.Replace("\"", "\"\"") + "\"";
}
