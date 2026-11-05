using System.Windows.Threading;
using AzurLaneDashboard.Capture;
using AzurLaneDashboard.Core.Configuration;
using AzurLaneDashboard.Core.Models;
using AzurLaneDashboard.Data.Services;
using AzurLaneDashboard.Resources;

namespace AzurLaneDashboard.App.ViewModels;

public sealed partial class MainWindowViewModel : ObservableObject, IDisposable
{
    public GrowthPlannerViewModel GrowthPlanner { get; }
    public FleetPlannerViewModel FleetPlanner { get; }
    public MacroViewModel Macros { get; }
    private readonly SqliteInventoryService inventoryService = new();
    private readonly AppSettingsService settingsService;
    private readonly DatabaseMaintenanceService databaseMaintenanceService = new();
    private readonly DataUpdateService dataUpdateService = new();
    private readonly Dictionary<string, ShipRow> shipsByUid = new(StringComparer.Ordinal);
    private readonly DispatcherTimer autoRefreshTimer;
    private readonly DispatcherTimer operationStatusTimer;
    private readonly DispatcherTimer updateLogFlushTimer;
    private readonly DispatcherTimer searchRefreshTimer;
    private readonly DispatcherTimer settingsSaveTimer;
    private readonly List<OperationLogLine> pendingUpdateLogLines = [];
    private CancellationTokenSource? reloadCancellation;
    private CancellationTokenSource? updateCancellation;
    private bool isDisposed;
    private string databasePath = "";
    private string query = "";
    private string selectedRarityFilter = "전체";
    private string selectedDockShipSortOption = "Lv";
    private string selectedLevelShipSortOption = "우선순위";
    private string selectedFleetDamageSortOption = "함대";
    private string selectedShipTypeFilter = "전체";
    private string selectedNationFilter = "전체";
    private string selectedShipExtraFilter = "제한 없음";
    private bool isDockShipSortAscending;
    private bool isLevelShipSortAscending;
    private bool isFleetDamageSortAscending = true;
    private string selectedEquipmentSortOption = "레어도";
    private string selectedEquipmentTypeFilter = "전체";
    private string selectedEquipmentExtraFilter = "제한 없음";
    private bool isEquipmentSortAscending;
    private int selectedMainTabIndex;
    private int selectedLevelSectionIndex;
    private int selectedEquipmentSectionIndex;
    private string status = "준비";
    private string snapshotSource = "";
    private string snapshotWarning = "";
    private string currentStateMode = "";
    private int currentStateLastDecodedRowId;
    private string currentStateLastStatus = "";
    private string currentStateLastArea = "";
    private string currentStateLastDetail = "";
    private string currentStateLastError = "";
    private int packetsSeen;
    private int knownPacketsSeen;
    private int knownPacketsDecoded;
    private int packetPayloadsSaved;
    private int decodeFailuresSaved;
    private string liveUpdatedAt = "";
    private string generatedAt = "";
    private string captureStatusLabel = "";
    private string captureStatusDetail = "";
    private string captureBackend = AppDefaults.CaptureBackendDumpcap;
    private string captureInterface = AppDefaults.DefaultCaptureInterface;
    private bool isRefreshingCaptureInterfaces;
    private string captureInterfaceStatus = "인터페이스 목록 미확인";
    private string captureDumpcapPath = AppDefaults.DefaultDumpcapExecutable;
    private int captureServerPort = AppDefaults.DefaultServerPort;
    private string captureServerHost = "";
    private int captureStatusInterval = AppDefaults.DefaultCaptureStatusInterval;
    private bool captureVerbose;
    private string region = AppDefaults.DefaultRegion;
    private string serial = AppDefaults.DefaultAdbSerial;
    private string adbPath = AppDefaults.DefaultAdbExecutable;
    private string mumuManagerPath = "";
    private string androidToolStatus = "도구 확인 전";
    private string dataDirectory = AppDefaults.DefaultDataDirectory;
    private int guildDockCapacityBonus;
    private string luaRepoDirectory = AppDefaults.DefaultLuaRepoDirectory;
    private string luaBranch = AppDefaults.DefaultLuaBranch;
    private string mapsDirectory = AppDefaults.DefaultMapsDirectory;
    private string gameResourcesDirectory = AppDefaults.DefaultGameResourcesDirectory;
    private string staticAssetsDirectory = AppDefaults.DefaultStaticAssetsDirectory;
    private string localAssetManifest = AppDefaults.DefaultLocalAssetManifest;
    private string updateStatePath = AppDefaults.DefaultUpdateStatePath;
    private string categoriesText = string.Join(Environment.NewLine, AppSettings.DefaultCategories);
    private int progressEvery = AppDefaults.DefaultProgressEvery;
    private bool skipGit;
    private bool skipRegal;
    private bool skipGameResources;
    private bool requireGameResources;
    private bool metadataOnly;
    private bool skipScripts;
    private bool forceGameResources;
    private bool skipIconExtract;
    private bool forceIconExtract;
    private bool isCaptureRunning;
    private bool isUpdating;
    private string updateStateLabel = "대기";
    private string updateProgressStage = "대기";
    private string updateProgressMessage = "업데이트 대기";
    private double updateProgressValue;
    private bool updateProgressIsIndeterminate;
    private string captureStateLabel = "OFF";
    private string captureLogKey = "";
    private bool initialUpdatePromptShown;
    private int dockUsed;
    private int dockMax;
    private int dockExemptCount;
    private int equipmentBagCount;
    private int equipmentBagMax;
    private int equipmentEquippedCount;
    private string battleSummaryText = "";
    private string battleSubText = "";
    private string fleetTechSummaryText = "";
    private string communitySummaryText = "";
    private bool isDarkMode = true;
    private bool isAutoRefreshEnabled = true;
    private bool isReloading;
    private bool isCompactingDatabase;
    private string lastLoadedAt = "-";
    private string databaseLastWriteAt = "-";
    private string retiredStorageText =
        "앱이 더 이상 쓰지 않는 테이블·열·인덱스를 지웁니다. 먼저 DB 전체를 같은 폴더에 백업하고, 백업을 검사한 뒤에만 지웁니다.";
    private string databaseMaintenanceText = "원본 패킷 payload가 커지면 빠른 DB 정리를 실행하세요. 파일 크기 축소는 별도 깊은 정리가 필요합니다.";
    private DatabaseChangeStamp lastObservedDatabaseChangeStamp;
    private int filteredShipCount;
    private int filteredEquipmentCount;
    private int filteredFleetDamageCount;
    private int filteredEquipmentRecommendationCount;
    private int filteredSpecialEquipmentCount;
    private int filteredEquipmentSkinCount;
    private int filteredCommunityGearShortageCount;
    private string appliedQuery = "";
    private ShipRow? selectedShip;
    private EquipmentRow? selectedEquipment;
    private GearGapRow? selectedGearGap;
    private FleetCompositionGroupRow? selectedFleetCompositionGroup;
    private bool isShipDetailOpen;

