using System.Text.Json;

namespace AzurLaneDashboard.Data.Services;

/// <summary>
/// Reads the dashboard's JSON maps straight from the maps directory.
/// A parsed document is cached per file and reused until the file's size or write time changes,
/// so the maps are parsed once per update instead of being copied into every database.
/// </summary>
public static class MapFileStore
{
    public static IReadOnlyList<string> DashboardMapFiles { get; } =
    [
        "ships.json",
        "skins.json",
        "equipment.json",
        "fire_rate_equipment.json",
        "equipment_skins.json",
        "spweapons.json",
        "resources.json",
        "skills.json",
        "ship_level.json",
        "stages.json",
        "fleet_tech.json",
        "fleet_tech_nation.json",
        "ship_blueprints.json",
        "ship_transforms.json",
        "regal_ships.json",
        "regal_equips.json",
        "regal_gear_map.json",
        "regal_augments.json",
        "regal_changelog.json",
        "meta_bosses.json",
        "meta_boss_guides.json",
        "commanders.json",
        "meowfficer_guide.json",
        "world_targets.json",
        "personal_tasks.json",
        "month_shop.json",
        "oil_costs.json",
        "commissions.json",
        "equipment_formulas.json",
        "ship_upgrades.json",
        "auction_collection.json",
        "dashboard_settings.json"
    ];

    private static readonly object CacheLock = new();
    private static readonly Dictionary<string, CachedMap> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static JsonElement Read(string directory, string fileName)
    {
        var info = new FileInfo(Path.Combine(directory, fileName));
        if (!info.Exists)
        {
            return default;
        }

        lock (CacheLock)
        {
            if (Cache.TryGetValue(info.FullName, out var cached) &&
                cached.WriteTicks == info.LastWriteTimeUtc.Ticks &&
                cached.Length == info.Length)
            {
                return cached.Json;
            }
        }

        JsonElement root;
        try
        {
            using var stream = new FileStream(info.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var document = JsonDocument.Parse(stream);
            root = document.RootElement.Clone();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // An update may be rewriting the file; keep serving the last good parse until it settles.
            lock (CacheLock)
            {
                return Cache.TryGetValue(info.FullName, out var previous) ? previous.Json : default;
            }
        }

        lock (CacheLock)
        {
            Cache[info.FullName] = new CachedMap(info.LastWriteTimeUtc.Ticks, info.Length, root);
        }

        return root;
    }

    /// <summary>Changes whenever any dashboard map is added, removed or rewritten.</summary>
    public static string Signature(string directory)
    {
        return string.Join("|", DashboardMapFiles.Select(fileName =>
        {
            var info = new FileInfo(Path.Combine(directory, fileName));
            return info.Exists ? $"{info.Length}:{info.LastWriteTimeUtc.Ticks}" : "-";
        }));
    }

    private sealed record CachedMap(long WriteTicks, long Length, JsonElement Json);
}
