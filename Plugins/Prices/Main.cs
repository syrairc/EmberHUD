using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using EmberHUD;
using EmberHUD.Ui;

namespace Prices;

public sealed class Main : Plugin<Settings>
{
    const string FeIcon = LocalListings.FeIcon;
    const float IconGap = 2;
    const float GroundGap = 4;
    const float SortGap = 8;
    const string SortPart = "UIButton_Arrage";

    static readonly System.Drawing.Color CompareGreen = System.Drawing.Color.FromArgb(0x5C, 0xD6, 0x6B);
    static readonly System.Drawing.Color CompareRed = System.Drawing.Color.FromArgb(0xE5, 0x57, 0x4F);
    static readonly System.Drawing.Color CompareGrey = System.Drawing.Color.FromArgb(0xB0, 0xB0, 0xB0);

    static readonly Dictionary<int, Price> NoPrices = [];

    readonly Dictionary<(int BaseId, int Count, bool Gear, ulong Legendary, bool Unidentified), string> _tags = [];
    IReadOnlyDictionary<int, Price> _prices = NoPrices;
    HttpClient? _http;
    long _nextAt;
    (int, int?, string) _seasonKey;
    (int, int?, string) _jobKey;
    (int, int?, string)? _fullKey;
    int? _heldSeason;
    Synced? _synced;
    (int Min, int Days, bool Local) _builtKey;
    string _statusBase = "";
    LocalBook? _book;
    int? _bookSeason;
    IReadOnlyDictionary<int, Price>? _local;
    LegendaryPrices? _localLegendary;
    TradeGood[]? _lastGoods;
    bool _localChanged, _rebuild, _builtOutliers = true;
    long _nextLocalAt, _nextFlushAt, _nextPurgeAt;
    Contribution? _contribution;
    CancellationTokenSource? _stopContributing;
    readonly Dictionary<int, InventoryItem[]> _stashTabs = [];
    readonly Overlay _overlay = new();
    readonly CompareCache _compare = new();
    int _unusedWidth, _unusedHeight;

    internal string DataDirectory { get; set; } = Path.Combine(AppContext.BaseDirectory, "Config", "Prices");

    internal HttpClient Http
    {
        get => _http ??= new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        set => _http = value;
    }

    internal BackgroundJob<Synced> Job { get; set; } = new();

    internal Func<long> Clock { get; set; } = () => Environment.TickCount64;

    internal Func<Vector2> Cursor { get; set; } = () => Hexa.NET.ImGui.ImGui.GetMousePos();

    internal Func<long> UnixClock { get; set; } = () => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    internal int? LogSeason { get; set; }

    internal LocalBook? Book => _book;

    internal LegendaryPrices? Legendary { get; private set; }

    internal int StashTabsSeen => _stashTabs.Count;

    public IReadOnlyDictionary<int, Price> Prices
    {
        get => _prices;
        internal set
        {
            _prices = value;
            _tags.Clear();
        }
    }

    public override void OnEnable()
    {
        Settings.PurgeLocal = PurgeNow;
        (Settings.ShareFolder, Settings.ExportShare, Settings.ImportShare) = (ShareDirectory, ExportShare, ImportShare);
        _fullKey = null;
        if (PriceSources.Cached(DataDirectory, Settings.Active, Settings.Season, LogSeason) is { } cached) Apply(cached.Synced, cached.At);
    }

    public override void Tick()
    {
        if (Settings.GroundEnabled) Game.WantGroundItems();
        Contribute();
        CollectLocal();
        if (_rebuild || ((_synced is not null || _local is not null) && _builtKey != BuildKey))
        {
            _rebuild = false;
            Prices = Build();
            UpdateStatus();
        }
        if (Job.TakeFinished() is { } done)
        {
            var result = done.GetAwaiter().GetResult();
            if (_jobKey == (Settings.Season, LogSeason, SourceKey))
            {
                Apply(result);
                if (result.Error is null) _fullKey = _jobKey;
            }
            _nextAt = Clock() + Math.Max(Settings.MinRefreshMinutes, Settings.RefreshMinutes) * 60_000L;
        }
        var seasonKey = (Settings.Season, LogSeason, SourceKey);
        if (seasonKey != _seasonKey) _nextAt = 0;
        if (!(Settings.Enabled || Settings.GroundEnabled) || Job.Running || Clock() < _nextAt) return;
        _seasonKey = _jobKey = seasonKey;
        var http = Http;
        var directory = DataDirectory;
        var full = _fullKey != seasonKey;
        var active = Settings.Active;
        Job.Start(() => PriceSources.Sync(http, directory, active, seasonKey.Item1, seasonKey.Item2, full));
    }