    public string Query
    {
        get => query;
        set
        {
            if (SetProperty(ref query, value ?? string.Empty))
            {
                ScheduleSearchRefresh();
            }
        }
    }

    public string SelectedRarityFilter
    {
        get => selectedRarityFilter;
        set
        {
            if (SetProperty(ref selectedRarityFilter, value))
            {
                RefreshShipViews();
                EquipmentView.Refresh();
                EquipmentRecommendationsView.Refresh();
                SpecialEquipmentView.Refresh();
                EquipmentSkinsView.Refresh();
                CommunityGearShortagesView.Refresh();
                NotifyCounts();
                SaveSettings();
            }
        }
    }

    public string SelectedShipSortOption
    {
        get => IsLevelTabActive
            ? selectedLevelShipSortOption
            : IsFleetDamageTabActive
                ? selectedFleetDamageSortOption
                : selectedDockShipSortOption;
        set
        {
            if (IsLevelTabActive)
            {
                value = NormalizeShipSortOption(value);
                if (value == "기술")
                {
                    value = "120 기술";
                }

                if (!SetProperty(ref selectedLevelShipSortOption, value))
                {
                    return;
                }

                OnPropertyChanged();
                ApplyShipSort(GearGapsView, ShipSortMode.Level);
            }
            else if (IsFleetDamageTabActive)
            {
                value = string.IsNullOrWhiteSpace(value) || !FleetDamageSortOptions.Contains(value)
                    ? "함대"
                    : value;
                if (!SetProperty(ref selectedFleetDamageSortOption,
                        value))
                {
                    return;
                }

                OnPropertyChanged();
                ApplyShipSort(FleetDamageView, ShipSortMode.FleetDamage);
            }
            else
            {
                value = NormalizeShipSortOption(value);
                if (!SetProperty(ref selectedDockShipSortOption, value))
                {
                    return;
                }

                OnPropertyChanged();
                ApplyShipSort(ShipsView, ShipSortMode.Dock);
            }

            RefreshShipViews();
            NotifyCounts();
            SaveSettings();
        }
    }

