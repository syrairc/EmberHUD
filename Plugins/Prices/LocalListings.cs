using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using EmberHUD;
using EmberHUD.Ui;
using Hexa.NET.ImGui;

namespace Prices;

internal sealed class ListingRow
{
    public required long GoodId { get; init; }
    public required string Name { get; init; }
    public required long Price { get; init; }
    public required int Quantity { get; init; }
    public required int Rare { get; init; }
    public required string RareLabel { get; init; }
    public required int ItemLevel { get; init; }
    public required string TierKey { get; init; }
    public required string TierText { get; init; }
    public required string FlagsText { get; init; }
    public required long LastSeen { get; init; }
    public required long FirstSeen { get; init; }
    public required string Search { get; init; }
    public required LocalRow Source { get; init; }

    public string LastSeenText => Time(LastSeen);

    public string FirstSeenText => Time(FirstSeen);

    static string Time(long unix) => DateTimeOffset.FromUnixTimeSeconds(unix).ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

    public static ListingRow From(LocalRow row, IReadOnlyDictionary<int, string> texts)
    {
        var id = row.SpecialId != 0 ? row.SpecialId : row.BaseId;
        var name = ItemNames.Of(id);
        if (name.Length == 0) name = "#" + id;
        var rare = row.Rare == 0 && row.Commodity ? ItemNames.Rarity(id) : row.Rare;
        var flags = FlagsOf(row);
        var affixes = string.Join(' ', (row.Affixes ?? []).Select(affix => AffixText(texts, affix.Id).Replace("#", "")));
        return new ListingRow
        {
            GoodId = row.GoodId,
            Name = name,
            Price = row.Price,
            Quantity = row.Quantity,
            Rare = rare,
            RareLabel = RareName(rare),
            ItemLevel = row.ItemLevel,
            TierKey = row.TierKey,
            TierText = TierMix(row.TierKey),
            FlagsText = flags,
            LastSeen = row.LastSeen,
            FirstSeen = row.FirstSeen,
            Search = $"{name} {affixes} {flags}".ToLowerInvariant(),
            Source = row,
        };
    }

    public static string RareName(int rare) => rare switch { 1 => "Grey", 2 => "Cyan", 3 => "Purple", 4 => "Pink", 100 => "Orange", _ => rare.ToString(CultureInfo.InvariantCulture) };

    public static string AffixText(IReadOnlyDictionary<int, string> texts, int id) => texts.TryGetValue(id, out var text) ? text : $"affix {id}";

    public static string TierMix(string tierKey)
    {
        if (tierKey.Length == 0) return "-";
        var parts = tierKey.Split(';');
        static string Part(string part) => string.Join(", ", part[2..].Split(',', StringSplitOptions.RemoveEmptyEntries).Select(pair => "T" + pair.Replace("x", " x")));
        var rolled = Part(parts[0]);
        var bases = Part(parts[1]);
        return string.Join(" | ", new[] { rolled, bases.Length > 0 ? "base: " + bases : "" }.Where(text => text.Length > 0));
    }

    static string FlagsOf(LocalRow row)
    {
        List<string> flags = [];
        if (row.Unidentified) flags.Add("Unidentified");
        if (row.Corrode) flags.Add("Corroded");
        if (row.Forged) flags.Add("Forged");
        if (row.Sold) flags.Add("Sold");
        if (row.EnhanceCount > 0) flags.Add($"Enhance {row.EnhanceCount}");
        if (row.Flags is { } other) flags.AddRange(other);
        return string.Join(", ", flags);
    }
}

internal sealed class LocalListings
{
    public const string FeIcon = "UI_Item_chuhuoyuanzhiV3_Icon_128";
    const string Any = "Any";
    const string NoTiers = "(no tiers)";
    const int DetailLines = 8;

    readonly TableColumn<ListingRow>[] _columns;
    LocalBook? _book;
    int _version = -1;
    bool _builtCommodities, _builtHideSold;
    List<ListingRow> _all = [];
    List<ListingRow> _shown = [];
    string? _tierKey;
    string[] _tierLabels = [Any];
    string?[] _tierKeys = [null];
    string _filter = "";

    public bool IncludeCommodities;
    public bool HideSold = true;
    public long SelectedGoodId;

    public LocalListings()
    {
        _columns =
        [
            new() { Header = "Name", SortKey = row => row.Name, Draw = (row, _) => DrawName(row) },
            new() { Header = "Price", Width = 110, SortKey = row => row.Price, Draw = (row, _) => Controls.AmountCell(row.Price.ToString("N0", CultureInfo.InvariantCulture), FeIcon) },
            new() { Header = "Qty", Width = 50, SortKey = row => row.Quantity, Draw = (row, _) => ImGui.TextUnformatted(row.Quantity.ToString(CultureInfo.InvariantCulture)) },
            new() { Header = "Item lvl", Width = 60, SortKey = row => row.ItemLevel, Draw = (row, _) => ImGui.TextUnformatted(row.ItemLevel.ToString(CultureInfo.InvariantCulture)) },
            new() { Header = "Tier mix", Width = 200, SortKey = row => row.TierText, Draw = (row, _) => ImGui.TextUnformatted(row.TierText) },
            new() { Header = "Last seen", Width = 120, SortKey = row => row.LastSeen, Draw = (row, _) => ImGui.TextUnformatted(row.LastSeenText) },
        ];
    }

