using System.IO;
using System.Text.Json;

namespace KitRoles.Model;

public enum InstType { Unknown = 0, Kick = 1, Snare = 2, Tom = 4, HiHat = 5, Crash = 7, Ride = 8, Splash = 9, China = 10 }

/// <summary>Instrument names extracted from the V51 firmware (tools/export_instruments.py).</summary>
public static class InstrumentCatalog
{
    // bank -> rows of [name, type, zone]
    private static readonly Dictionary<int, JsonElement[][]> Banks = Load();

    private static Dictionary<int, JsonElement[][]> Load()
    {
        using var s = typeof(InstrumentCatalog).Assembly.GetManifestResourceStream("instruments.json")!;
        var raw = JsonSerializer.Deserialize<Dictionary<string, JsonElement[][]>>(s)!;
        return raw.ToDictionary(kv => int.Parse(kv.Key), kv => kv.Value);
    }

    public static bool IsEmpty(int bank, int inst) => bank == 0 && inst == 0;

    public static string Name(int bank, int inst) =>
        Banks.TryGetValue(bank, out var rows) && inst < rows.Length
            ? rows[inst][0].GetString()!
            : $"{BankLabel(bank)} #{inst}";

    public static InstType Type(int bank, int inst) =>
        bank == 0 && inst < Banks[0].Length ? (InstType)Banks[0][inst][1].GetInt32() : InstType.Unknown;

    /// <summary>Index of a built-in (bank 0) instrument by name.</summary>
    public static int FindBuiltIn(string name) =>
        Array.FindIndex(Banks[0], r => r[0].GetString() == name) is var i and >= 0
            ? i : throw new KeyNotFoundException($"Built-in instrument '{name}' not found");

    public static string BankLabel(int bank) => bank switch
    {
        0 => "Built-in",
        1 or 2 => "User sample",
        >= 2007 and <= 2009 => "Preinstalled",
        >= 300 and < 400 => $"Kit pack {bank}",
        _ => $"Expansion {bank}",
    };
}
