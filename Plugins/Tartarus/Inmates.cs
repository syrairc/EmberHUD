using System;

namespace Tartarus;

public enum InmateSpecies { Mammoth, Lion, Wolf, Rat }

public readonly record struct InmateKind(InmateSpecies Species, int Step);

public static class Inmates
{
    static readonly string[] SpeciesNames = ["Mammoth", "Berserk Lion", "Alpha Wolf", "Colossal Rat"];
    static readonly string[] StepNames = ["Normal", "Magic", "Rare", "Legendary"];

    public static InmateKind? Classify(int cfgId, Func<string, string?> text)
    {
        if (text($"npc|name|{cfgId}") is not { } name) return null;
        var split = name.IndexOf(": ", StringComparison.Ordinal);
        if (split < 0) return null;
        var species = Array.IndexOf(SpeciesNames, name[(split + 2)..]);
        if (species < 0) return null;
        var title = name[..split];
        for (var step = 1; step <= 4; step++)
            if (text($"npc|name|{5027000 + species * 10 + step}") is { } known && known.StartsWith(title + ": ", StringComparison.Ordinal))
                return new((InmateSpecies)species, step);
        return new((InmateSpecies)species, 0);
    }

    public static string StepName(int step) => StepNames[step - 1];

    public static string SpeciesName(InmateSpecies species) => SpeciesNames[(int)species];
}
