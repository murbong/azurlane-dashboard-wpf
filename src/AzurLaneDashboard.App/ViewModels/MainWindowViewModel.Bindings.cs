using System.Collections.ObjectModel;
using System.ComponentModel;
using AzurLaneDashboard.Core.Models;

namespace AzurLaneDashboard.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    public sealed record CaptureInterfaceOption(string Id, string Name)
    {
        public string DisplayName => string.IsNullOrWhiteSpace(Name) ? $"{Id} (수동)" : $"{Id} - {Name}";
    }

    public ObservableCollection<DashboardKpiRow> Kpis { get; }
    public ObservableCollection<ShipRow> Ships { get; }
    public ObservableCollection<EquipmentRow> Equipment { get; }
    public ObservableCollection<EquipmentRow> EquipmentRecommendations { get; }
    public ObservableCollection<SpecialEquipmentRow> SpecialEquipment { get; }
    public ObservableCollection<EquipmentSkinRow> EquipmentSkins { get; }
    public ObservableCollection<LevelPriorityRow> LevelPriorities { get; }
    public ObservableCollection<GearGapRow> GearGaps { get; }
    public ObservableCollection<BattleRunRow> BattleRuns { get; }
    public ObservableCollection<BattleRunRow> OverviewBattleRuns { get; }
    public ObservableCollection<FleetDamageRow> FleetDamageRows { get; }
    public ObservableCollection<FleetCompositionGroupRow> FleetCompositionGroups { get; }
    public ObservableCollection<SkillPriorityRow> SkillPriorities { get; }
    public ObservableCollection<SystemStatusRow> SystemStatuses { get; }
    public ObservableCollection<AcademyRoomRow> AcademyRooms { get; }
    public ObservableCollection<ResearchQueueRow> ResearchQueue { get; }
    public ObservableCollection<CommissionRow> Commissions { get; }
    public ObservableCollection<BuildQueueRow> Builds { get; }
    public ObservableCollection<ResourceRow> Resources { get; }
    public ObservableCollection<CommunityGearShortageRow> CommunityGearShortages { get; }
    public ObservableCollection<CleanupCandidateRow> CleanupCandidates { get; }
    public ObservableCollection<PacketStatRow> PacketStats { get; }
    public ObservableCollection<PacketStatRow> PacketInterpretationGaps { get; }
    public ObservableCollection<DecodeFailureRow> DecodeFailures { get; }
    public ObservableCollection<UpdateStatusViewRow> UpdateStatusRows { get; }
    public ObservableCollection<CaptureStatusViewRow> CaptureStatusRows { get; }
    public ObservableCollection<OperationLogLine> UpdateLogLines { get; }
    public ObservableCollection<OperationLogLine> CaptureLogLines { get; }
    public ObservableCollection<string> RarityFilters { get; }
    public ObservableCollection<string> ShipSortOptions { get; }
    public ObservableCollection<string> DockShipSortOptions { get; }
    public ObservableCollection<string> LevelShipSortOptions { get; }
    public ObservableCollection<string> FleetDamageSortOptions { get; }
    public ObservableCollection<string> ShipTypeFilters { get; }
    public ObservableCollection<string> NationFilters { get; }
    public ObservableCollection<string> ShipExtraFilters { get; }
    public ObservableCollection<string> EquipmentSortOptions { get; }
    public ObservableCollection<string> EquipmentTypeFilters { get; }
    public ObservableCollection<string> EquipmentExtraFilters { get; }
    public ObservableCollection<string> CaptureBackendOptions { get; }
    public ObservableCollection<CaptureInterfaceOption> CaptureInterfaceOptions { get; }
    public ICollectionView ShipsView { get; }
    public ICollectionView GearGapsView { get; }
    public ICollectionView FleetDamageView { get; }
    public ICollectionView EquipmentView { get; }
    public ICollectionView EquipmentRecommendationsView { get; }
    public ICollectionView SpecialEquipmentView { get; }
    public ICollectionView EquipmentSkinsView { get; }
    public ICollectionView CommunityGearShortagesView { get; }
    public RelayCommand ReloadCommand { get; }
    public RelayCommand BrowseDatabaseCommand { get; }
    public RelayCommand ExportWorkbookCommand { get; }
    public RelayCommand ToggleThemeCommand { get; }
    public RelayCommand ToggleAutoRefreshCommand { get; }
    public RelayCommand ToggleShipSortDirectionCommand { get; }
    public RelayCommand ToggleEquipmentSortDirectionCommand { get; }
    public RelayCommand SaveSettingsCommand { get; }
    public RelayCommand RunUpdateCommand { get; }
    public RelayCommand BrowseAdbPathCommand { get; }
    public RelayCommand BrowseMuMuManagerPathCommand { get; }
    public RelayCommand AutoDetectAndroidToolsCommand { get; }
    public RelayCommand RefreshCaptureInterfacesCommand { get; }
    public RelayCommand StartCaptureCommand { get; }
    public RelayCommand StopCaptureCommand { get; }
    public RelayCommand ToggleCaptureCommand { get; }
    public RelayCommand CompactDatabaseCommand { get; }
    public RelayCommand DeepCompactDatabaseCommand { get; }
    public RelayCommand CleanupRetiredStorageCommand { get; }
    public RelayCommand OpenShipDetailCommand { get; }
    public RelayCommand CloseShipDetailCommand { get; }
}
