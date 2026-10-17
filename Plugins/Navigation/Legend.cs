using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;

namespace NavigationPlugin;

internal readonly record struct LegendRow(Color Colour, string Name, string Distance, bool Dimmed);

internal static class Legend
{
    public static List<LegendRow> Rows(IReadOnlyList<PathState> paths) =>
        [.. paths.Select(p => p.Points is null
            ? new LegendRow(p.Colour, p.Name, "-", true)
            : new LegendRow(p.Colour, p.Name, Distance(p), false))];

    public static string Distance(PathState path) => MathF.Round(path.Length).ToString("0", CultureInfo.InvariantCulture);
}
