using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace NameplatesPlugin;

public readonly record struct PlateData(string Name, double Life, double MaxLife, float Distance, string Rarity, string Type, string Cast = "", float CastLeft = 0);

public static class NumberFormat
{
    static readonly string[] Suffixes = ["", "K", "M", "B"];

    public static string Abbrev(double value)
    {
        var step = 0;
        while (step < Suffixes.Length - 1 && Math.Abs(value) >= (step == 0 ? 999.5 : 999.95))
        {
            value /= 1000;
            step++;
        }
        return value.ToString(step == 0 ? "0" : "0.#", CultureInfo.InvariantCulture) + Suffixes[step];
    }
}

public sealed class Template
{
    enum Token { Literal, Name, Hp, HpMax, HpPercent, Dist, Rarity, Type, Cast, Left }

    static readonly Dictionary<string, Token> Tokens = new(StringComparer.OrdinalIgnoreCase)
    {
        ["name"] = Token.Name,
        ["hp"] = Token.Hp,
        ["hpmax"] = Token.HpMax,
        ["hp%"] = Token.HpPercent,
        ["dist"] = Token.Dist,
        ["rarity"] = Token.Rarity,
        ["type"] = Token.Type,
        ["cast"] = Token.Cast,
        ["left"] = Token.Left,
    };

    readonly List<(Token Token, string? Text)> segments = new();
    readonly StringBuilder builder = new();
    string? constant;

    public static Template Parse(string text)
    {
        var template = new Template();
        var literal = new StringBuilder();
        var i = 0;
        while (i < text.Length)
        {
            var close = text[i] == '{' ? text.IndexOf('}', i) : -1;
            if (close > 0 && Tokens.TryGetValue(text.Substring(i + 1, close - i - 1), out var token))
            {
                if (literal.Length > 0) template.segments.Add((Token.Literal, literal.ToString()));
                literal.Clear();
                template.segments.Add((token, null));
                i = close + 1;
            }
            else literal.Append(text[i++]);
        }
        if (literal.Length > 0) template.segments.Add((Token.Literal, literal.ToString()));
        if (template.segments.Count == 0) template.constant = "";
        else if (template.segments.Count == 1 && template.segments[0].Token == Token.Literal) template.constant = template.segments[0].Text;
        return template;
    }

    public string Render(in PlateData data)
    {
        if (constant != null) return constant;
        builder.Clear();
        foreach (var (token, text) in segments)
        {
            switch (token)
            {
                case Token.Literal: builder.Append(text); break;
                case Token.Name: builder.Append(data.Name); break;
                case Token.Hp: builder.Append(NumberFormat.Abbrev(data.Life)); break;
                case Token.HpMax: builder.Append(NumberFormat.Abbrev(data.MaxLife)); break;
                case Token.HpPercent: builder.Append(Percent(data)); break;
                case Token.Dist: builder.Append(NumberFormat.Abbrev(data.Distance)); break;
                case Token.Rarity: builder.Append(data.Rarity); break;
                case Token.Type: builder.Append(data.Type); break;
                case Token.Cast: builder.Append(data.Cast); break;
                case Token.Left: builder.Append(data.CastLeft.ToString("0.0", CultureInfo.InvariantCulture)); break;
            }
        }
        return builder.ToString();
    }

    static string Percent(in PlateData data)
    {
        if (data.MaxLife <= 0) return "0";
        var percent = Math.Clamp(Math.Round(data.Life / data.MaxLife * 100), 0, 100);
        return percent.ToString("0", CultureInfo.InvariantCulture);
    }
}
