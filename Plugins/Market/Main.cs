using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Numerics;
using EmberHUD;
using EmberHUD.Ui;
using Hexa.NET.ImGui;

namespace Market;

internal readonly record struct Pick(int Page, int Slot, long Quantity, long Price)
{
    public static Pick Of(Row row) => new(row.Page, row.Listing.Slot, row.Listing.Quantity, row.Listing.Price);
}

internal enum PickState { Here, OtherPage, Gone }

internal readonly record struct RowsKey(int Version, long StaleSignature, long StaleAfter, int? Page, int MaxPrice, float MaxPerUnit);

public sealed class Main : Plugin<Settings>
{
    readonly Book _book = new();
    List<Row> _rows = [];
    RowsKey? _rowsKey;
    string _tableFilter = "";
    TableColumn<Row>[]? _columns;
    Sprite? _priceSprite;
    TradePage? _shown;
    readonly HashSet<Pick> _picks = [];
    Pick? _hovered;

    internal IReadOnlyCollection<Pick> Picks => _picks;

    internal void Toggle(Pick pick)
    {
        if (!_picks.Remove(pick)) _picks.Add(pick);
    }

    internal static PickState StateOf(Pick pick, TradePage shown, Book book)
    {
        if (pick.Page != shown.Page)
            return book.Find(pick.Page, pick.Slot) is { } cached && Matches(pick, cached) ? PickState.OtherPage : PickState.Gone;
        return Book.ListingAt(shown.Listings, pick.Slot) is { } listing && Matches(pick, listing) ? PickState.Here : PickState.Gone;
    }

    static bool Matches(Pick pick, TradeListing listing) =>
        listing.Status is null && listing.Quantity == pick.Quantity && listing.Price == pick.Price;

    internal static bool Passes(TradeListing listing, Settings settings) =>
        (settings.MaxPrice <= 0 || listing.Price <= settings.MaxPrice) &&
        (settings.MaxPerUnit <= 0 || Row.PerUnitOf(listing) <= settings.MaxPerUnit);

    internal static List<Row> Shown(List<Row> rows, Settings settings, int currentPage)
    {
        return [.. rows.Where(row => !row.Sold && (!settings.CurrentPageOnly || row.Page == currentPage) && Passes(row.Listing, settings))
            .OrderBy(row => row.PerUnit)
            .ThenBy(row => row.Listing.Price)
            .ThenBy(row => row.Page)
            .ThenBy(row => row.Listing.Slot)];
    }

    internal List<Row> RowsFor(TradePage shown, long now)
    {
        var staleAfter = Settings.StaleMinutes * 60_000L;
        var key = new RowsKey(_book.Version, _book.StaleSignature(now, staleAfter), staleAfter, Settings.CurrentPageOnly ? shown.Page : null, Settings.MaxPrice, Settings.MaxPerUnit);
        if (key == _rowsKey) return _rows;
        _rowsKey = key;
        return _rows = Shown(_book.Rows(now, staleAfter), Settings, shown.Page);
    }

    public override void Initialize() =>
        RegisterHotkey("Toggle window", "Shows or hides the market listing window", () => Settings.Show = !Settings.Show);

    public override void Tick()
    {
        var shown = Game.State.Trade;
        _book.Update(shown, Environment.TickCount64);
        if (shown is null) _picks.Clear();
        else _picks.RemoveWhere(pick => StateOf(pick, shown, _book) == PickState.Gone);
    }

    public override void Render()
    {
        if (Game.State.Trade is not { } shown || !Settings.Show) return;
        var rows = RowsFor(shown, Environment.TickCount64);
        _hovered = null;
        DrawWindow(rows, shown);
        DrawGrid(shown);
    }

