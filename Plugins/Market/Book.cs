using System;
using System.Collections.Generic;
using System.Linq;
using EmberHUD;

namespace Market;

internal readonly record struct Row(int Page, TradeListing Listing, bool Stale)
{
    public static double PerUnitOf(TradeListing listing) => listing.Quantity > 0 ? (double)listing.Price / listing.Quantity : double.PositiveInfinity;

    public double PerUnit => PerUnitOf(Listing);

    public bool Sold => Listing.Status is not null;

    public string PageText { get; } = Page.ToString();

    public string QuantityText { get; } = Listing.Quantity.ToString("N0");

    public string PriceText { get; } = Listing.Price.ToString("N0");

    public string PerUnitText { get; } = double.IsPositiveInfinity(PerUnitOf(Listing)) ? "-" : PerUnitOf(Listing).ToString("0.000");
}

internal sealed class Book
{
    readonly SortedDictionary<int, (TradeListing[] Listings, long ReadAt)> _pages = [];
    string _item = "";
    int _cellsPerPage;
    int? _lastPage;

    public int Count => _pages.Count;

    public int Version { get; private set; }

    public long StaleSignature(long now, long staleAfter)
    {
        long signature = 0;
        foreach (var (page, cached) in _pages)
            if (now - cached.ReadAt > staleAfter) signature += HashCode.Combine(page);
        return signature;
    }

    public string PagesText => _lastPage is { } last ? $"{_pages.Count} / {last}" : $"{_pages.Count} / {(_pages.Count == 0 ? 0 : _pages.Keys.Last())}+";

    public void Update(TradePage? shown, long now)
    {
        if (shown is null)
        {
            Clear();
            return;
        }
        var item = shown.Listings.Select(l => l.Name).FirstOrDefault(name => name.Length > 0) ?? "";
        if (shown.CellsPerPage != _cellsPerPage || item.Length > 0 && _item.Length > 0 && item != _item) Clear();
        _cellsPerPage = shown.CellsPerPage;
        if (item.Length > 0) _item = item;
        if (shown.LastPage)
        {
            _lastPage = shown.Page;
            foreach (var page in _pages.Keys.Where(page => page > shown.Page).ToList())
            {
                _pages.Remove(page);
                Version++;
            }
        }
        else if (shown.Page >= _lastPage) _lastPage = null;
        if (!_pages.TryGetValue(shown.Page, out var cached) || !SameListings(cached.Listings, shown.Listings)) Version++;
        _pages[shown.Page] = (shown.Listings, now);
    }

    static bool SameListings(TradeListing[] a, TradeListing[] b) => ReferenceEquals(a, b) || a.AsSpan().SequenceEqual(b);

    public void Clear()
    {
        if (_pages.Count > 0) Version++;
        _pages.Clear();
        _item = "";
        _cellsPerPage = 0;
        _lastPage = null;
    }

    public List<Row> Rows(long now, long staleAfter) =>
        [.. _pages.SelectMany(page => page.Value.Listings.Select(listing => new Row(page.Key, listing, now - page.Value.ReadAt > staleAfter)))];

    public static TradeListing? ListingAt(TradeListing[] listings, int slot)
    {
        foreach (var listing in listings)
            if (listing.Slot == slot) return listing;
        return null;
    }

    public TradeListing? Find(int page, int slot) => _pages.TryGetValue(page, out var cached) ? ListingAt(cached.Listings, slot) : null;
}