    string SourceKey => string.Join(',', Settings.Active);

    public override void OnDisable()
    {
        StopContributing();
        FlushBook();
    }

    public override void Dispose()
    {
        StopContributing();
        FlushBook();
        _http?.Dispose();
    }

    void Contribute()
    {
        if (!Settings.Contribute)
        {
            StopContributing();
            return;
        }
        if (_contribution is null)
        {
            if (Game.ExePath is not { } exe)
            {
                Settings.ContributeStatus = "waiting for the game";
                return;
            }
            StartContributing(Exchange.LogPath(exe));
        }
        var contribution = _contribution!;
        Settings.ContributeStatus = contribution.Status;
        if (contribution.Season is { } season) LogSeason = season;
        if (!contribution.Found.IsEmpty) Prices = Merged(_prices, contribution.Found);
    }

    void StartContributing(string logPath)
    {
        var contribution = _contribution = new Contribution(Http, logPath, DataDirectory);
        var stop = _stopContributing = new CancellationTokenSource();
        Task.Run(async () =>
        {
            var flushAt = DateTime.UtcNow.AddMinutes(1);
            try
            {
                contribution.Start();
                while (!stop.IsCancellationRequested)
                {
                    var now = DateTime.UtcNow;
                    try
                    {
                        contribution.Poll(now);
                        if (now >= flushAt)
                        {
                            contribution.Flush(() => Settings.Contribute && !stop.IsCancellationRequested, now);
                            flushAt = now.AddMinutes(1);
                        }
                    }
                    catch (Exception ex)
                    {
                        contribution.Fail(ex.Message);
                    }
                    contribution.Describe(now);
                    await Task.Delay(1000, stop.Token);
                }
            }
            catch (OperationCanceledException)
            {
                // stopped
            }
            catch (Exception ex)
            {
                contribution.Fail(ex.Message);
                contribution.Describe(DateTime.UtcNow);
            }
        });
    }

    void StopContributing()
    {
        _stopContributing?.Cancel();
        _stopContributing = null;
        _contribution = null;
        Settings.ContributeStatus = "";
    }

    internal static Dictionary<int, Price> Merged(IReadOnlyDictionary<int, Price> prices, ConcurrentQueue<Search> found)
    {
        var merged = new Dictionary<int, Price>(prices);
        while (found.TryDequeue(out var search))
        {
            var median = (float)Exchange.ReferencePrice(search.PricesFe);
            merged[search.BaseId] = merged.TryGetValue(search.BaseId, out var old) ? old with { Median = median } : new Price(median, null, null);
        }
        return merged;
    }

    (int, int, bool) BuildKey => (Settings.MinSamples, Settings.IgnoreOlderThanDays, Settings.UseLocal);

    Dictionary<PriceSource, IReadOnlyDictionary<int, Price>> AllTables()
    {
        var tables = new Dictionary<PriceSource, IReadOnlyDictionary<int, Price>>(_synced?.Tables ?? new Dictionary<PriceSource, IReadOnlyDictionary<int, Price>>());
        if (Settings.UseLocal && _local is not null) tables[PriceSource.Local] = _local;
        return tables;
    }

    IReadOnlyDictionary<int, Price> Build()
    {
        _builtKey = BuildKey;
        var tables = AllTables();
        Settings.Tables = tables;
        DateTimeOffset? notBefore = Settings.IgnoreOlderThanDays > 0 ? DateTimeOffset.FromUnixTimeSeconds(UnixClock()) - TimeSpan.FromDays(Settings.IgnoreOlderThanDays) : null;
        Legendary = Settings.UseLocal ? _localLegendary?.Filtered(Settings.MinSamples, notBefore) : null;
        Settings.Legendaries = Legendary;
        _tags.Clear();
        if (tables.Count == 0) return _synced?.File?.Rows ?? NoPrices;
        return PriceSources.Combine(Settings.Active.Where(tables.ContainsKey).Select(source => tables[source]), Settings.MinSamples, notBefore);
    }

