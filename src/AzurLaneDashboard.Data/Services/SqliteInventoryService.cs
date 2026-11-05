using AzurLaneDashboard.Core.Models;

namespace AzurLaneDashboard.Data.Services;

public sealed class SqliteInventoryService
{
    private readonly NativeSnapshotService nativeSnapshotService = new();

    public DashboardSnapshot Load(string databasePath, CancellationToken cancellationToken = default)
    {
        return nativeSnapshotService.Load(databasePath, cancellationToken);
    }
}
