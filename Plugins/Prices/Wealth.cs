using System;

namespace Prices;

public sealed record Line(int BaseId, int Count, float Unit, float Value);

public sealed record Snapshot(DateTimeOffset At, int AreaId, string AreaName, bool InTown, float Total, float Fe, int Unpriced, Line[] Lines);