    int? FileSeason() => _heldSeason ?? LogSeason ?? (Settings.Season > 0 ? Settings.Season : null);

    void CollectLocal()
    {
        if (!Settings.UseLocal)
        {
            if (_book is null) return;
            FlushBook();
            (_book, _bookSeason, _local, _localLegendary, _localChanged, _rebuild) = (null, null, null, null, false, true);
            Settings.Book = null;
            return;
        }
        Game.WantTradeGoods();
        var now = UnixClock();
        OpenBook();
        if (Game.State.TradeGoods is { } goods && !ReferenceEquals(goods, _lastGoods))
        {
            _lastGoods = goods;
            if (_book!.Add(goods, now)) _localChanged = true;
        }
        if (Settings.ExcludeOutliers != _builtOutliers) (_builtOutliers, _localChanged, _nextLocalAt) = (Settings.ExcludeOutliers, true, 0);
        if (_localChanged && now >= _nextLocalAt)
        {
            _local = _book!.Prices(now, LocalBook.Recent, _builtOutliers);
            _localLegendary = _book.Legendary(now, LocalBook.Recent, _builtOutliers);
            (_localChanged, _rebuild, _nextLocalAt) = (false, true, now + 5);
        }
        // ponytail: flushed on the main thread, a few hundred lines per 5 s is nothing - move to the job if a scan ever grows to tens of thousands
        if (now >= _nextFlushAt)
        {
            FlushBook();
            _nextFlushAt = now + 5;
        }
        if (Settings.PurgeAfterDays > 0 && now >= _nextPurgeAt)
        {
            PurgeNow();
            _nextPurgeAt = now + 3600;
        }
    }

    // ponytail: a season switch drops the old book and loads the other file, nothing is shared between seasons; a book started before any season was known is kept and joins the first file
    void OpenBook()
    {
        var season = FileSeason();
        if (_book is not null && season == _bookSeason) return;
        FlushBook();
        if (_book is null || _bookSeason is not null) _book = new LocalBook();
        if (season is { } known) _book.Load(DataDirectory, known);
        (Settings.Book, _bookSeason, _localChanged, _nextPurgeAt) = (_book, season, true, 0);
    }

    void FlushBook()
    {
        if (_book is not null && _bookSeason is { } season) _book.Flush(DataDirectory, season);
    }

    internal int PurgeNow()
    {
        if (_book is null || Settings.PurgeAfterDays <= 0) return 0;
        var removed = _book.Purge(UnixClock() - Settings.PurgeAfterDays * 86400L);
        if (removed > 0) _localChanged = true;
        return removed;
    }

    internal string ShareDirectory => Path.Combine(DataDirectory, "Share");

    IReadOnlyDictionary<int, Price>? Stored(PriceSource source, int season)
    {
        if (_synced is { Tables: { } tables } && _synced.Season == season && tables.TryGetValue(source, out var table)) return table;
        var cache = source == PriceSource.Titrack ? Path.Combine(DataDirectory, $"prices-{season}.json") : EtorSync.CachePath(DataDirectory, season);
        return PriceSources.WithImported(DataDirectory, source, season, PriceSync.Load(cache)?.Rows);
    }

    internal string ExportShare(PriceSource[] which)
    {
        if (which.Length == 0) return "Pick a database to export";
        if (FileSeason() is not { } season) return "Season not known yet";
        try
        {
            LocalBook? book = null;
            if (which.Contains(PriceSource.Local))
            {
                book = _book is not null && _bookSeason == season ? _book : new LocalBook();
                if (!ReferenceEquals(book, _book)) book.Load(DataDirectory, season);
            }
            var content = new ShareContent(season, book?.Rows.ToList(), book?.AffixTexts,
                which.Contains(PriceSource.Titrack) ? Stored(PriceSource.Titrack, season) ?? new Dictionary<int, Price>() : null,
                which.Contains(PriceSource.Etor) ? Stored(PriceSource.Etor, season) ?? new Dictionary<int, Price>() : null);
            var now = DateTimeOffset.FromUnixTimeSeconds(UnixClock());
            var path = Path.Combine(ShareDirectory, Share.FileName(season, now));
            Share.Write(path, content, now);
            return $"Exported {string.Join(", ", which.Select(source => source.ToString()))} (season {season}) to {path}";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return "Export failed: " + ex.Message;
        }
    }

