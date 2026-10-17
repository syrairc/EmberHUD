using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

namespace Prices;

public readonly record struct Search(int BaseId, double[] PricesFe);

// Exchange log parsing ported from TITrack (MIT) src/titrack/parser/exchange_parser.py at f02b9b8, see licenses\THIRD-PARTY.txt
public static class Exchange
{
    static readonly Regex PlayerSeason = new(@"\+player\+SeasonId\s*\[(\d+)\]");
    static readonly Regex BlockSeason = new(@"^\|\s{6}\+SeasonId\s*\[(\d+)\]");

    public static double ReferencePrice(double[] sorted) => sorted.Length == 0 ? 0 : sorted[Math.Max(0, (int)(sorted.Length * 0.10) - 1)];

    public static int? SeasonOf(string line) =>
        PlayerSeason.Match(line) is { Success: true } player ? int.Parse(player.Groups[1].ValueSpan, CultureInfo.InvariantCulture)
        : BlockSeason.Match(line) is { Success: true } block ? int.Parse(block.Groups[1].ValueSpan, CultureInfo.InvariantCulture)
        : null;

    public static string LogPath(string exePath) => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(exePath)!, @"..\..\Torchlight\Saved\Logs\UE_game.log"));
}

public sealed class ExchangeParser
{
    const int Fe = 100300;
    static readonly TimeSpan PendingFor = TimeSpan.FromMinutes(5);
    static readonly Regex SendStart = new(@"----Socket SendMessage STT----XchgSearchPrice----SynId = (\d+)");
    static readonly Regex RecvStart = new(@"----Socket RecvMessage STT----XchgSearchPrice----SynId = (\d+)");
    static readonly Regex End = new(@"----Socket (?:Send|Recv)Message End----");
    static readonly Regex Refer = new(@"\+refer \[(\d+)\]");
    static readonly Regex Currency = new(@"\+currency \[(\d+)\]");
    static readonly Regex UnitPrice = new(@"\+(?:unitPrices\+)?\d+ \[([0-9.]+)\]");
    static readonly Regex SectionStart = new(@"\+prices\+\d+\+unitPrices");

    readonly Dictionary<int, (int BaseId, DateTime At)> _pending = [];
    readonly List<string> _lines = [];
    bool? _sending;
    int _synId;

    public Search? Feed(string line, DateTime now)
    {
        if (SendStart.Match(line) is { Success: true } send) Start(true, send);
        else if (RecvStart.Match(line) is { Success: true } recv) Start(false, recv);
        else if (End.IsMatch(line)) return Finish(now);
        else if (_sending is not null) _lines.Add(line);
        return null;
    }

    void Start(bool sending, Match start)
    {
        _sending = sending;
        _synId = int.Parse(start.Groups[1].ValueSpan, CultureInfo.InvariantCulture);
        _lines.Clear();
    }

    Search? Finish(DateTime now)
    {
        if (_sending is not { } sending) return null;
        _sending = null;
        if (sending)
        {
            foreach (var (id, request) in _pending)
                if (now - request.At > PendingFor) _pending.Remove(id);
            foreach (var line in _lines)
            {
                if (Refer.Match(line) is not { Success: true } refer) continue;
                var baseId = int.Parse(refer.Groups[1].ValueSpan, CultureInfo.InvariantCulture);
                if (baseId != 0) _pending[_synId] = (baseId, now);
                break;
            }
            return null;
        }
        if (!_pending.Remove(_synId, out var asked) || now - asked.At > PendingFor) return null;
        var prices = FePrices();
        if (prices.Count == 0) return null;
        prices.Sort();
        return new Search(asked.BaseId, [.. prices]);
    }

    List<double> FePrices()
    {
        var fe = new List<double>();
        var section = new List<double>();
        bool? sectionIsFe = null;
        foreach (var line in _lines)
        {
            if (SectionStart.IsMatch(line))
            {
                if (section.Count > 0 && sectionIsFe == true) fe.AddRange(section);
                section.Clear();
                sectionIsFe = null;
            }
            if (Currency.Match(line) is { Success: true } currency)
            {
                var isFe = int.Parse(currency.Groups[1].ValueSpan, CultureInfo.InvariantCulture) == Fe;
                if (section.Count > 0)
                {
                    if (isFe) fe.AddRange(section);
                    section.Clear();
                    sectionIsFe = null;
                }
                else sectionIsFe = isFe;
                continue;
            }
            if (UnitPrice.Match(line) is not { Success: true } price || !double.TryParse(price.Groups[1].ValueSpan, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)) continue;
            if (sectionIsFe == true) fe.Add(value);
            else section.Add(value);
        }
        return fe;
    }
}
