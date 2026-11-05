namespace AzurLaneDashboard.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    public int DockUsed
    {
        get => dockUsed;
        private set => SetProperty(ref dockUsed, value);
    }

    public int DockMax
    {
        get => dockMax;
        private set => SetProperty(ref dockMax, value);
    }

    public int DockExemptCount
    {
        get => dockExemptCount;
        private set => SetProperty(ref dockExemptCount, value);
    }

    public int EquipmentBagCount
    {
        get => equipmentBagCount;
        private set => SetProperty(ref equipmentBagCount, value);
    }

    public int EquipmentBagMax
    {
        get => equipmentBagMax;
        private set => SetProperty(ref equipmentBagMax, value);
    }

    public int EquipmentEquippedCount
    {
        get => equipmentEquippedCount;
        private set => SetProperty(ref equipmentEquippedCount, value);
    }

    public string DockUsageText => DockMax > 0
        ? $"도크 {DockUsed:N0}/{DockMax:N0} · 면제 {DockExemptCount:N0}"
        : $"도크 {ShipCount:N0}척";

    public string EquipmentUsageText => EquipmentBagMax > 0
        ? $"장비 {FilteredEquipmentCount:N0}/{EquipmentCount:N0}종 · 창고 {EquipmentBagCount:N0}/{EquipmentBagMax:N0} · 장착 {EquipmentEquippedCount:N0} · 총 {(EquipmentBagCount + EquipmentEquippedCount):N0}"
        : $"장비 {FilteredEquipmentCount:N0}/{EquipmentCount:N0}종";

    public string FleetDamageUsageText =>
        FleetCompositionGroups.Count > 0
            ? $"편성 {FleetCompositionGroups.Count:N0}개 · 함선 {FleetCompositionGroups.Sum(group => group.ShipCount):N0}척 · 타이밍 {FilteredFleetDamageCount:N0}/{FleetDamageRows.Count:N0}개"
            : "편성 0개 · 게임에서 편성 정보를 불러오면 표시됩니다";

    public string BattleSummaryText
    {
        get => battleSummaryText;
        private set => SetProperty(ref battleSummaryText, value);
    }

    public string BattleSubText
    {
        get => battleSubText;
        private set => SetProperty(ref battleSubText, value);
    }

    public string FleetTechSummaryText
    {
        get => fleetTechSummaryText;
        private set => SetProperty(ref fleetTechSummaryText, value);
    }

    public string CommunitySummaryText
    {
        get => communitySummaryText;
        private set => SetProperty(ref communitySummaryText, value);
    }

    public string LastLoadedAt
    {
        get => lastLoadedAt;
        private set => SetProperty(ref lastLoadedAt, value);
    }

    public string DatabaseLastWriteAt
    {
        get => databaseLastWriteAt;
        private set => SetProperty(ref databaseLastWriteAt, value);
    }

    public bool IsReloading
    {
        get => isReloading;
        private set
        {
            if (SetProperty(ref isReloading, value))
            {
                ReloadCommand.RaiseCanExecuteChanged();
                ExportWorkbookCommand.RaiseCanExecuteChanged();
                CompactDatabaseCommand.RaiseCanExecuteChanged();
                DeepCompactDatabaseCommand.RaiseCanExecuteChanged();
                CleanupRetiredStorageCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool IsCompactingDatabase
    {
        get => isCompactingDatabase;
        private set
        {
            if (SetProperty(ref isCompactingDatabase, value))
            {
                CompactDatabaseCommand.RaiseCanExecuteChanged();
                DeepCompactDatabaseCommand.RaiseCanExecuteChanged();
                CleanupRetiredStorageCommand.RaiseCanExecuteChanged();
                RunUpdateCommand.RaiseCanExecuteChanged();
                ReloadCommand.RaiseCanExecuteChanged();
                ExportWorkbookCommand.RaiseCanExecuteChanged();
                OnPropertyChanged(nameof(DatabaseMaintenanceButtonText));
                OnPropertyChanged(nameof(DeepDatabaseMaintenanceButtonText));
                OnPropertyChanged(nameof(RetiredStorageButtonText));
            }
        }
    }

    public string DatabaseMaintenanceText
    {
        get => databaseMaintenanceText;
        private set => SetProperty(ref databaseMaintenanceText, value);
    }

    public string DatabaseMaintenanceButtonText => IsCompactingDatabase ? "정리 중..." : "빠른 DB 정리";

    public string DeepDatabaseMaintenanceButtonText => IsCompactingDatabase ? "정리 중..." : "깊은 DB 정리";

    public string RetiredStorageText
    {
        get => retiredStorageText;
        private set => SetProperty(ref retiredStorageText, value);
    }

    public string RetiredStorageButtonText => IsCompactingDatabase ? "정리 중..." : "백업 후 정리";

    public string RetiredStorageBlockedText => IsCaptureRunning ? "실시간 캡처를 끈 뒤에 실행할 수 있습니다." : "";

    public int ShipCount => Ships.Count;
    public int EquipmentCount => Equipment.Count;
    public int FilteredShipCount => filteredShipCount;
    public int FilteredEquipmentCount => filteredEquipmentCount;
    public int FilteredFleetDamageCount => filteredFleetDamageCount;
    public bool HasFleetCompositionGroups => FleetCompositionGroups.Count > 0;
}
