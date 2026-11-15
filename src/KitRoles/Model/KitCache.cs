using System.IO;
using System.Text.Json;

namespace KitRoles.Model;

/// <summary>Header + category of a kit as last seen, so startup only probes kits that changed.</summary>
public record IndexEntry(string Header, KitCategory Category);

/// <summary>Stores data next to the exe (data/...) so the app stays portable.</summary>
public static class KitCache
{
    private static readonly string Root = Path.Combine(AppContext.BaseDirectory, "data");
    private static readonly string KitFolder = Path.Combine(Root, "kits");
    private static readonly string IndexPath = Path.Combine(Root, "index.json");
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private static string PathFor(int kit) => Path.Combine(KitFolder, $"kit{kit:000}.json");

    public static KitSnapshot? Load(int kit) => Read<KitSnapshot>(PathFor(kit));

    public static void Save(KitSnapshot snapshot) => Write(PathFor(snapshot.Kit), snapshot);

    /// <summary>Backups taken right before an apply: data/backups/&lt;session&gt;/kitNNN.json.</summary>
    private static readonly string BackupRoot = Path.Combine(Root, "backups");

    public static string NewBackupSession() => DateTime.Now.ToString("yyyyMMdd-HHmmss");

    public static void SaveBackup(string session, KitSnapshot s) =>
        Write(Path.Combine(BackupRoot, session, $"kit{s.Kit:000}.json"), s);

    /// <summary>Kit snapshots of the most recent apply session, or empty.</summary>
    public static (string? Session, List<KitSnapshot> Kits) LoadLastBackup()
    {
        if (!Directory.Exists(BackupRoot)) return (null, []);
        var last = Directory.GetDirectories(BackupRoot).OrderBy(d => d).LastOrDefault();
        if (last is null) return (null, []);
        var kits = Directory.GetFiles(last, "kit*.json").Select(Read<KitSnapshot>).OfType<KitSnapshot>().ToList();
        return (Path.GetFileName(last), kits);
    }

    public static void DeleteBackup(string session) => Directory.Delete(Path.Combine(BackupRoot, session), true);

    public static Dictionary<int, IndexEntry> LoadIndex() => Read<Dictionary<int, IndexEntry>>(IndexPath) ?? [];

    public static void SaveIndex(Dictionary<int, IndexEntry> index) => Write(IndexPath, index);

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
