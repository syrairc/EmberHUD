using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using EmberHUD;

namespace Prices;

public sealed class Main : Plugin<Settings>
{
    const string FeIcon = "UI_Item_chuhuoyuanzhiV3_Icon_128";
    const float IconGap = 2;
    const float GroundGap = 4;

    static readonly Dictionary<int, Price> NoPrices = [];

    readonly Dictionary<(int BaseId, int Count, bool Gear), string> _tags = [];
    IReadOnlyDictionary<int, Price> _prices = NoPrices;
    HttpClient? _http;
    long _nextAt;
    (int, int?) _seasonKey;
    (int, int?) _jobKey;
    (int, int?)? _fullKey;
    int? _heldSeason;
    Contribution? _contribution;
    CancellationTokenSource? _stopContributing;

    internal string DataDirectory { get; set; } = Path.Combine(AppContext.BaseDirectory, "Config", "Prices");

    internal HttpClient Http
    {
        get => _http ??= new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        set => _http = value;
    }

    internal BackgroundJob<Synced> Job { get; set; } = new();

    internal Func<long> Clock { get; set; } = () => Environment.TickCount64;

    internal int? LogSeason { get; set; }

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
        _fullKey = null;
        if (PriceSync.CachedPath(DataDirectory, Settings.Season, LogSeason) is { } path && PriceSync.Load(path) is { } file)
            Apply(new Synced(file, file.Season, "cached", null), File.GetLastWriteTime(path));
    }

    public override void Tick()
    {
        if (Settings.GroundEnabled) Game.WantGroundItems();
        Contribute();
        if (Job.TakeFinished() is { } done)
        {
            var result = done.GetAwaiter().GetResult();
            if (_jobKey == (Settings.Season, LogSeason))
            {
                Apply(result);
                if (result.Error is null) _fullKey = _jobKey;
            }
            _nextAt = Clock() + Math.Max(Settings.MinRefreshMinutes, Settings.RefreshMinutes) * 60_000L;
        }
        var seasonKey = (Settings.Season, LogSeason);
        if (seasonKey != _seasonKey) _nextAt = 0;
        if (!(Settings.Enabled || Settings.GroundEnabled) || Job.Running || Clock() < _nextAt) return;
        _seasonKey = _jobKey = seasonKey;
        var http = Http;
        var directory = DataDirectory;
        var full = _fullKey != seasonKey;
        Job.Start(() => PriceSync.Sync(http, directory, seasonKey.Season, seasonKey.LogSeason, full));
    }

    public override void OnDisable() => StopContributing();

    public override void Dispose()
    {
        StopContributing();
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

    void Apply(Synced result, DateTime? at = null)
    {
        if (result.File is { } file)
        {
            Prices = file.Rows;
            _heldSeason = result.Season;
        }
        else if (result.Season is { } season && _heldSeason is { } held && held != season)
        {
            Prices = NoPrices;
            _heldSeason = season;
        }
        var from = result.Season is { } shown ? $"season {shown} ({result.Source})" : "no season";
        Settings.Status = result.Error is { } error ? $"download failed: {error} - {from}" : $"{_prices.Count} prices, {from}, updated {at ?? DateTime.Now:HH:mm}";
    }

    public override void Render()
    {
        DrawInventory();
        DrawGround();
    }

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
            if (!onTab(item) || !Settings.Shows(item.RareValue) || TagFor(item.BaseId, item.Count, item.Bag == InventoryBag.Equipment) is not { } text) continue;
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
            if (!Settings.ShowsOnGround(item) || TagFor(item.BaseId, item.Count, item.Equipment) is not { } text) continue;
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

    string? TagFor(int baseId, int count, bool gear)
    {
        var key = (baseId, count, gear);
        if (_tags.TryGetValue(key, out var text)) return text;
        text = Tags.Text(baseId, count, gear, _prices);
        if (text is not null) _tags[key] = text;
        return text;
    }
}