    internal string ImportShare(string path)
    {
        var (content, error) = Share.Read(path.Trim().Trim('"'));
        if (content is null) return error!;
        try
        {
            var parts = new List<string>();
            if (content.LocalRows is not null)
            {
                var live = Settings.UseLocal && _book is not null && _bookSeason == content.Season;
                var book = live ? _book! : new LocalBook();
                if (!live) book.Load(DataDirectory, content.Season);
                var (added, updated, skipped) = book.Merge(content.LocalRows, content.Affixes ?? new Dictionary<int, string>());
                if (live) _localChanged = true;
                else
                {
                    book.Flush(DataDirectory, content.Season);
                    if (book.Dirty) return "Could not write the local database file";
                }
                parts.Add($"{Count(added)} new and {Count(updated)} updated listings" + (skipped > 0 ? $" ({Count(skipped)} skipped)" : ""));
            }
            var (titrack, etor) = Share.ImportTables(DataDirectory, content);
            if (content.Titrack is not null) parts.Add($"{Count(titrack)} TITrack prices");
            if (content.Etor is not null) parts.Add($"{Count(etor)} ETor prices");
            if (titrack + etor > 0 && PriceSources.Cached(DataDirectory, Settings.Active, Settings.Season, LogSeason) is { } cached) Apply(cached.Synced, cached.At);
            var other = FileSeason() is { } current && current != content.Season ? $", not the current season {current} - merged into its own files" : "";
            return parts.Count == 0 ? $"The file has no databases (season {content.Season})" : $"Imported {string.Join(", ", parts)} (season {content.Season}{other})";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return "Import failed: " + ex.Message;
        }
    }

    static string Count(int number) => number.ToString("N0", System.Globalization.CultureInfo.InvariantCulture);

    void UpdateStatus()
    {
        var local = Settings.UseLocal && _book is not null ? $"Local {_book.Count} rows" : "";
        Settings.Status = _statusBase.Length == 0 ? local : local.Length == 0 ? _statusBase : $"{_statusBase}, {local}";
    }

    void Apply(Synced result, DateTime? at = null)
    {
        if (result.File is not null)
        {
            _synced = result;
            _heldSeason = result.Season;
            Prices = Build();
        }
        else if (result.Season is { } season && _heldSeason is { } held && held != season)
        {
            Prices = NoPrices;
            _heldSeason = season;
        }
        var from = result.Season is { } shown ? $"season {shown} ({result.Source})" : "no season";
        var databases = result.Databases.Length > 0 ? $" from {result.Databases}" : "";
        _statusBase = result.Error is { } error ? $"download failed: {error} - {from}" : $"{_prices.Count} prices{databases}, {from}, updated {at ?? DateTime.Now:HH:mm}";
        UpdateStatus();
    }

    public override void Render()
    {
        DrawInventory();
        DrawTotals();
        DrawGround();
        DrawCompare();
    }

    internal static string CompareLabel(float value) =>
        Math.Abs(value) < 0.05f ? "0%" : value == 999 ? "+999%" : (value > 0 ? "+" : "") + value.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + "%";

    void DrawCompare()
    {
        var page = Settings.CompareEnabled ? Game.State.Trade : null;
        _compare.SettleMs = Settings.CompareHoverMs;
        _compare.Feed(page, Game.State.Compare, Cursor(), Clock());
        if (page is null) return;
        var style = Settings.CompareText.Clone();
        foreach (var listing in page.Listings)
        {
            if (_compare.Rows(page.Page, listing) is not { } rows) continue;
            using var clip = Draw.Clip(listing.Min, listing.Max);
            DrawCompareSide(rows, row => row.Damage, Settings.CompareDamageCorner, Settings.CompareDamageX, Settings.CompareDamageY, listing, style, Game.State.Compare);
            DrawCompareSide(rows, row => row.Survival, Settings.CompareSurvivalCorner, Settings.CompareSurvivalX, Settings.CompareSurvivalY, listing, style, Game.State.Compare);
        }
    }

