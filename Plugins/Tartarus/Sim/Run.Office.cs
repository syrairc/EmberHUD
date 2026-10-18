using System.Collections.Generic;
using System.Linq;

namespace Tartarus.Sim;

public sealed partial class Run
{
    void Bestow()
    {
        Lift();
        Phase = Phase.Manage;
    }

    void Instigate()
    {
        Plant();
        Phase = Phase.Manage;
    }

    void Lift()
    {
        var inmates = Inmates();
        if (inmates.Count > 0) Raise(inmates[Pick(inmates.Count)], 1);
    }

    void Plant()
    {
        foreach (var cell in Sample(InstigateCells(), InstigateCount)) Cells[cell] = Cells[cell] with { Event = RandomEvent(), Seen = true };
    }

    internal List<int> InstigateCells() => Enumerable.Range(1, Cells.Length - 1).Where(cell => !Cells[cell].HasEvent && (Cells[cell].Open || !Cells[cell].Markers.HasFlag(Marker.Nest))).ToList();

    internal int InstigateCount => Cells.Any(cell => cell.HasEvent) ? 1 : 2;

    void Tyranny()
    {
        for (var i = 0; i < 3; i++) Plant();
        for (var i = 0; i < 3; i++) Lift();
    }
}
