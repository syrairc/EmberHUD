using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text.Json.Serialization;
using EmberHUD;
using EmberHUD.Ui;
using Hexa.NET.ImGui;

namespace Prices;

public sealed partial class Settings : PluginSettings
{
    public bool Enabled = true;
    public Corner Corner = Corner.TopLeft;
    public int OffsetX = 6;
    public int OffsetY = 8;
    public int MapRewardOffsetX;
    public int MapRewardOffsetY;
    public int BossRewardOffsetX;
    public int BossRewardOffsetY;
    public TextStyle Text = new() { Color = Color.FromArgb(0xF9, 0xDD, 0x75), ShadowEnabled = true };
    public bool ShowIcon = true;
    public bool ShowGrey = true;
    public bool ShowCyan = true;
    public bool ShowPurple = true;
    public bool ShowPink = true;
    public bool ShowOrange = true;
    public bool ShowSkills = true;
    public bool ShowCommodities = true;
    public bool ShowOther = true;
    public float MinValue;
    public bool MinValueGearOnly;
    public int RefreshMinutes = 5;
    public int Season;
    public bool Contribute;
    public bool UseTitrack = true;
    public bool UseEtor = true;
    public bool UseLocal;
    public int MinSamples;
    public int IgnoreOlderThanDays;
    public int PurgeAfterDays;
    public bool ExcludeOutliers = true;
    public bool ShareTitrack;
    public bool ShareEtor;
    public bool ShareLocal = true;
    public PriceSource[] Priority = [PriceSource.Etor, PriceSource.Titrack, PriceSource.Local];
    [JsonIgnore] public string Status = "";
    [JsonIgnore] public string ContributeStatus = "";
    [JsonIgnore] public Func<int>? PurgeLocal;
    [JsonIgnore] public string PurgeResult = "";
    [JsonIgnore] public Func<PriceSource[], string>? ExportShare;
    [JsonIgnore] public Func<string, string>? ImportShare;
    [JsonIgnore] public string ShareFolder = "";
    [JsonIgnore] public IReadOnlyDictionary<PriceSource, IReadOnlyDictionary<int, Price>> Tables = new Dictionary<PriceSource, IReadOnlyDictionary<int, Price>>();

    [JsonIgnore] public LegendaryPrices? Legendaries;
    [JsonIgnore] internal LocalBook? Book;

    public bool ShowTotals = true;
    public bool TotalRange;
    public bool Lock = true;
    public Hotkey MoveKey = new(Key.Alt);
    public int PanelX = 40;
    public int PanelY = 200;

    public bool GroundEnabled = true;
    public bool GroundGrey;
    public bool GroundCyan;
    public bool GroundPurple;
    public bool GroundPink;
    public bool GroundOrange = true;
    public bool GroundNonEquipment;

    public bool CompareEnabled = true;
    public Corner CompareDamageCorner = Corner.BottomLeft;
    public int CompareDamageX;
    public int CompareDamageY;
    public Corner CompareSurvivalCorner = Corner.BottomRight;
    public int CompareSurvivalX;
    public int CompareSurvivalY;
    public int CompareHoverMs = CompareCache.DefaultSettleMs;
    public TextStyle CompareText = new() { ShadowEnabled = true };

    public const int MinRefreshMinutes = 5;

    static bool Pick(int rareValue, bool grey, bool cyan, bool purple, bool pink, bool orange) => rareValue switch { 1 => grey, 2 => cyan, 3 => purple, 4 => pink, 100 => orange, _ => true };

    public bool Shows(InventoryItem item) => item.Bag switch
    {
        InventoryBag.Equipment => Pick(item.RareValue, ShowGrey, ShowCyan, ShowPurple, ShowPink, ShowOrange),
        InventoryBag.Skills => ShowSkills,
        InventoryBag.Commodities => ShowCommodities,
        _ => ShowOther,
    };

    public bool TooCheap(InventoryItem item, double value) => value < MinValue && (!MinValueGearOnly || item.Bag == InventoryBag.Equipment);

    public bool ShowsOnGround(GroundItem item) =>
        (item.Equipment || GroundNonEquipment) && Pick(item.RareValue, GroundGrey, GroundCyan, GroundPurple, GroundPink, GroundOrange);

    public PriceSource[] Order() => [.. Priority.Distinct(), .. Enum.GetValues<PriceSource>().Except(Priority)];

    bool Uses(PriceSource source) => source switch { PriceSource.Titrack => UseTitrack, PriceSource.Etor => UseEtor, _ => UseLocal };

