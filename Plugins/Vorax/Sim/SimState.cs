using System.Collections.Generic;
using System.Linq;

namespace Vorax.Sim;

public enum EventKind { Add, Remove, Mutate, Awaken, Fuse, Devour, AddOverflow }

public readonly record struct SimEvent(EventKind Kind, int Dish, Species Species, Species ToSpecies);

// Port of the community Vorax simulator (MIT, see LICENSE-voraxsim.txt) - data.js constants and engine.js operations
public sealed partial class SimState
{
    public const int DishCount = 6;

    public Monster?[] Dishes { get; private set; } = new Monster?[DishCount];
    public List<int> Instruments { get; private set; } = [];
    public int Round { get; set; }
    public int Rerolls { get; set; }
    public bool Passed8000 { get; set; }
    public bool Passed28000 { get; set; }
    public Dictionary<int, int> AcquiredRound { get; private set; } = [];
    public List<SimEvent> Events { get; private set; } = [];
    public int PendingInstrumentPicks { get; set; }
    public bool Over { get; set; }
    public Rng? Rng { get; set; }
    public long TotalActivity => Dishes.Sum(d => d?.Total ?? 0L);

    public SimState Clone() => new()
    {
        Dishes = (Monster?[])Dishes.Clone(),
        Instruments = [.. Instruments],
        Round = Round,
        Rerolls = Rerolls,
        Passed8000 = Passed8000,
        Passed28000 = Passed28000,
        AcquiredRound = new(AcquiredRound),
        Events = [.. Events],
        PendingInstrumentPicks = PendingInstrumentPicks,
        Over = Over,
    };
}