    void DrawWindow(List<Row> rows, TradePage shown)
    {
        _shown = shown;
        var icon = shown.PriceIcon;
        _priceSprite = icon.StartsWith(Draw.GameIconPrefix, StringComparison.Ordinal) && Draw.TryGameIcon(icon.AsSpan(Draw.GameIconPrefix.Length), out var sprite) ? sprite : null;
        var open = true;
        ImGui.SetNextWindowSize(new Vector2(340, 560), ImGuiCond.FirstUseEver);
        if (ImGui.Begin("Market###market", ref open))
        {
            ImGui.TextDisabled($"Pages {_book.PagesText}");
            ImGui.SameLine();
            ImGui.Checkbox("Current page only", ref Settings.CurrentPageOnly);
            ImGui.SameLine();
            if (ImGui.Button("Clear"))
            {
                _book.Clear();
                _picks.Clear();
            }

            ImGui.SetNextItemWidth(90);
            if (ImGui.InputInt("Max price", ref Settings.MaxPrice)) Settings.MaxPrice = Math.Max(0, Settings.MaxPrice);
            ImGui.SameLine();
            ImGui.SetNextItemWidth(90);
            if (ImGui.InputFloat("Max per unit", ref Settings.MaxPerUnit, 0, 0, "%.3f")) Settings.MaxPerUnit = Math.Max(0, Settings.MaxPerUnit);

            SortableTable.Draw("rows", rows, _columns ??= Columns(), ref _tableFilter, row => row.Listing.Name, ImGui.GetContentRegionAvail().Y);
        }
        ImGui.End();
        if (!open) Settings.Show = false;
    }

    internal TableColumn<Row>[] Columns() =>
    [
        new() { Header = "", Width = 24, Draw = (row, _) => PickCell(row) },
        new() { Header = "Name", SortKey = row => row.Listing.Name, Draw = (row, _) => Cell(row, row.Listing.Name) },
        new() { Header = "Page", Width = 40, SortKey = row => row.Page * 100_000L + row.Listing.Slot, Draw = (row, _) => Cell(row, PageLabel(row)) },
        new() { Header = "Qty", Width = 64, SortKey = row => row.Listing.Quantity, Draw = (row, _) => Cell(row, row.QuantityText) },
        new() { Header = "Price", Width = 84, SortKey = row => row.Listing.Price, Draw = (row, _) => PriceCell(row) },
        new() { Header = "Per unit", Width = 64, SortKey = row => row.PerUnit, Draw = (row, _) => Cell(row, row.PerUnitText) },
    ];

    string PageLabel(Row row) =>
        _shown is { } shown && _picks.Contains(Pick.Of(row)) && StateOf(Pick.Of(row), shown, _book) == PickState.OtherPage ? $"{row.Page} >" : row.PageText;

    void PickCell(Row row)
    {
        var pick = Pick.Of(row);
        var picked = _picks.Contains(pick);
        if (ImGuiP.TableGetHoveredRow() == ImGui.TableGetRowIndex()) _hovered = pick;
        if (ImGui.Checkbox("##pick", ref picked)) Toggle(pick);
    }

    void DrawGrid(TradePage shown)
    {
        var color = Settings.GlowColor;
        if (Settings.DimNonMatches)
            foreach (var listing in Dimmed(shown, Settings))
                Draw.RectFilled(listing.Min, listing.Max, Color.FromArgb(128, 0, 0, 0));
        if (_hovered is { } hovered && hovered.Page == shown.Page && Book.ListingAt(shown.Listings, hovered.Slot) is { } hover)
            Draw.Rect(hover.Min, hover.Max, Color.FromArgb(color.A * 7 / 10, color), 2);
        var pulse = 0.65f + 0.35f * MathF.Sin((float)ImGui.GetTime() * 1000f / 180f);
        var thickness = Settings.GlowThickness;
        foreach (var pick in _picks)
            if (StateOf(pick, shown, _book) == PickState.Here && Book.ListingAt(shown.Listings, pick.Slot) is { } picked)
                Draw.GlowRect(picked.Min, picked.Max, Color.FromArgb((int)(color.A * pulse), color), thickness, thickness * 4);
    }

    internal static IEnumerable<TradeListing> Dimmed(TradePage shown, Settings settings) =>
        shown.Listings.Where(listing => listing.Status is not null || !Passes(listing, settings));

    internal static void Cell(Row row, string text)
    {
        using var dim = Layout.Dim(row.Stale);
        ImGui.TextUnformatted(text);
    }

    void PriceCell(Row row)
    {
        using var dim = Layout.Dim(row.Stale);
        if (_priceSprite is { } sprite)
        {
            Controls.IconImage(sprite, ImGui.GetTextLineHeight(), Color.White);
            ImGui.SameLine();
            ImGui.TextUnformatted(row.PriceText);
        }
        else ImGui.TextUnformatted($"{row.PriceText} FE");
    }
}