    [JsonIgnore] public PriceSource[] Active => [.. Order().Where(Uses)];

    public sealed record Row(int BaseId, float Median, float? Low, float? High, DateTimeOffset? Updated, int? Submissions, int? Devices);

    public static List<Row> Rows(IReadOnlyDictionary<int, Price> table) => [.. table.OrderBy(pair => pair.Key).Select(pair => new Row(pair.Key, pair.Value.Median, pair.Value.Low, pair.Value.High, pair.Value.Updated, pair.Value.Submissions, pair.Value.Devices))];

    static readonly TableColumn<Row>[] DatabaseColumns =
    [
        new() { Header = "Base ID", Width = 90, SortKey = row => row.BaseId, Draw = (row, _) => ImGui.TextUnformatted(row.BaseId.ToString()) },
        new() { Header = "Name", SortKey = row => ItemNames.Of(row.BaseId), Draw = (row, _) => DrawBase(row.BaseId) },
        new() { Header = "Price", Width = 110, SortKey = row => row.Median, Draw = (row, _) => Amount(row.Median) },
        new() { Header = "Low", Width = 110, SortKey = row => row.Low ?? -1f, Draw = (row, _) => Amount(row.Low) },
        new() { Header = "High", Width = 110, SortKey = row => row.High ?? -1f, Draw = (row, _) => Amount(row.High) },
        new() { Header = "Samples", Width = 80, SortKey = row => row.Submissions ?? -1, Draw = (row, _) => ImGui.TextUnformatted(row.Submissions?.ToString() ?? "-") },
        new() { Header = "Devices", Width = 80, SortKey = row => row.Devices ?? -1, Draw = (row, _) => ImGui.TextUnformatted(row.Devices?.ToString() ?? "-") },
        new() { Header = "Updated", Width = 130, SortKey = row => row.Updated?.UtcTicks ?? 0L, Draw = (row, _) => ImGui.TextUnformatted(row.Updated is { } at ? at.ToLocalTime().ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture) : "-") },
    ];

    static void Amount(float? value) => Controls.AmountCell(value is { } v ? Number(v) : "-", value is null ? null : LocalListings.FeIcon);

    internal static HashSet<int> LegendaryIds = [];

    static void DrawBase(int baseId)
    {
        var name = ItemNames.Of(baseId);
        var rarity = LegendaryIds.Contains(baseId) ? 100 : ItemNames.Rarity(baseId);
        if (rarity > 1) ImGui.TextColored(EColor.ToVector4(GameColors.Rarity(rarity)), name);
        else ImGui.TextUnformatted(name);
    }

    static string Number(float value) => value.ToString(value < 10 ? "0.###" : "0.#", System.Globalization.CultureInfo.InvariantCulture);

    static readonly TableColumn<LegendaryEntry>[] LegendaryColumns =
    [
        new() { Header = "Name", SortKey = row => ItemNames.Of(row.SpecialId), Draw = (row, _) => ImGui.TextColored(EColor.ToVector4(GameColors.LegendaryName), ItemNames.Of(row.SpecialId)) },
        new() { Header = "State", Width = 100, SortKey = row => row.Unidentified, Draw = (row, _) => ImGui.TextUnformatted(row.Unidentified ? "Unidentified" : "Identified") },
        new() { Header = "Price", Width = 110, SortKey = row => row.Price.Median, Draw = (row, _) => Amount(row.Price.Median) },
        new() { Header = "Low", Width = 110, SortKey = row => row.Price.Low ?? -1f, Draw = (row, _) => Amount(row.Price.Low) },
        new() { Header = "High", Width = 110, SortKey = row => row.Price.High ?? -1f, Draw = (row, _) => Amount(row.Price.High) },
        new() { Header = "Samples", Width = 80, SortKey = row => row.Price.Submissions ?? -1, Draw = (row, _) => ImGui.TextUnformatted(row.Price.Submissions?.ToString() ?? "-") },
        new() { Header = "Updated", Width = 130, SortKey = row => row.Price.Updated?.UtcTicks ?? 0L, Draw = (row, _) => ImGui.TextUnformatted(row.Price.Updated is { } at ? at.ToLocalTime().ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture) : "-") },
    ];

    enum LocalView { Commodities, Legendaries, Listings }

    LocalView _localView;
    readonly LocalListings _listings = new();
    string _legendaryFilter = "";
    LegendaryPrices? _viewLegendaries;
    List<LegendaryEntry> _legendaryRows = [];

    void DrawLegendaries()
    {
        if (!ReferenceEquals(Legendaries, _viewLegendaries))
        {
            _viewLegendaries = Legendaries;
            _legendaryRows = Legendaries is null ? [] : [.. Legendaries.Entries];
        }
        ImGui.TextDisabled($"{_legendaryRows.Count} legendaries");
        if (_legendaryRows.Count == 0)
        {
            ImGui.TextDisabled("No legendary listings scanned yet");
            return;
        }
        SortableTable.Draw("legendaries", _legendaryRows, LegendaryColumns, ref _legendaryFilter, row => ItemNames.Of(row.SpecialId) + " " + (row.Unidentified ? "Unidentified" : "Identified"), ImGui.GetContentRegionAvail().Y);
    }

    PriceSource _view = PriceSource.Titrack;
    string _filter = "";
    IReadOnlyDictionary<int, Price>? _viewTable;
    List<Row> _rows = [];

    LocalBook? _legendaryIdsBook;
    int _legendaryIdsVersion = -1;

    void RefreshLegendaryIds()
    {
        if (ReferenceEquals(Book, _legendaryIdsBook) && (Book?.Version ?? -1) == _legendaryIdsVersion) return;
        _legendaryIdsBook = Book;
        _legendaryIdsVersion = Book?.Version ?? -1;
        LegendaryIds = Book is null ? [] : [.. Book.Rows.Where(row => row.SpecialId != 0).Select(row => row.SpecialId)];
    }

    internal void DrawDatabase()
    {
        RefreshLegendaryIds();
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted("Database");
        ImGui.SameLine(160);
        Combo.Enum(ref _view, "dbview");
        if (_view == PriceSource.Local)
        {
            ImGui.SameLine();
            Combo.Enum(ref _localView, "localview");
            if (_localView == LocalView.Legendaries)
            {
                DrawLegendaries();
                return;
            }
            if (_localView == LocalView.Listings)
            {
                _listings.Draw(Book);
                return;
            }
        }
        var table = Tables.TryGetValue(_view, out var found) ? found : null;
        if (!ReferenceEquals(table, _viewTable))
        {
            _viewTable = table;
            _rows = table is null ? [] : Rows(table);
        }
        ImGui.SameLine();
        ImGui.TextDisabled($"{_rows.Count} items");
        if (_rows.Count == 0)
        {
            ImGui.TextDisabled(!Uses(_view) ? "This database is turned off" : _view == PriceSource.Local ? "No commodity listings saved yet - search commodities in the trade house" : "Nothing loaded from this database yet");
            return;
        }
        SortableTable.Draw("database", _rows, DatabaseColumns, ref _filter, row => row.BaseId + " " + ItemNames.Of(row.BaseId), ImGui.GetContentRegionAvail().Y);
    }

    string _seasonText = "";
    bool _seasonEditing;

    public override void Draw() => Layout.TabBar("prices", ("Prices", DrawPrices), ("Sources", DrawSources), ("Ground Items", DrawGround), ("Totals", DrawTotals), ("Compare", DrawCompare), ("Wealth", DrawWealth), ("Overlay", DrawOverlay), ("Database", DrawDatabase), ("Share", DrawShare));

    internal void DrawTotals()
    {
        Setting.Checkbox("Show totals", ref ShowTotals, "The FE value of your whole inventory (all four bags) next to the bag's sort button, and while the stash is open the open tab / every stash tab opened since Ember started - the game only loads a tab once you open it. Only what shows a tag counts - the gear colours, bags and Hide below on the Prices tab. Each item counts at its low price (the median when there is no low), unpriced items count 0. Uses the Prices tab's text style and FE icon");
        Setting.Checkbox("Show low-high range", ref TotalRange, "Show the totals as low-high - every item at its low price, then every item at its high price. Off = only the low total");
        Setting.Checkbox("Lock", ref Lock, "The stash total ignores the mouse while locked - hold the move key to drag it anyway");
        Setting.Hotkey("Move key", ref MoveKey, "Hold to move the stash total while locked");
    }

    internal void DrawCompare()
    {
        Setting.Checkbox("Show compare numbers", ref CompareEnabled, "In the trade house, the damage and survival change the game shows when you hover an item are kept and drawn on that item's card, so you can read them for every card you hovered. A card is read again when you hover it after its listing changed");
        ImGui.SeparatorText("Damage");
        Setting.Choice("Corner", ref CompareDamageCorner, "Which corner of the card the damage change sits in. Two compare rows stack away from the card edge", id: "CompareDamageCorner");
        Setting.Slider("Offset X", ref CompareDamageX, -100, 100, "Pixels from the corner, positive moves the number right", id: "CompareDamageX");
        Setting.Slider("Offset Y", ref CompareDamageY, -100, 100, "Pixels from the corner, positive moves the number down", id: "CompareDamageY");
        ImGui.SeparatorText("Survival");
        Setting.Choice("Corner", ref CompareSurvivalCorner, "Which corner of the card the survival change sits in. Two compare rows stack away from the card edge", id: "CompareSurvivalCorner");
        Setting.Slider("Offset X", ref CompareSurvivalX, -100, 100, "Pixels from the corner, positive moves the number right", id: "CompareSurvivalX");
        Setting.Slider("Offset Y", ref CompareSurvivalY, -100, 100, "Pixels from the corner, positive moves the number down", id: "CompareSurvivalY");
        ImGui.Separator();
        Setting.Slider("Hover time (ms)", ref CompareHoverMs, 0, 1000, "How long the numbers must stay the same before they are kept for the card. Only filters flicker while the game rewrites the tooltip, it no longer affects which card the numbers go to. Lower is faster", id: "CompareHoverMs");
        Setting.Style("Text", CompareText, "Size, background and shadow of the numbers - the colour follows the sign (green better, red worse, grey equal)");
    }

    internal void DrawGround()
    {
        Setting.Checkbox("Show on ground items", ref GroundEnabled, "A price tag next to the label of each priced item on the ground in maps");
        Setting.Checkbox("Grey items", ref GroundGrey, "Price tags on grey (normal) items on the ground", id: "GroundGrey");
        Setting.Checkbox("Cyan items", ref GroundCyan, "Price tags on cyan (magic) items on the ground", id: "GroundCyan");
        Setting.Checkbox("Purple items", ref GroundPurple, "Price tags on purple (rare) items on the ground", id: "GroundPurple");
        Setting.Checkbox("Pink items", ref GroundPink, "Price tags on pink items on the ground", id: "GroundPink");
        Setting.Checkbox("Orange items", ref GroundOrange, "Price tags on orange (legendary) items on the ground", id: "GroundOrange");
        Setting.Checkbox("Non-equipment items", ref GroundNonEquipment, "Also tag items that are not gear - currency, materials and the like. They follow the same colour choices");
    }

    internal void DrawPrices()
    {
        Setting.Checkbox("Show prices", ref Enabled, "A price tag on each priced item in the open bag");
        Setting.Choice("Corner", ref Corner, "Which corner of the cell the tag sits in - bottom-right is where the game draws its own item count");
        Setting.Slider("Offset X", ref OffsetX, -100, 100, "Pixels from the corner, positive moves the tag right");
        Setting.Slider("Offset Y", ref OffsetY, -100, 100, "Pixels from the corner, positive moves the tag down");
        Setting.Slider("Map screen offset X", ref MapRewardOffsetX, -100, 100, "Pixels from the corner on the Netherrealm map screen rewards and drops, positive moves the tag right", id: "MapRewardOffsetX");
        Setting.Slider("Map screen offset Y", ref MapRewardOffsetY, -100, 100, "Pixels from the corner on the Netherrealm map screen rewards and drops, positive moves the tag down", id: "MapRewardOffsetY");
        Setting.Slider("Boss screen offset X", ref BossRewardOffsetX, -100, 100, "Pixels from the corner on the boss rewards screen, positive moves the tag right", id: "BossRewardOffsetX");
        Setting.Slider("Boss screen offset Y", ref BossRewardOffsetY, -100, 100, "Pixels from the corner on the boss rewards screen, positive moves the tag down", id: "BossRewardOffsetY");
        Setting.Style("Text", Text, "Colour, size, background and shadow of the tag");
        Setting.Checkbox("Show FE icon", ref ShowIcon, "The FE icon in front of the number - needs the game icons from Setup");
        Setting.Checkbox("Grey gear", ref ShowGrey, "Price tags on grey frame gear (normal)");
        Setting.Checkbox("Cyan gear", ref ShowCyan, "Price tags on cyan frame gear (magic)");
        Setting.Checkbox("Purple gear", ref ShowPurple, "Price tags on purple frame gear (rare)");
        Setting.Checkbox("Pink gear", ref ShowPink, "Price tags on pink frame gear");
        Setting.Checkbox("Orange gear", ref ShowOrange, "Price tags on orange frame gear (legendary)");
        Setting.Checkbox("Skills", ref ShowSkills, "Price tags on everything in the Skills bag, any frame colour");
        Setting.Checkbox("Commodities", ref ShowCommodities, "Price tags on everything in the Commodities bag, any frame colour - in the stash every non-gear item counts as a commodity");
        Setting.Checkbox("Other", ref ShowOther, "Price tags on everything in the Other bag, any frame colour");
        Setting.Slider("Hide below (FE)", ref MinValue, 0, 1000, "No price tag on anything worth less than this - a stack counts as a whole, gear at its median price. Hidden items stay out of the totals too. 0 = show everything", fmt: "%.1f", log: true);
        Setting.Checkbox("Only hide gear", ref MinValueGearOnly, "Hide below only applies to gear, commodities, skills and other items always show. Off = applies to everything");
        Setting.Slider("Refresh every (minutes)", ref RefreshMinutes, MinRefreshMinutes, 60, "How often new prices are downloaded");
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted("Season");
        EmberHUD.Ui.Text.Tip("Which season's prices are shown. 0 = automatic - the season from the game log, else the newest one with recent prices. Never changes what is shared - shared prices always carry the season from the game log. Type a number and press Enter");
        ImGui.SameLine(160);
        ImGui.SetNextItemWidth(120);
        if (!_seasonEditing) _seasonText = Season.ToString();
        var entered = ImGui.InputText("##season", ref _seasonText, 8, ImGuiInputTextFlags.EnterReturnsTrue | ImGuiInputTextFlags.AutoSelectAll | ImGuiInputTextFlags.CharsDecimal);
        _seasonEditing = ImGui.IsItemActive();
        if (entered) Season = int.TryParse(_seasonText, out var typed) ? Math.Max(0, typed) : 0;
        if (Status.Length > 0) ImGui.TextDisabled(Status);
        Setting.Checkbox("Share Exchange prices", ref Contribute, "Reads the game log (Settings > Other > Enable Log) and sends the prices of your Exchange searches to TITrack's price server, the same way TITrack does. Off = nothing is read or sent");
        if (Contribute && ContributeStatus.Length > 0) ImGui.TextDisabled(ContributeStatus);
    }

    int _selectedSource;

    static string Tip(PriceSource source) => source switch
    {
        PriceSource.Titrack => "TITrack's community price server - crowd-sourced Exchange searches, many items, some prices skewed by filtered searches",
        PriceSource.Etor => "ETor's community price server - only downloaded, nothing is sent. An unofficial interface, it can change or go away",
        _ => "Your own database, filled from the trade house while it is open - prices come from the cheap pile of commodity listings you scanned",
    };

    void DrawSources()
    {
        ImGui.TextDisabled("Drag to reorder - higher in the list wins when two databases price the same item");
        List<PriceSource> order = [.. Order()];
        if (ListEditor.ReorderableList("sources", order, ref _selectedSource, (source, _) =>
        {
            var on = Uses(source);
            if (ImGui.Checkbox("##use", ref on))
            {
                if (source == PriceSource.Titrack) UseTitrack = on;
                else if (source == PriceSource.Etor) UseEtor = on;
                else UseLocal = on;
            }
            ImGui.SameLine();
            ImGui.TextUnformatted(source == PriceSource.Titrack ? "TITrack" : source.ToString());
            EmberHUD.Ui.Text.Tip(Tip(source));
        }, new ListOpts { ShowIndex = true })) Priority = [.. order];
        Setting.Slider("Minimum samples", ref MinSamples, 0, 200, "Ignore a price that fewer than this many Exchange searches back, the next database in the list prices the item instead. 0 = keep everything. Only TITrack reports sample counts, ETor prices always stay");
        Setting.Checkbox("Exclude price outliers", ref ExcludeOutliers, "Local prices only. Drops listings priced far above or below the bulk before picking the price - if most of an item is listed at 1 FE and a few at 200 FE, the item is worth 1 FE. Off = every listing counts");
        Setting.Slider("Ignore older than (days)", ref IgnoreOlderThanDays, 0, 90, "Ignore a price whose last update is older than this many days, the next database in the list prices the item instead. 0 = keep everything. Applies to every database");
        Setting.Slider("Purge local data after (days)", ref PurgeAfterDays, 0, 365, "Delete saved local trade house listings that were last seen more than this many days ago, on start and then every hour. 0 = never. Only your local database is purged, TITrack and ETor are downloaded copies");
        if (ImGui.Button("Purge now##local")) PurgeResult = PurgeAfterDays <= 0 ? "Set a number of days first" : PurgeLocal is { } purge ? $"Removed {purge()} saved listings" : "";
        if (PurgeResult.Length > 0)
        {
            ImGui.SameLine();
            ImGui.TextDisabled(PurgeResult);
        }
    }
}