    static void DrawCompareSide(CompareRow[] rows, Func<CompareRow, float?> pick, Corner corner, int offsetX, int offsetY, TradeListing listing, TextStyle style, EquipCompare? tooltip)
    {
        var bottom = corner is Corner.BottomLeft or Corner.BottomRight;
        for (var i = 0; i < rows.Length; i++)
        {
            if (pick(rows[i]) is not { } value) continue;
            var text = CompareLabel(value);
            style.Color = text == "0%" ? CompareGrey : value > 0 ? CompareGreen : CompareRed;
            var size = Draw.MeasureText(text, style);
            var below = bottom ? i - (rows.Length - 1) : i;
            var at = Tags.Anchor(corner, listing.Min, listing.Max, size) + new Vector2(offsetX, offsetY + below * size.Y);
            if (CompareRects.Hit(tooltip, at, at + size)) continue;
            Draw.Text(text, at, style);
        }
    }

    void DrawTotals()
    {
        var state = Game.State;
        if (state.Stash is { } shown && state.StashItems is { } shownItems) _stashTabs[shown.Tab] = shownItems;
        if (!Settings.ShowTotals || OpenKind(state.Ui) is null) return;
        Game.WantInventory();
        var icon = TagIcon(out var hasIcon);
        if (state.Inventory is { } inventory && SortButton(state.Ui) is { } sort)
            DrawTag(TotalText(TotalOf(inventory)), icon, hasIcon, size => new Vector2(sort.Min.X - SortGap - size.X, (sort.Min.Y + sort.Max.Y - size.Y) / 2));
        if (state.Stash is { } stash && _stashTabs.TryGetValue(stash.Tab, out var tab))
        {
            (double Low, double High) all = default;
            foreach (var items in _stashTabs.Values) all = TotalOf(items) is var (low, high) ? (all.Low + low, all.High + high) : all;
            DrawTag($"{TotalText(TotalOf(tab))} / {TotalText(all)}", icon, hasIcon, Movable);
        }
    }

    internal (double Low, double High) TotalOf(IEnumerable<InventoryItem> items)
    {
        double low = 0, high = 0;
        foreach (var item in items)
        {
            if (!Settings.Shows(item) || Settings.TooCheap(item, Tags.Value(item, _prices, Legendary))) continue;
            low += Tags.Value(item, _prices, Legendary, Tags.LowEnd);
            high += Tags.Value(item, _prices, Legendary, Tags.HighEnd);
        }
        return (low, high);
    }

    internal string TotalText((double Low, double High) total) => Settings.TotalRange && Fe(total.High) is var high && Fe(total.Low) is var low && low != high ? $"{low}-{high}" : Fe(total.Low);

    Vector2 Movable(Vector2 size)
    {
        _overlay.Locked = Settings.Lock;
        _overlay.MoveHeld = Settings.MoveKey.Key != Key.None && Input.IsDown(Settings.MoveKey.Key);
        var origin = new Vector2(Settings.PanelX, Settings.PanelY);
        var (hovered, _, active) = _overlay.Handle("prices-stash", ref origin, size, ref Settings.PanelX, ref Settings.PanelY, ref _unusedWidth, 0, 4000, ref _unusedHeight, 0, 0, Overlay.ResizeEdge.None);
        if (hovered || active) Overlay.ClickBlocker("prices-stash", origin, size);
        if (_overlay.Movable) Overlay.DragHint(origin, origin + size, active, hovered, Overlay.ResizeEdge.None);
        return origin;
    }

    static NamedRect? SortButton(GameUi ui)
    {
        foreach (var panel in ui.Panels)
            if (panel.Kind == PanelKind.Inventory)
                foreach (var part in panel.Parts)
                    if (part.Name == SortPart) return part;
        return null;
    }

    static string Fe(double fe) => Tags.Format(fe) ?? "0";

