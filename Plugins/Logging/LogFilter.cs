using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using EmberHUD;

namespace Logging;

sealed class Throttle(long intervalMs, Func<long> clock)
{
    long _last;
    bool _started;

    public bool Due()
    {
        var now = clock();
        if (_started && now - _last < intervalMs) return false;
        _started = true;
        _last = now;
        return true;
    }
}

static class LogFilter
{
    public static LogEntry[] Apply(IReadOnlyList<LogEntry> entries, LogLevel minimum, IReadOnlySet<string> sources, string search) =>
        entries.Where(entry => entry.Level >= minimum
            && (sources.Count == 0 || sources.Contains(entry.Source ?? ""))
            && (search.Length == 0 || Contains(entry.Source, search) || Contains(entry.Message, search))).ToArray();

    public static string Text(IReadOnlyList<LogEntry> entries) => string.Join('\n', entries.Select(LogFormat.Format));

    public static int SourceWidth(IReadOnlyList<LogEntry> entries, int cap = 16) => Math.Min(cap, entries.Select(entry => (entry.Source ?? "").Length).DefaultIfEmpty(0).Max());

    public static string Pad(string source, int width) => source.Length > width ? source[..Math.Max(0, width - 1)] + "~" : source.PadRight(width);

    public static string Block(IReadOnlyList<LogEntry> entries, int sourceWidth)
    {
        var text = new StringBuilder();
        var blank = new string(' ', 12 + 1 + 5 + 1 + sourceWidth + 1);
        foreach (var entry in entries)
        {
            var lines = (entry.Message ?? "").Replace("\r", "").Split('\n');
            if (text.Length > 0) text.Append('\n');
            text.Append(entry.Time.ToString("HH:mm:ss.fff")).Append(' ').Append(LogFormat.Level(entry.Level)).Append(' ').Append(Pad(entry.Source ?? "", sourceWidth)).Append(' ').Append(lines[0]);
            for (var i = 1; i < lines.Length; i++) text.Append('\n').Append(blank).Append(lines[i]);
        }
        return text.ToString();
    }

    public static DateOnly[] FileDays(IEnumerable<DateOnly> days, DateOnly today) => days.Where(day => day != today).OrderByDescending(day => day).ToArray();

    static bool Contains(string? text, string search) => text is not null && text.Contains(search, StringComparison.OrdinalIgnoreCase);
}
