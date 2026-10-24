using System;
using System.Collections.Generic;
using System.Linq;

namespace Tartarus;

public static class Registry
{
    static readonly Lazy<IReadOnlyList<Strategy>> Found = new(() => Discover(false));

    public static IReadOnlyList<Strategy> All() => Found.Value;

    public static Strategy? ByName(string? name) => string.IsNullOrEmpty(name) ? null : Found.Value.FirstOrDefault(s => s.Name == name);

    internal static IReadOnlyList<Strategy> Discover(bool includeNested)
    {
        var found = new List<Strategy>();
        foreach (var type in typeof(Strategy).Assembly.GetTypes())
        {
            if (type is not { IsClass: true, IsAbstract: false } || !type.IsSubclassOf(typeof(Strategy)) || type.GetConstructor(Type.EmptyTypes) is null) continue;
            if (type.IsNested && !includeNested) continue;
            try
            {
                var strategy = (Strategy)Activator.CreateInstance(type)!;
                _ = strategy.Name;
                found.Add(strategy);
            }
            catch (Exception)
            {
            }
        }

        return [.. found.OrderBy(s => s.Name, StringComparer.Ordinal)];
    }
}
