using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Tartarus;

public sealed record PullEntry(DateTime When, int Run, int Day, int Cell, int Uid, int Species, int Tier, string Cause, int Collected);

public sealed record EventEntry(DateTime When, int Run, int Day, int Cell, int EventId, string Name, bool Open, int Species, int Tier, bool Marked);

public sealed class MinigameStore
{
    const int EntryCap = 5000;
    const string FileName = "minigame.json";

    sealed record Stored(List<PullEntry> Entries, List<EventEntry>? Events = null);

    public List<PullEntry> Entries { get; private set; } = [];
    public List<EventEntry> Events { get; private set; } = [];
    public bool Dirty { get; private set; }
    public bool Loaded { get; private init; } = true;

    public void Add(PullEntry entry)
    {
        Entries.Add(entry);
        Trim();
        Dirty = true;
    }

    public void Add(EventEntry entry)
    {
        Events.Add(entry);
        Trim();
        Dirty = true;
    }

    void Trim()
    {
        if (Events.Count > EntryCap) Events.RemoveRange(0, Events.Count - EntryCap);
        if (Entries.Count > EntryCap) Entries.RemoveRange(0, Entries.Count - EntryCap);
    }

    public void Clear()
    {
        Entries = [];
        Events = [];
        Dirty = true;
    }

    public static MinigameStore Load(string directory)
    {
        var path = Path.Combine(directory, FileName);
        if (!File.Exists(path)) return new();
        string text;
        try { text = File.ReadAllText(path); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return new() { Loaded = false }; }
        try
        {
            var stored = JsonSerializer.Deserialize<Stored>(text) ?? throw new JsonException();
            if (stored.Entries is null || stored.Entries.Any(entry => entry?.Cause is null) || stored.Events?.Any(entry => entry?.Name is null) == true) throw new JsonException();
            var store = new MinigameStore { Entries = stored.Entries, Events = stored.Events ?? [] };
            store.Trim();
            return store;
        }
        catch (Exception e) when (e is JsonException or NotSupportedException)
        {
            try { File.Move(path, path + ".bad", true); } catch (Exception) { }
            return new();
        }
    }

    public void Absorb(MinigameStore pending)
    {
        Entries.AddRange(pending.Entries);
        Events.AddRange(pending.Events);
        Trim();
        Dirty |= pending.Dirty;
    }

    public void Save(string directory)
    {
        if (!Loaded) return;
        var path = Path.Combine(directory, FileName);
        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(new Stored(Entries, Events)));
            File.Move(path + ".tmp", path, true);
            Dirty = false;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            try { File.Delete(path + ".tmp"); } catch (Exception) { }
        }
    }
}