    public string ShipSortDirectionLabel => ActiveShipSortAscending ? "↑" : "↓";

    public string ShipSortDirectionToolTip => ActiveShipSortAscending ? "오름차순 정렬" : "내림차순 정렬";

    private bool ActiveShipSortAscending => IsLevelTabActive
        ? isLevelShipSortAscending
        : IsFleetDamageTabActive
            ? isFleetDamageSortAscending
            : isDockShipSortAscending;

    public string SelectedEquipmentSortOption
    {
        get => selectedEquipmentSortOption;
        set
        {
            if (SetProperty(ref selectedEquipmentSortOption, string.IsNullOrWhiteSpace(value) ? "레어도" : value))
            {
                ApplyEquipmentSort();
                RefreshEquipmentViews();
                NotifyCounts();
                SaveSettings();
            }
        }
    }

    public string EquipmentSortDirectionLabel => isEquipmentSortAscending ? "↑" : "↓";

    public string EquipmentSortDirectionToolTip => isEquipmentSortAscending ? "오름차순 정렬" : "내림차순 정렬";

    public string SelectedEquipmentTypeFilter
    {
        get => selectedEquipmentTypeFilter;
        set
        {
            if (SetProperty(ref selectedEquipmentTypeFilter, string.IsNullOrWhiteSpace(value) ? "전체" : value))
            {
                RefreshEquipmentViews();
                NotifyCounts();
                SaveSettings();
            }
        }
    }

    public string SelectedEquipmentExtraFilter
    {
        get => selectedEquipmentExtraFilter;
        set
        {
            if (SetProperty(ref selectedEquipmentExtraFilter, string.IsNullOrWhiteSpace(value) ? "제한 없음" : value))
            {
                RefreshEquipmentViews();
                NotifyCounts();
                SaveSettings();
            }
        }
    }

    public int SelectedMainTabIndex
    {
        get => selectedMainTabIndex;
        set
        {
            if (SetProperty(ref selectedMainTabIndex, value))
            {
                RefreshToolbarSortOptions();
                OnPropertyChanged(nameof(IsLevelTabActive));
                OnPropertyChanged(nameof(IsEquipmentTabActive));
                OnPropertyChanged(nameof(IsFleetDamageTabActive));
                OnPropertyChanged(nameof(IsDockTabActive));
                OnPropertyChanged(nameof(IsShipFilterVisible));
                OnPropertyChanged(nameof(IsShipSortVisible));
                OnPropertyChanged(nameof(IsEquipmentFilterVisible));
                OnPropertyChanged(nameof(IsGrowthPlannerActive));
                OnPropertyChanged(nameof(IsInventorySearchVisible));
                if (value == MacroPageIndex) Macros.OnPageShown();
                OnPropertyChanged(nameof(IsToolbarSearchVisible));
                OnPropertyChanged(nameof(SelectedShipSortOption));
                OnPropertyChanged(nameof(ShipSortDirectionLabel));
                OnPropertyChanged(nameof(ShipSortDirectionToolTip));
                SaveSettings();
            }
        }
    }

