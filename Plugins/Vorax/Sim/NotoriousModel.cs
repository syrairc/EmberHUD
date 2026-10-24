using System;
using System.Globalization;

namespace Vorax.Sim;

// Value of a board for the Notorious search: the fork's linear model of the final Activity, one weight vector per round (MIT, see LICENSE-voraxsim.txt)
static partial class NotoriousModel
{
    public const double Cap = 950000;

    const int HeadSize = 31;
    const int PerInstrument = 13;
    const int InstrumentCount = 25;
    const int PairBase = HeadSize + InstrumentCount * PerInstrument;

    static readonly double[][] Rounds = Parse();

    // archetype order of the fork: pest, aemberon, bone sentry, agito
    static int Archetype(Species s) => s switch { Species.Pests => 0, Species.Aemberon => 1, Species.BoneSentry => 2, _ => 3 };

    public static double Value(SimState s)
    {
        var total = s.TotalActivity;
        if (s.Over) return Math.Min(total, Cap);

        var w = Rounds[Math.Min(s.Round, Rounds.Length) - 1];
        var archetypes = new double[4];
        var rarities = new double[4];
        double strong = 0, wide = 0, hot = 0, sumActivity = 0, sumQuantity = 0, maxActivity = 0, maxQuantity = 0, maxTotal = 0;
        var count = 0;
        foreach (var d in s.Dishes)
        {
            if (d is not { } m) continue;
            count++;
            archetypes[Archetype(m.Species)]++;
            rarities[(int)m.Rarity - 1]++;
            if (m.Activity > 150 && m.Quantity > 150) strong++;
            if (m.Quantity > 275) wide++;
            if (m.Activity > 255) hot++;
            sumActivity += m.Activity;
            sumQuantity += m.Quantity;
            maxActivity = Math.Max(maxActivity, m.Activity);
            maxQuantity = Math.Max(maxQuantity, m.Quantity);
            maxTotal = Math.Max(maxTotal, m.Total);
        }

        var logScore = Math.Log(1 + total) / 14;
        var paired = new double[11];
        for (var i = 0; i < 4; i++)
        {
            paired[i] = archetypes[i] / 6;
            paired[4 + i] = rarities[i] / 6;
        }
        paired[8] = strong / 6;
        paired[9] = wide / 6;
        paired[10] = hot / 6;

        double[] head =
        [
            1, logScore, total / 1e6, Math.Min(total, Cap) / 1e6, Math.Sqrt(total) / 1e3, total >= Cap ? 1 : 0,
            sumActivity / 1e3, sumQuantity / 3e3, maxActivity / 1e3, maxQuantity / 3e3, maxTotal / 3e5, count / 6.0,
            .. paired,
            0, 0, s.Instruments.Count / 3.0, s.PendingInstrumentPicks, s.Passed8000 ? 1 : 0, s.Passed28000 ? 1 : 0,
            s.PendingInstrumentPicks > 0 ? 1 : 0, s.PendingInstrumentPicks == 0 && s.Round > Round.PotionRounds ? 1 : 0,
        ];
        head[HeadSize - 8] = s.Rerolls / 3.0;

        double sum = 0;
        for (var i = 0; i < HeadSize; i++) sum += w[i] * head[i];

        var owned = new bool[InstrumentCount];
        var any = false;
        foreach (var id in s.Instruments)
            if (id is >= 1 and <= InstrumentCount)
            {
                owned[id - 1] = true;
                any = true;
            }

        if (any)
            for (var i = 0; i < InstrumentCount; i++)
            {
                if (!owned[i]) continue;
                var u = HeadSize + i * PerInstrument;
                sum += w[u];
                for (var d = 0; d < 11; d++) sum += w[u + 1 + d] * paired[d];
                sum += w[u + 12] * logScore;
                for (var j = i + 1; j < InstrumentCount; j++)
                    if (owned[j]) sum += w[PairBase + i * InstrumentCount - i * (i + 1) / 2 + (j - i - 1)];
            }

        return Math.Max(0, Math.Min(Cap, sum * 1e6));
    }

    static double[][] Parse()
    {
        var rows = Data.Split(';');
        var result = new double[rows.Length][];
        for (var r = 0; r < rows.Length; r++)
        {
            var cells = rows[r].Split(',');
            result[r] = new double[cells.Length];
            for (var c = 0; c < cells.Length; c++) result[r][c] = double.Parse(cells[c], CultureInfo.InvariantCulture);
        }
        return result;
    }
}
