using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace Prices;

public sealed class LogTail(string path)
{
    readonly byte[] _bytes = new byte[64 * 1024];
    readonly StringBuilder _partial = new();
    char[] _chars = [];
    long _position;
    Decoder? _decoder;

    public void SkipToEnd()
    {
        try
        {
            _position = new FileInfo(path).Length;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _position = 0;
        }
    }

    public List<string> ReadNew()
    {
        var lines = new List<string>();
        try
        {
            using var stream = Open(path);
            if (stream.Length < _position)
            {
                _position = 0;
                _decoder = null;
                _partial.Clear();
            }
            if (_decoder is null)
            {
                var (encoding, bom) = EncodingOf(stream);
                _decoder = encoding.GetDecoder();
                _chars = new char[encoding.GetMaxCharCount(_bytes.Length)];
                if (_position == 0) _position = bom;
            }
            stream.Position = _position;
            int read;
            while ((read = stream.Read(_bytes)) > 0)
            {
                _position += read;
                var count = _decoder.GetChars(_bytes, 0, read, _chars, 0);
                var start = 0;
                for (var i = 0; i < count; i++)
                {
                    if (_chars[i] != '\n') continue;
                    _partial.Append(_chars, start, i - start);
                    if (_partial.Length > 0 && _partial[^1] == '\r') _partial.Length--;
                    lines.Add(_partial.ToString());
                    _partial.Clear();
                    start = i + 1;
                }
                _partial.Append(_chars, start, count - start);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // missing or locked log reads as nothing new
        }
        return lines;
    }

    public static int? LastSeason(string path, int maxBytes)
    {
        try
        {
            using var stream = Open(path);
            var (encoding, bom) = EncodingOf(stream);
            var start = Math.Max(bom, stream.Length - maxBytes);
            if (encoding is UnicodeEncoding && (start - bom) % 2 != 0) start++;
            stream.Position = start;
            var bytes = new byte[stream.Length - start];
            stream.ReadExactly(bytes);
            var lines = encoding.GetString(bytes).Split('\n');
            for (var i = lines.Length - 1; i >= 0; i--)
                if (Exchange.SeasonOf(lines[i].TrimEnd('\r')) is { } season) return season;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // no log, no season
        }
        return null;
    }

    static FileStream Open(string path) => new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

    static (Encoding Encoding, int Bom) EncodingOf(FileStream stream)
    {
        Span<byte> head = stackalloc byte[3];
        stream.Position = 0;
        var read = stream.Read(head);
        if (read >= 2 && head[0] == 0xFF && head[1] == 0xFE) return (Encoding.Unicode, 2);
        if (read >= 2 && head[0] == 0xFE && head[1] == 0xFF) return (Encoding.BigEndianUnicode, 2);
        return (Encoding.UTF8, read == 3 && head[0] == 0xEF && head[1] == 0xBB && head[2] == 0xBF ? 3 : 0);
    }
}

public sealed class Contribution(HttpClient http, string logPath, string dataDirectory)
{
    static readonly TimeSpan RateLimitPause = TimeSpan.FromHours(1);
    const int OutboxCap = 200;

    readonly LogTail _tail = new(logPath);
    readonly ExchangeParser _parser = new();
    readonly List<(Search Search, int Season)> _outbox = [];
    volatile int _season;
    volatile int _sent;
    DateTime _pausedUntil;
    string? _device;
    string? _error;

    public ConcurrentQueue<Search> Found { get; } = new();

    public int? Season => _season > 0 ? _season : null;

    public int Sent => _sent;

    public string Status { get; private set; } = "starting";

    public void Start()
    {
        _season = LogTail.LastSeason(logPath, 4 << 20) ?? 0;
        _tail.SkipToEnd();
    }

    public void Poll(DateTime now)
    {
        foreach (var line in _tail.ReadNew())
        {
            if (Exchange.SeasonOf(line) is { } season) _season = season;
            if (_parser.Feed(line, now) is not { } search) continue;
            Found.Enqueue(search);
            Queue(search, _season);
        }
    }

    internal void Queue(Search search, int logSeason)
    {
        if (logSeason <= 0) return;
        if (_outbox.Count >= OutboxCap) _outbox.RemoveAt(0);
        _outbox.Add((search, logSeason));
    }

    public void Flush(Func<bool> contribute, DateTime now)
    {
        if (now < _pausedUntil) return;
        while (_outbox.Count > 0 && contribute())
        {
            var (search, season) = _outbox[0];
            try
            {
                _device ??= DeviceId(dataDirectory);
                var request = new HttpRequestMessage(HttpMethod.Post, PriceSync.Base + "/rest/v1/rpc/submit_price") { Content = new StringContent(Body(_device, search, season), Encoding.UTF8, "application/json") };
                using var reply = JsonDocument.Parse(PriceSync.Send(http, request).GetAwaiter().GetResult());
                var root = reply.RootElement;
                if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("rate_limited", out var limited) && limited.ValueKind == JsonValueKind.True)
                {
                    _pausedUntil = now + RateLimitPause;
                    return;
                }
                _outbox.RemoveAt(0);
                if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("success", out var success) && success.ValueKind == JsonValueKind.True)
                {
                    _sent++;
                    _error = null;
                }
                else
                {
                    _error = root.ValueKind == JsonValueKind.Object && root.TryGetProperty("error", out var error) ? error.ToString() : root.GetRawText();
                    return;
                }
            }
            catch (Exception ex)
            {
                // a failed submission is dropped, retrying a bad one forever helps nobody
                _outbox.RemoveAt(0);
                _error = ex.Message;
                return;
            }
        }
    }

    public void Describe(DateTime now) => Status =
        !File.Exists(logPath) ? "log not found - turn on Settings > Other > Enable Log in the game"
        : now < _pausedUntil ? $"rate limited - paused until {_pausedUntil.ToLocalTime():HH:mm}"
        : _season <= 0 ? "waiting for season - relog or switch character"
        : $"watching the log, {_sent} sent this session" + (_error is { } error ? $" - last error: {error}" : "");

    internal void Fail(string message) => _error = message;

    public static string Body(string device, Search search, int season) => JsonSerializer.Serialize(new
    {
        p_device_id = device,
        p_config_base_id = search.BaseId,
        p_season_id = season,
        p_price_fe = Exchange.ReferencePrice(search.PricesFe),
        p_prices_array = search.PricesFe,
    });

    public static string DeviceId(string directory)
    {
        var path = Path.Combine(directory, "device.txt");
        try
        {
            if (File.Exists(path) && Guid.TryParse(File.ReadAllText(path).Trim(), out var saved)) return saved.ToString();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // unreadable, make a new one
        }
        var made = Guid.NewGuid().ToString();
        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(path, made);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // kept for this session only
        }
        return made;
    }
}
