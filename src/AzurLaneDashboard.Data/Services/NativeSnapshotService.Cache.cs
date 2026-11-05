using System.Text.Json;
using AzurLaneDashboard.Core.Models;

namespace AzurLaneDashboard.Data.Services;

public sealed partial class NativeSnapshotService
{
    private const string SnapshotCacheVersion = "current-fleet-composition-v10-skill-fire-rate";
    private static readonly object SnapshotCacheLock = new();
    private static readonly Dictionary<string, CachedSnapshot> SnapshotCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object ShipStatsCacheLock = new();
    private static readonly Dictionary<string, CachedShipStats> ShipStatsCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object EquipmentAttributesCacheLock = new();
    private static readonly Dictionary<string, CachedEquipmentAttributes> EquipmentAttributesCache =
        new(StringComparer.OrdinalIgnoreCase);

    private static bool TryGetCachedSnapshot(
        string databasePath,
        string signature,
        out DashboardSnapshot snapshot)
    {
        var cacheSignature = SnapshotCacheSignature(signature);
        lock (SnapshotCacheLock)
        {
            if (SnapshotCache.TryGetValue(databasePath, out var cached) && cached.Signature == cacheSignature)
            {
                if (cached.Snapshot.TryGetTarget(out var target))
                {
                    if (IsCompleteCachedSnapshot(target))
                    {
                        snapshot = target;
                        return true;
                    }

                    SnapshotCache.Remove(databasePath);
                    snapshot = default!;
                    return false;
                }

                SnapshotCache.Remove(databasePath);
            }
        }

        snapshot = default!;
        return false;
    }

    private static void CacheSnapshot(string databasePath, string signature, DashboardSnapshot snapshot)
    {
        lock (SnapshotCacheLock)
        {
            SnapshotCache[databasePath] = new CachedSnapshot(
                SnapshotCacheSignature(signature),
                new WeakReference<DashboardSnapshot>(snapshot));
        }
    }

    private static bool IsCompleteCachedSnapshot(DashboardSnapshot snapshot)
    {
        return snapshot.FleetDamageRows.Count == 0 || snapshot.FleetCompositionGroups.Count > 0;
    }

    private static string SnapshotCacheSignature(string databaseSignature)
    {
        return $"{SnapshotCacheVersion}:{databaseSignature}";
    }

    private sealed record CachedSnapshot(string Signature, WeakReference<DashboardSnapshot> Snapshot);

    private sealed record CachedShipStats(string Signature, IReadOnlyDictionary<int, ShipStatBlock> Stats);

    private sealed record CachedEquipmentAttributes(
        string Signature,
        IReadOnlyDictionary<int, IReadOnlyList<EquipmentAttributeRow>> Attributes);
}
