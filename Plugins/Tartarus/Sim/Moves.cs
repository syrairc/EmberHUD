using System;

namespace Tartarus.Sim;

public enum Beast : byte { Mammoth, Lion, Wolf, Rat }

public enum MoveKind : byte { Door, Consume, Replace, End, StopChain, Instigate, Bestow, Instrument, Reward, Confirm, Skip, Hunt }

public enum Phase : byte { Patrol, Manage, Office, Event, Ended }

public enum ToolId : byte
{
    Revenge, Muzzle, Saw, Keys, Rope, Fire, IronBox, Skewer, Hook, Pestle, Scales, Tongs, Guillotine, Axe, Shackles,
    Collar, Forged, DogTag, Cage, Blacklist, ShortChain, Maiden, Whip, Ravenous, Scapegoat, Stake, Coin, Crown, Maul, Claws, Trap,
}

public static class Tiers
{
    public const int Rare = 2;

    public const int Legendary = 3;

    public const int Ultimate = 4;

    public const int Chromatic = 5;
}

public enum RewardId : byte { Ring, Horn, Wine, Brand }

public enum ObjectiveKind : byte { None, King, Walk, Consume, Incident, Doors }

public enum StartCard : byte { None, CageFight, IronLaw, Tyranny, Opulence }

public sealed record TreasurePreview(int Opened, int Unopened, int Hungry, int[] Upgrades, int Bare, int Feedable);

public readonly record struct Objective(ObjectiveKind Kind, int Progress = 0, bool Paid = false);

public readonly record struct Offer(MoveKind Kind, int[] Options);

public readonly record struct Move(MoveKind Kind, int Cell = -1, int Param = 0);

[Flags]
public enum Marker : byte { None = 0, Tusk = 1, Scratch = 2, Nest = 4 }

public enum EventKind : byte
{
    ChainReaction, Execution, BehindClosedDoors, RavenousAppetite, HighRiskConvict, CrowWhispers, TasteOfFreedom, UsurpationI, SewerRatway, TreasureHunt,
    UsurpationII, UsurpationIII, UsurpationIV,
    ExtraPortion, SharedFortune, Archenemy, TransferOfConvicts,
}

public readonly record struct PreyMark(int Lion, int Target, int Rounds, int Next);

public record struct Cell(bool Open, Beast Beast, int Tier, bool Known, bool Seen = false, int AtLeast = 0, Marker Markers = Marker.None, int NestDay = 0, EventKind? Event = null, bool Marked = false)
{
    public readonly bool HasEvent => Event is not null || Marked;
}

public readonly record struct StepResult(bool Ok, string? Why);
