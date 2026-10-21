using System;
using System.Collections.Generic;
using System.Numerics;
using EmberHUD;

namespace Prices;

public sealed class CompareCache
{
    public const int DefaultSettleMs = 50;

    public long SettleMs { get; set; } = DefaultSettleMs;

    record struct Signature(string Name, long Quantity, long Price);

    sealed record Pending(int Page, int Slot, CompareRow[] Rows, long Since)
    {
        public bool Committed { get; set; }
    }

    readonly Dictionary<(int Page, int Slot), (Signature Signature, CompareRow[] Rows)> entries = [];
    Pending? pending;
    (int Page, int Slot)? hovered;
    long? lastVersion;
    long? baseline;
    bool accepted;

    public static int? HoveredSlot(TradeListing[] listings, Vector2 mouse)
    {
        foreach (var listing in listings)
            if (mouse.X >= listing.Min.X && mouse.X < listing.Max.X && mouse.Y >= listing.Min.Y && mouse.Y < listing.Max.Y) return listing.Slot;
        return null;
    }

    public void Feed(TradePage? page, EquipCompare? compare, Vector2 mouse, long nowMs)
    {
        if (page is null)
        {
            Clear();
            return;
        }
        var slot = HoveredSlot(page.Listings, mouse);
        (int Page, int Slot)? hover = slot is { } s ? (page.Page, s) : null;
        if (hover != hovered)
        {
            hovered = hover;
            baseline = lastVersion;
            accepted = false;
        }
        if (compare is not null)
        {
            accepted |= compare.Version == 0 || baseline is null || compare.Version != baseline;
            lastVersion = compare.Version;
        }
        if (compare is null || slot is null || !accepted)
        {
            pending = null;
            return;
        }
        if (pending is null || pending.Page != page.Page || pending.Slot != slot || !pending.Rows.AsSpan().SequenceEqual(compare.Rows))
            pending = new Pending(page.Page, slot.Value, compare.Rows, nowMs);
        if (pending.Committed || nowMs - pending.Since < SettleMs) return;
        foreach (var listing in page.Listings)
            if (listing.Slot == slot) entries[(page.Page, slot.Value)] = (Sign(listing), pending.Rows);
        pending.Committed = true;
    }

    public CompareRow[]? Rows(int page, TradeListing listing) =>
        entries.TryGetValue((page, listing.Slot), out var entry) && entry.Signature == Sign(listing) ? entry.Rows : null;

    public void Clear()
    {
        entries.Clear();
        pending = null;
        hovered = null;
        baseline = lastVersion = null;
        accepted = false;
    }

    static Signature Sign(TradeListing listing) => new(listing.Name, listing.Quantity, listing.Price);
}

internal static class CompareRects
{
    public static bool Hit(EquipCompare? tooltip, Vector2 min, Vector2 max) =>
        tooltip is { } t && t.Max.X > t.Min.X && t.Max.Y > t.Min.Y && min.X < t.Max.X && max.X > t.Min.X && min.Y < t.Max.Y && max.Y > t.Min.Y;
}
