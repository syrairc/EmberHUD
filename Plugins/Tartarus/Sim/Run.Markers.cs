using System.Collections.Generic;

namespace Tartarus.Sim;

public sealed partial class Run
{
    public IReadOnlyList<PreyMark> PreyMarks => Prey;

    internal List<PreyMark> Prey { get; set; } = [];

    bool PreyHolds(PreyMark mark) =>
        Cells[mark.Lion].Open && Cells[mark.Lion].Beast == Beast.Lion && Cells[mark.Target].Open && Eats(Cells[mark.Lion], Cells[mark.Target].Beast);

    void Morning()
    {
        Prey.RemoveAll(mark => !PreyHolds(mark));
        for (var i = 0; i < Prey.Count; i++)
        {
            var mark = Prey[i];
            if (mark.Next > Day || !PreyHolds(mark)) continue;
            Raise(mark.Lion, 1);
            if (Cells[mark.Lion].Open) Raise(mark.Target, 1);
            Prey[i] = mark with { Rounds = mark.Rounds - 1, Next = Day + 1 };
        }
        Prey.RemoveAll(mark => mark.Rounds <= 0 || !PreyHolds(mark));
        for (var cell = 1; cell < Cells.Length; cell++)
            if (Cells[cell].Markers.HasFlag(Marker.Nest) && Cells[cell].NestDay <= Day)
                Fill(cell, new Cell(true, Beast.Rat, Tiers.Rare, false));
        MorningTools();
    }
}
