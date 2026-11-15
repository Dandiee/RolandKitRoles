using System.IO;
using System.Text.Json;

namespace KitRoles.Model;

/// <summary>Stores data next to the exe (data/...) so the app stays portable.</summary>
public static class KitCache
{
    private static readonly string Root = Path.Combine(AppContext.BaseDirectory, "data");
    private static readonly string KitsPath = Path.Combine(Root, "kits.json");
    private static readonly string BackupRoot = Path.Combine(Root, "backups");
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public static Dictionary<int, KitInfo> LoadKits() => Read<Dictionary<int, KitInfo>>(KitsPath) ?? [];

    public static void SaveKits(Dictionary<int, KitInfo> kits) => Write(KitsPath, kits);

    /// <summary>Blocks backed up right before an apply: data/backups/&lt;session&gt;/kitNNN.json.</summary>
    public static string NewBackupSession() => DateTime.Now.ToString("yyyyMMdd-HHmmss");

    public static void SaveBackup(string session, KitSnapshot s) =>
        Write(Path.Combine(BackupRoot, session, $"kit{s.Kit:000}.json"), s);

    /// <summary>Backups of the most recent apply session, or empty.</summary>
    public static (string? Session, List<KitSnapshot> Kits) LoadLastBackup()
    {
        if (!Directory.Exists(BackupRoot)) return (null, []);
        var last = Directory.GetDirectories(BackupRoot).OrderBy(d => d).LastOrDefault();
        if (last is null) return (null, []);
        var kits = Directory.GetFiles(last, "kit*.json").Select(Read<KitSnapshot>).OfType<KitSnapshot>().ToList();
        return (Path.GetFileName(last), kits);
    }

    public static void DeleteBackup(string session) => Directory.Delete(Path.Combine(BackupRoot, session), true);

    private static T? Read<T>(string path)
    {
        if (!File.Exists(path)) return default;
        try { return JsonSerializer.Deserialize<T>(File.ReadAllText(path), Json); }
        catch (JsonException) { return default; }
    }

    private static void Write<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(value, Json));
    }
}
