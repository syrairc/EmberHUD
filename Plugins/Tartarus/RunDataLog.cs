using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using EmberHUD;

namespace Tartarus;

sealed class RunDataLog(string directory, Func<DateTime>? clock = null)
{
    const int Schema = 1;

    readonly Func<DateTime> _clock = clock ?? (() => DateTime.UtcNow);
    string? _run;
    string? _path;
    string? _last;

    internal string? Path => _path;

    public void Write(TartarusRaw? raw)
    {
        if (raw is null) return;
        try
        {
            var now = _clock();
            if (raw.Run != _run || _path is null) Open(raw, now);
            if (raw.State == _last) return;
            _last = raw.State;
            File.AppendAllText(_path!, $"{{\"t\":{JsonSerializer.Serialize(now.ToString("O"))},\"state\":{raw.State}}}\n");
        }
        catch (Exception error)
        {
            Log.Error(error, "tartarus run data log");
        }
    }

    void Open(TartarusRaw raw, DateTime now)
    {
        Directory.CreateDirectory(directory);
        _run = raw.Run;
        _last = null;
        var name = raw.Run.Length > 0 ? raw.Run : "norun";
        _path = raw.Run.Length > 0 ? Directory.GetFiles(directory, $"*-{name}.jsonl").FirstOrDefault() : null;
        if (_path is not null) return;
        _path = System.IO.Path.Combine(directory, $"{now:yyyyMMdd-HHmmss}-{name}.jsonl");
        var header = new { schema = Schema, ember = typeof(Game).Assembly.GetName().Version?.ToString(), build = raw.Build, run = raw.Run };
        File.AppendAllText(_path, JsonSerializer.Serialize(new { header }) + "\n");
    }
}