    void DrawName(ListingRow row)
    {
        ImGui.PushStyleColor(ImGuiCol.Text, EColor.ToVector4(GameColors.Rarity(row.Source.SpecialId != 0 ? 100 : row.Rare)));
        if (ImGui.Selectable(row.Name + "##sel", row.GoodId == SelectedGoodId, ImGuiSelectableFlags.SpanAllColumns)) SelectedGoodId = row.GoodId;
        ImGui.PopStyleColor();
    }

    public IReadOnlyList<ListingRow> Shown => _shown;

    public int Total => _all.Count;

    public IReadOnlyList<string> TierLabels => _tierLabels;

    public void SelectTier(string? key)
    {
        _tierKey = key;
        Refilter();
    }

    public void Rebuild(LocalBook? book)
    {
        if (ReferenceEquals(book, _book) && book?.Version == _version && IncludeCommodities == _builtCommodities && HideSold == _builtHideSold) return;
        (_book, _version, _builtCommodities, _builtHideSold) = (book, book?.Version ?? -1, IncludeCommodities, HideSold);
        _all = book is null ? [] : [.. book.Rows.Where(row => (IncludeCommodities || !row.Commodity) && (!HideSold || !row.Sold)).Select(row => ListingRow.From(row, book.AffixTexts))];
        var counts = _all.GroupBy(row => row.TierKey).OrderByDescending(group => group.Count()).ThenBy(group => group.Key, StringComparer.Ordinal).ToList();
        _tierKeys = [null, .. counts.Select(group => group.Key)];
        _tierLabels = [Any, .. counts.Select(group => $"{(group.Key.Length == 0 ? NoTiers : ListingRow.TierMix(group.Key))} ({group.Count()})")];
        if (_tierKey is not null && !_tierKeys.Contains(_tierKey)) _tierKey = null;
        Refilter();
    }

    void Refilter() => _shown = _tierKey is null ? [.. _all] : [.. _all.Where(row => row.TierKey == _tierKey)];

    public ListingRow? Selected => _all.FirstOrDefault(row => row.GoodId == SelectedGoodId);

    public IEnumerable<string> Details(ListingRow row, IReadOnlyDictionary<int, string> texts)
    {
        var source = row.Source;
        yield return $"{row.Name}  -  {row.Price} FE x {row.Quantity}  -  {row.RareLabel}";
        yield return $"Item level {row.ItemLevel}, level {source.Level}{(row.FlagsText.Length > 0 ? ", " + row.FlagsText : "")}";
        foreach (var kind in new[] { TradeAffixKind.Implicit, TradeAffixKind.Prefix, TradeAffixKind.Suffix, TradeAffixKind.BaseAttribute })
            foreach (var affix in (source.Affixes ?? []).Where(affix => affix.Kind == kind))
                yield return $"{(affix.Tier > 0 ? "T" + affix.Tier : "  ")}  {ListingRow.AffixText(texts, affix.Id)}";
    }

    public void Draw(LocalBook? book)
    {
        ImGui.Checkbox("Include commodities##listcom", ref IncludeCommodities);
        ImGui.SameLine();
        ImGui.Checkbox("Hide sold##listsold", ref HideSold);
        Rebuild(book);
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted("Tier mix");
        ImGui.SameLine(160);
        var index = Math.Max(0, Array.IndexOf(_tierKeys, _tierKey));
        if (Combo.Option(ref index, _tierLabels, "listtier", 320f)) SelectTier(_tierKeys[index]);
        ImGui.SameLine();
        ImGui.TextDisabled($"{_shown.Count} / {_all.Count} listings");
        if (_all.Count == 0)
        {
            ImGui.TextDisabled(book is null ? "Local database is turned off" : "No listings scanned yet");
            return;
        }
        var detailsHeight = ImGui.GetTextLineHeightWithSpacing() * DetailLines + ImGui.GetStyle().WindowPadding.Y * 2;
        var tableHeight = Math.Max(80f, ImGui.GetContentRegionAvail().Y - detailsHeight - ImGui.GetFrameHeightWithSpacing() - ImGui.GetStyle().ItemSpacing.Y);
        SortableTable.Draw("listings", _shown, _columns, ref _filter, row => row.Search, tableHeight);
        DrawDetails(book!.AffixTexts, detailsHeight);
    }

    void DrawDetails(IReadOnlyDictionary<int, string> texts, float height)
    {
        if (ImGui.BeginChild("listdetails", new Vector2(0, height), ImGuiChildFlags.Borders))
        {
            if (Selected is { } row)
                foreach (var line in Details(row, texts)) ImGui.TextUnformatted(line);
            else ImGui.TextDisabled("Click a listing to see its affixes");
        }
        ImGui.EndChild();
    }
}