    public bool IsLevelTabActive => SelectedMainTabIndex == 1;
    public bool IsEquipmentTabActive => SelectedMainTabIndex == 2;
    public bool IsFleetDamageTabActive => SelectedMainTabIndex == 3;
    public bool IsDockTabActive => SelectedMainTabIndex == 6;
    public int SelectedLevelSectionIndex
    {
        get => selectedLevelSectionIndex;
        set
        {
            if (!SetProperty(ref selectedLevelSectionIndex, value)) return;
            OnPropertyChanged(nameof(IsGrowthPlannerActive));
            OnPropertyChanged(nameof(IsInventorySearchVisible));
            OnPropertyChanged(nameof(IsShipFilterVisible));
            OnPropertyChanged(nameof(IsShipSortVisible));
        }
    }
    // 육성 계획 sections: 0 쩔 대기열, 1 내 육성 목표, 2 전투 기록, 3 장비 빈칸.
    public const int GrowthQueueSection = 0;
    public const int GrowthGoalsSection = 1;
    public bool IsGrowthPlannerActive => IsLevelTabActive && SelectedLevelSectionIndex == GrowthGoalsSection;
    public bool IsInventorySearchVisible => IsShipFilterVisible || IsEquipmentTabActive;
    // Pages with their own tab strip show search under the tabs (TabTools) so the strip never moves.
    public bool IsToolbarSearchVisible => IsInventorySearchVisible && !(IsLevelTabActive || IsEquipmentTabActive || IsDockTabActive);
    public bool IsShipFilterVisible => (IsLevelTabActive && SelectedLevelSectionIndex is GrowthQueueSection or 3) || IsDockTabActive || IsFleetDamageTabActive;
    // The queue keeps its own order, so it takes the filters but not the sort.
    public bool IsShipSortVisible => IsShipFilterVisible && !(IsLevelTabActive && SelectedLevelSectionIndex == GrowthQueueSection);
    public int SelectedEquipmentSectionIndex
    {
        get => selectedEquipmentSectionIndex;
        set
        {
            if (!SetProperty(ref selectedEquipmentSectionIndex, value)) return;
            OnPropertyChanged(nameof(IsEquipmentFilterVisible));
        }
    }
    public bool IsEquipmentFilterVisible => IsEquipmentTabActive && SelectedEquipmentSectionIndex is 0 or 1;

    public FleetCompositionGroupRow? SelectedFleetCompositionGroup
    {
        get => selectedFleetCompositionGroup;
        set => SetProperty(ref selectedFleetCompositionGroup, value);
    }

    public string SelectedShipTypeFilter
    {
        get => selectedShipTypeFilter;
        set
        {
            if (SetProperty(ref selectedShipTypeFilter, value))
            {
                RefreshShipViews();
                NotifyCounts();
                SaveSettings();
            }
        }
    }

    public string SelectedNationFilter
    {
        get => selectedNationFilter;
        set
        {
            if (SetProperty(ref selectedNationFilter, value))
            {
                RefreshShipViews();
                NotifyCounts();
                SaveSettings();
            }
        }
    }

    public string SelectedShipExtraFilter
    {
        get => selectedShipExtraFilter;
        set
        {
            if (SetProperty(ref selectedShipExtraFilter, value))
            {
                RefreshShipViews();
                NotifyCounts();
                SaveSettings();
            }
        }
    }

    private bool MatchesRarity(string rarity)
    {
        return string.IsNullOrWhiteSpace(SelectedRarityFilter)
               || SelectedRarityFilter == "전체"
               || RarityFilterTier(rarity) == SelectedRarityFilter;
    }
}
