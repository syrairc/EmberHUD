using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using EmberHUD;

namespace Pickit;

static class Space
{
    static readonly string[] Names = ["Gear", "Skills", "Commodities", "Other"];

    public static readonly Color[] Gradient = [Color.FromArgb(255, 111, 207, 122), Color.FromArgb(255, 217, 193, 103), Color.FromArgb(255, 224, 103, 103)];

    public readonly record struct Row(string Name, int Used, int Capacity)
    {
        public int Free => Math.Max(0, Capacity - Used);

        public string Text => $"{Name} {Used}/{Capacity} ({Free} free)";
    }

    public static string BagName(int bag) => Names[bag];

    public static List<Row> Rows(IReadOnlyList<InventoryItem> items, int[] capacities, Settings s)
    {
        var rows = new List<Row>();
        for (var bag = 0; bag < Math.Min(Settings.BagCount, capacities.Length); bag++)
            if (s.SpaceBags[bag]) rows.Add(new Row(Names[bag], items.Count(i => (int)i.Bag == bag), capacities[bag]));
        return rows;
    }
}
