using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using EmberHUD;

namespace NameplatesPlugin;

public static class BarTextures
{
    const int Width = 8;
    const int Height = 32;
    static readonly string[] BuiltIns = ["Flat", "Gradient", "Gloss", "Bevel"];
    static readonly HashSet<string> Warned = [];
    static readonly string FallbackKey = Key("Flat");
    static List<string> _names = [.. BuiltIns];
    static Dictionary<string, string> _keys = Index(BuiltIns);

    internal static Func<string, string, bool> PngLoader { get; set; } = Textures.LoadPng;

    public static IReadOnlyList<string> Names => _names;

    public static string Fallback => "Flat";

    public static string DropInFolder { get; } = Path.Combine(AppContext.BaseDirectory, "Config", "Nameplates", "Textures");

    public static void Load(string dropInFolder)
    {
        foreach (var name in BuiltIns)
        {
            var rgba = Generate(name, out var width, out var height);
            Textures.Upload(Key(name), rgba, width, height);
        }
        Register(Scan(dropInFolder));
    }

    internal static void Register(IEnumerable<string> paths)
    {
        var names = new List<string>(BuiltIns);
        foreach (var path in paths)
        {
            var name = Path.GetFileNameWithoutExtension(path);
            if (names.Contains(name, StringComparer.OrdinalIgnoreCase)) continue;
            if (PngLoader(Key(name), path)) names.Add(name);
            else if (Warned.Add(path)) Log.Warn($"Nameplates: skipped unreadable texture {path}");
        }
        _keys = Index(names);
        _names = names;
    }

    public static Sprite? Get(string name)
    {
        if (name is null || !_keys.TryGetValue(name, out var key)) key = FallbackKey;
        return Textures.TryGet(key, out var sprite) ? sprite : null;
    }

    static Dictionary<string, string> Index(IEnumerable<string> names)
    {
        var keys = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in names) keys.TryAdd(name, Key(name));
        return keys;
    }

    internal static string[] Scan(string folder)
    {
        try
        {
            Directory.CreateDirectory(folder);
            return Directory.EnumerateFiles(folder, "*.png")
                .Where(path => !BuiltIns.Contains(Path.GetFileNameWithoutExtension(path), StringComparer.OrdinalIgnoreCase))
                .OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            if (Warned.Add(folder)) Log.Warn($"Nameplates: cannot read texture folder {folder} - {ex.Message}");
            return [];
        }
    }

    internal static byte[] Generate(string name, out int width, out int height)
    {
        width = Width;
        height = Height;
        var rgba = new byte[Width * Height * 4];
        for (var row = 0; row < Height; row++)
        {
            var value = (byte)(name switch
            {
                "Gradient" => 255 - row * 127 / (Height - 1),
                "Gloss" => row is >= 3 and < 12 ? 255 : 190,
                "Bevel" => row == 0 ? 255 : row == Height - 1 ? 110 : 200,
                _ => 255,
            });
            for (var column = 0; column < Width; column++)
            {
                var at = (row * Width + column) * 4;
                rgba[at] = rgba[at + 1] = rgba[at + 2] = value;
                rgba[at + 3] = 255;
            }
        }
        return rgba;
    }

    static string Key(string name) => $"nameplates/{name}";
}
