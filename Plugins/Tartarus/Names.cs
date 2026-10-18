using System.Collections.Generic;
using EmberHUD;

namespace Tartarus;

public static class Names
{
    static readonly Dictionary<int, string> Instruments = new()
    {
        [101] = "Raging Beast's Agonizing Muzzle",
        [102] = "Ferocious Beast's Ravenous Chains",
        [103] = "Maddened Beast's Barbed Collar",
        [104] = "Avenger's Casket",
        [105] = "Crowded Beastcage",
        [106] = "Barbed Whip",
        [107] = "Rusty Saw",
        [111] = "Executioner's Blacklist",
        [112] = "Lost Ring of Keys",
        [113] = "Kin's Coin of Fate",
        [114] = "Searing Branding Iron",
        [115] = "Felon's Dog Tag",
        [116] = "Forged Key",
        [117] = "Sorting Hook",
        [121] = "Scapegoat's Guillotine",
        [122] = "Gaoler's Execution Axe",
        [123] = "Severed Iron Maiden",
        [124] = "Warden's Scales",
        [125] = "Hangman's Noose",
        [126] = "Burning Stake",
        [127] = "Iron Pestle",
        [131] = "Short-Chained Shackles",
        [132] = "Long-Chained Shackles",
        [141] = "Iron Tongs of Lust: Mammoth",
        [142] = "Elephant-Foot Maul: Mammoth",
        [143] = "Blood-Stained Trap: Berserk Lion",
        [144] = "Iron Crown of the Prison King: Berserk Lion",
        [145] = "Filthy Claws: Alpha Wolf",
        [146] = "Fanged Guillotine: Alpha Wolf",
        [147] = "Squirming Iron Box: Colossal Rat",
        [148] = "Skewered Rat Heads: Colossal Rat",
    };

    static readonly Dictionary<int, string> Events = new()
    {
        [201] = "High-Risk Convict",
        [202] = "Taste of Freedom",
        [203] = "Behind Closed Doors",
        [211] = "Ravenous Appetite",
        [212] = "Extra Portion",
        [213] = "Shared Fortune",
        [221] = "Archenemy",
        [222] = "Execution Efficiency",
        [231] = "Crow Whispers",
        [232] = "Treasure Hunt",
        [241] = "Sewer Ratway",
        [242] = "Chain Reaction",
        [251] = "Transfer of Convicts",
        [252] = "Usurpation I",
        [253] = "Usurpation II",
        [254] = "Usurpation III",
        [255] = "Usurpation IV",
    };

    public static string Event(int eventId) => Events.GetValueOrDefault(eventId, $"Event {eventId}");

    static readonly Dictionary<int, string> Rewards = new()
    {
        [1] = "Three-Headed Thumb Ring",
        [2] = "Brass Horn of Strife",
        [3] = "Crimson Vintage",
        [4] = "Brand of Original Sin",
    };

    public static string Option(TartarusOption option, TartarusState state, bool execute = false) => option.Kind switch
    {
        TartarusOptionKind.Door => Door(option, state),
        TartarusOptionKind.Consume => $"Consume cell {option.Cell + 1}",
        TartarusOptionKind.Replace => execute ? "Execute" : "Replace",
        TartarusOptionKind.End => "End",
        TartarusOptionKind.StopChain => "Stop chain",
        TartarusOptionKind.Instigate => "Instigate",
        TartarusOptionKind.Bestow => "Bestow",
        TartarusOptionKind.Instrument => Instruments.GetValueOrDefault(option.Param, $"Instrument {option.Param}"),
        TartarusOptionKind.Reward => Rewards.GetValueOrDefault(option.Param, $"Reward {option.Param}"),
        _ => $"Option {option.Id}",
    };

    static string Door(TartarusOption option, TartarusState state)
    {
        var name = $"Cell {option.Cell + 1} ({option.Param} steps)";
        foreach (var cell in state.Cells)
            if (cell.Index == option.Cell && cell.Open && cell.Tier >= 0) return $"{name} {cell.Beast} T{cell.Tier}";
        return name;
    }
}