    void DrawInventory()
    {
        if (!Settings.Enabled) return;
        var state = Game.State;
        var kind = OpenKind(state.Ui);
        if (kind is null) return;
        Game.WantInventory();
        if (kind == PanelKind.Inventory)
        {
            if (state.Bag is { } bag && state.Inventory is { } inventory)
                DrawTags(inventory, bag.ClipMin, bag.ClipMax, bag.Cells, item => item.Bag == bag.Tab);
        }
        else if (state.Stash is { } stash && state.StashItems is { } items)
            DrawTags(items, stash.ClipMin, stash.ClipMax, stash.Cells, _ => true);
    }

    void DrawTags(IReadOnlyList<InventoryItem> items, Vector2 clipMin, Vector2 clipMax, BagCell[] cells, Func<InventoryItem, bool> onTab)
    {
        var icon = TagIcon(out var hasIcon);
        using var clip = Draw.Clip(clipMin, clipMax);
        foreach (var item in items)
        {
            if (!onTab(item) || !Settings.Shows(item) || Settings.TooCheap(item, Tags.Value(item, _prices, Legendary)) || TagFor(item.BaseId, item.Count, item.Bag == InventoryBag.Equipment, item.SpecialId, item.Unidentified, item.PrefixIds, item.SuffixIds) is not { } text) continue;
            foreach (var cell in cells)
            {
                if (cell.Slot != item.Slot) continue;
                DrawTag(text, icon, hasIcon, size => Tags.Anchor(Settings.Corner, cell.Min, cell.Max, size) + new Vector2(Settings.OffsetX, Settings.OffsetY));
                break;
            }
        }
    }

    void DrawGround()
    {
        if (!Settings.GroundEnabled || Game.State.GroundItems is not { } items || Game.State.Ui.AnyFullScreen) return;
        var icon = TagIcon(out var hasIcon);
        foreach (var item in items)
        {
            if (!Settings.ShowsOnGround(item) || TagFor(item.BaseId, item.Count, item.Equipment, item.SpecialId, item.PrefixIds.Length + item.SuffixIds.Length == 0, item.PrefixIds, item.SuffixIds) is not { } text) continue;
            DrawTag(text, icon, hasIcon, size => new Vector2(item.LabelMax.X + GroundGap, (item.LabelMin.Y + item.LabelMax.Y - size.Y) / 2));
        }
    }

    Sprite TagIcon(out bool hasIcon)
    {
        Sprite icon = default;
        hasIcon = Settings.ShowIcon && Draw.TryGameIcon(FeIcon, out icon);
        return icon;
    }

    void DrawTag(string text, Sprite icon, bool hasIcon, Func<Vector2, Vector2> place)
    {
        var style = Settings.Text;
        var size = Draw.MeasureText(text, style);
        var height = size.Y;
        var at = place(new Vector2(size.X + (hasIcon ? height + IconGap : 0), height));
        if (hasIcon)
        {
            Draw.Image(icon, at, at + new Vector2(height));
            at.X += height + IconGap;
        }
        Draw.Text(text, at, style);
    }

    static PanelKind? OpenKind(GameUi ui)
    {
        foreach (var panel in ui.Panels)
            if (panel.Kind is PanelKind.Inventory or PanelKind.Stash) return panel.Kind;
        return null;
    }

    internal string? TagFor(int baseId, int count, bool gear, int specialId = 0, bool unidentified = false, int[]? prefixIds = null, int[]? suffixIds = null)
    {
        int[] affixIds = specialId == 0 ? [] : [.. prefixIds ?? [], .. suffixIds ?? []];
        var key = (baseId, count, gear, specialId == 0 ? 0 : LegendaryPrices.Key(specialId, affixIds), specialId != 0 && unidentified);
        if (_tags.TryGetValue(key, out var text)) return text;
        text = specialId != 0 && (Legendary?.Of(specialId, unidentified, affixIds) ?? Tags.Find(_prices, specialId)) is { } special
            ? Tags.Text(special, 1, gear) is { } priced ? (unidentified ? "?" + priced : priced) : null
            : Tags.Text(baseId, count, gear, _prices);
        if (text is not null) _tags[key] = text;
        return text;
    }
}
