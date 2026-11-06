using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Data;
using System.Windows.Threading;
using AzurLaneDashboard.App.Services;
using AzurLaneDashboard.Capture;
using AzurLaneDashboard.Core.Configuration;
using AzurLaneDashboard.Core.Models;
using AzurLaneDashboard.Core.Services;
using AzurLaneDashboard.Data.Services;

namespace AzurLaneDashboard.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    public MainWindowViewModel(AppSettingsService? settingsService = null, bool startBackgroundServices = true)
    {
        this.settingsService = settingsService ?? new AppSettingsService();
        backgroundServicesEnabled = startBackgroundServices;
        var settings = this.settingsService.Load();
        GrowthPlanner = new GrowthPlannerViewModel(settings.GrowthGoals, SaveSettings, ship => OpenShipDetail(ship));
        FleetPlanner = new FleetPlannerViewModel(GrowthPlanner, shipUid => OpenShipDetail(shipUid), settings, SaveSettings);
        Macros = new MacroViewModel(new MacroHost(
            IsMacroCaptureRunning,
            () => ExternalToolLocator.ResolveAdbPath(AdbPath, ProjectLocator.FindProjectRoot()),
            () => MacroSerial,
            () => FleetPlanner.LevelingShips,
            StartMacroCapture,
            SaveSettings), settings.AlautoRoot, settings.NotifyMacroEnd);
        FleetPlanner.SetQueueShipFilter(uid => !shipsByUid.TryGetValue(uid, out var ship) || MatchesShipRow(ship));
        isDarkMode = settings.IsDarkMode;
        isAutoRefreshEnabled = settings.IsAutoRefreshEnabled;
        isLiveStateExportEnabled = settings.IsLiveStateExportEnabled;
        notifySortieEnd = settings.NotifySortieEnd;
        region = string.IsNullOrWhiteSpace(settings.Region) ? AppDefaults.DefaultRegion : settings.Region;
        serial = string.IsNullOrWhiteSpace(settings.Serial) ? AppDefaults.DefaultAdbSerial : settings.Serial;
        adbPath = string.IsNullOrWhiteSpace(settings.AdbPath) ? AppDefaults.DefaultAdbExecutable : settings.AdbPath;
        mumuManagerPath = settings.MuMuManagerPath;
        dataDirectory = string.IsNullOrWhiteSpace(settings.DataDirectory)
            ? AppDefaults.DefaultDataDirectory
            : settings.DataDirectory;
        captureBackend = NormalizeCaptureBackend(settings.CaptureBackend);
        captureInterface = string.IsNullOrWhiteSpace(settings.CaptureInterface)
            ? AppDefaults.DefaultCaptureInterface
            : settings.CaptureInterface;
        captureDumpcapPath = string.IsNullOrWhiteSpace(settings.CaptureDumpcapPath)
            ? AppDefaults.DefaultDumpcapExecutable
            : settings.CaptureDumpcapPath;
        if (captureBackend == AppDefaults.CaptureBackendDumpcap &&
            !File.Exists(DumpcapCaptureService.ResolveDumpcapPath(captureDumpcapPath)) &&
            ExternalToolLocator.ResolveAdbPath(adbPath, ProjectLocator.FindProjectRoot()) is { } detectedAdb)
        {
            captureBackend = AppDefaults.CaptureBackendAdb;
            adbPath = detectedAdb;
        }
        captureServerPort =
            settings.CaptureServerPort <= 0 ? AppDefaults.DefaultServerPort : settings.CaptureServerPort;
        captureServerHost = settings.CaptureServerHost;
        captureStatusInterval = settings.CaptureStatusInterval <= 0
            ? AppDefaults.DefaultCaptureStatusInterval
            : settings.CaptureStatusInterval;
        captureVerbose = settings.CaptureVerbose;
        guildDockCapacityBonus = Math.Max(0, settings.GuildDockCapacityBonus);
        luaRepoDirectory = string.IsNullOrWhiteSpace(settings.LuaRepoDirectory)
            ? AppDefaults.DefaultLuaRepoDirectory
            : settings.LuaRepoDirectory;
        luaBranch = string.IsNullOrWhiteSpace(settings.LuaBranch) ? AppDefaults.DefaultLuaBranch : settings.LuaBranch;
        mapsDirectory = string.IsNullOrWhiteSpace(settings.MapsDirectory)
            ? AppDefaults.DefaultMapsDirectory
            : settings.MapsDirectory;
        gameResourcesDirectory = string.IsNullOrWhiteSpace(settings.GameResourcesDirectory)
            ? AppDefaults.DefaultGameResourcesDirectory
            : settings.GameResourcesDirectory;
        staticAssetsDirectory = string.IsNullOrWhiteSpace(settings.StaticAssetsDirectory)
            ? AppDefaults.DefaultStaticAssetsDirectory
            : settings.StaticAssetsDirectory;
        localAssetManifest = string.IsNullOrWhiteSpace(settings.LocalAssetManifest)
            ? AppDefaults.DefaultLocalAssetManifest
            : settings.LocalAssetManifest;
        updateStatePath = string.IsNullOrWhiteSpace(settings.UpdateStatePath)
            ? AppDefaults.DefaultUpdateStatePath
            : settings.UpdateStatePath;
        var categories = settings.Categories is { Count: > 0 } saved &&
                         !AppDefaults.PreviousDefaultAssetCategoryLists.Any(previous =>
                             saved.ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(previous))
            ? settings.Categories
            : AppSettings.DefaultCategories;
        categoriesText = string.Join(Environment.NewLine, NormalizeAssetCategories(categories));
        progressEvery = settings.ProgressEvery <= 0 ? AppDefaults.DefaultProgressEvery : settings.ProgressEvery;
        skipGit = settings.SkipGit;
        skipRegal = settings.SkipRegal;
        skipGameResources = settings.SkipGameResources;
        requireGameResources = settings.RequireGameResources;
        metadataOnly = settings.MetadataOnly;
        skipScripts = settings.SkipScripts;
        forceGameResources = settings.ForceGameResources;
        skipIconExtract = settings.SkipIconExtract;
        forceIconExtract = settings.ForceIconExtract;

        Kpis = new BulkObservableCollection<DashboardKpiRow>();
        Ships = new BulkObservableCollection<ShipRow>();
        Equipment = new BulkObservableCollection<EquipmentRow>();
        EquipmentRecommendations = new BulkObservableCollection<EquipmentRow>();
        SpecialEquipment = new BulkObservableCollection<SpecialEquipmentRow>();
        EquipmentSkins = new BulkObservableCollection<EquipmentSkinRow>();
        LevelPriorities = new BulkObservableCollection<LevelPriorityRow>();
        GearGaps = new BulkObservableCollection<GearGapRow>();
        BattleRuns = new BulkObservableCollection<BattleRunRow>();
        OverviewBattleRuns = new BulkObservableCollection<BattleRunRow>();
        FleetDamageRows = new BulkObservableCollection<FleetDamageRow>();
        FleetCompositionGroups = new BulkObservableCollection<FleetCompositionGroupRow>();
        SkillPriorities = new BulkObservableCollection<SkillPriorityRow>();
        SystemStatuses = new BulkObservableCollection<SystemStatusRow>();
        AcademyRooms = new BulkObservableCollection<AcademyRoomRow>();
        ResearchQueue = new BulkObservableCollection<ResearchQueueRow>();
        Commissions = new BulkObservableCollection<CommissionRow>();
        Builds = new BulkObservableCollection<BuildQueueRow>();
        Resources = new BulkObservableCollection<ResourceRow>();
        CommunityGearShortages = new BulkObservableCollection<CommunityGearShortageRow>();
        CleanupCandidates = new BulkObservableCollection<CleanupCandidateRow>();
        PacketStats = new BulkObservableCollection<PacketStatRow>();
        PacketInterpretationGaps = new BulkObservableCollection<PacketStatRow>();
        DecodeFailures = new BulkObservableCollection<DecodeFailureRow>();
        UpdateStatusRows = new BulkObservableCollection<UpdateStatusViewRow>();
        CaptureStatusRows = new BulkObservableCollection<CaptureStatusViewRow>();
        UpdateLogLines = new BulkObservableCollection<OperationLogLine>();
        CaptureLogLines = new BulkObservableCollection<OperationLogLine>();

        RarityFilters = ["전체", "N", "R", "SR", "SSR", "UR"];
        ShipSortOptions = new ObservableCollection<string>();
        DockShipSortOptions = ["Lv", "커뮤니티", "레어도", "함종", "진영", "기술", "기분", "입수시간", "이름"];
        LevelShipSortOptions =
            ["우선순위", "커뮤니티", "120 기술", "Lv", "120까지", "125까지", "EXP", "120 예상", "125 예상", "레어도", "기분", "입수시간", "이름"];
        FleetDamageSortOptions = ["함대", "쿨다운", "사속", "DPS", "장전", "함선", "슬롯"];
        ShipTypeFilters = ["전체", "전열", "주력", "잠수", "구축", "경순", "중순", "전함", "항모", "기타"];
        NationFilters = ["전체", "USS", "HMS", "IJN", "KMS", "ROC", "RN", "SN", "FFNF", "MNF", "META", "템페스타", "기타"];
        ShipExtraFilters = ["제한 없음", "커뮤니티 A+", "120 기술", "스킬작", "장비 빈칸", "잠금", "레벨 부족"];
        EquipmentSortOptions = ["레어도", "점수", "강화", "개수", "창고", "장착", "이름"];
        EquipmentTypeFilters = ["전체", "함포", "항공", "어뢰", "대공", "설비", "대잠", "미사일", "기타"];
        EquipmentExtraFilters = ["제한 없음", "강화 필요", "+10~12", "+13 완료", "복수 보유"];
        selectedDockShipSortOption = DockShipSortOptions.Contains(settings.DockShipSortOption)
            ? settings.DockShipSortOption
            : "Lv";
        selectedLevelShipSortOption = LevelShipSortOptions.Contains(settings.LevelShipSortOption)
            ? settings.LevelShipSortOption
            : "우선순위";
        selectedFleetDamageSortOption = FleetDamageSortOptions.Contains(settings.FleetDamageSortOption)
            ? settings.FleetDamageSortOption
            : "함대";
        isDockShipSortAscending = settings.DockShipSortAscending;
        isLevelShipSortAscending = settings.LevelShipSortAscending;
        isFleetDamageSortAscending = settings.FleetDamageSortAscending;
        selectedShipTypeFilter = ShipTypeFilters.Contains(settings.ShipTypeFilter) ? settings.ShipTypeFilter : "전체";
        selectedNationFilter = NationFilters.Contains(settings.NationFilter) ? settings.NationFilter : "전체";
        selectedShipExtraFilter = ShipExtraFilters.Contains(settings.ShipExtraFilter)
            ? settings.ShipExtraFilter
            : "제한 없음";
        selectedEquipmentSortOption = EquipmentSortOptions.Contains(settings.EquipmentSortOption)
            ? settings.EquipmentSortOption
            : "레어도";
        isEquipmentSortAscending = settings.EquipmentSortAscending;
        selectedEquipmentTypeFilter = EquipmentTypeFilters.Contains(settings.EquipmentTypeFilter)
            ? settings.EquipmentTypeFilter
            : "전체";
        selectedEquipmentExtraFilter = EquipmentExtraFilters.Contains(settings.EquipmentExtraFilter)
            ? settings.EquipmentExtraFilter
            : "제한 없음";
        selectedMainTabIndex = Math.Clamp(settings.SelectedMainTabIndex, 0, 9);
        CaptureBackendOptions =
        [
            AppDefaults.CaptureBackendDumpcap,
            AppDefaults.CaptureBackendNpcap,
            AppDefaults.CaptureBackendAdb
        ];
        CaptureInterfaceOptions = new ObservableCollection<CaptureInterfaceOption>();
        EnsureCaptureInterfaceOption();
        selectedRarityFilter = RarityFilters.Contains(settings.RarityFilter) ? settings.RarityFilter : "전체";
        ShipsView = CollectionViewSource.GetDefaultView(Ships);
        GearGapsView = CollectionViewSource.GetDefaultView(GearGaps);
        EquipmentView = CollectionViewSource.GetDefaultView(Equipment);
        EquipmentRecommendationsView = CollectionViewSource.GetDefaultView(EquipmentRecommendations);
        SpecialEquipmentView = CollectionViewSource.GetDefaultView(SpecialEquipment);
        EquipmentSkinsView = CollectionViewSource.GetDefaultView(EquipmentSkins);
        CommunityGearShortagesView = CollectionViewSource.GetDefaultView(CommunityGearShortages);
        FleetDamageView = CollectionViewSource.GetDefaultView(FleetDamageRows);
        ShipsView.Filter = FilterShip;
        GearGapsView.Filter = FilterGearGap;
        FleetDamageView.Filter = FilterFleetDamage;
        EquipmentView.Filter = FilterEquipment;
        EquipmentRecommendationsView.Filter = FilterEquipment;
        SpecialEquipmentView.Filter = FilterSpecialEquipment;
        EquipmentSkinsView.Filter = FilterEquipmentSkin;
        CommunityGearShortagesView.Filter = FilterCommunityGearShortage;
        ApplyShipSort();
        ApplyEquipmentSort();
        RefreshToolbarSortOptions();

        ReloadCommand = new RelayCommand(_unused => _ = ReloadAsync(), _ => !IsReloading && !IsCompactingDatabase);
        BrowseDatabaseCommand = new RelayCommand(_ => BrowseDatabase());
        ExportWorkbookCommand = new RelayCommand(_unused => _ = ExportWorkbookAsync(), _ => CanExportWorkbook());
        ToggleThemeCommand = new RelayCommand(_ => IsDarkMode = !IsDarkMode);
        ToggleAutoRefreshCommand = new RelayCommand(_ => IsAutoRefreshEnabled = !IsAutoRefreshEnabled);
        ToggleShipSortDirectionCommand = new RelayCommand(_ => ToggleShipSortDirection());
        ToggleEquipmentSortDirectionCommand = new RelayCommand(_ => ToggleEquipmentSortDirection());
        SaveSettingsCommand = new RelayCommand(_ => SaveSettingsNow());
        RunUpdateCommand = new RelayCommand(_unused => _ = RunUpdateAsync(), _ => !IsUpdating && !IsCompactingDatabase);
        BrowseAdbPathCommand = new RelayCommand(_ => BrowseAdbPath());
        BrowseMuMuManagerPathCommand = new RelayCommand(_ => BrowseMuMuManagerPath());
        AutoDetectAndroidToolsCommand = new RelayCommand(_ => AutoDetectAndroidTools());
        RefreshCaptureInterfacesCommand =
            new RelayCommand(_unused => _ = RefreshCaptureInterfacesAsync(), _ => !IsRefreshingCaptureInterfaces);
        StartCaptureCommand = new RelayCommand(_ => StartCapture(), _ => CaptureTargets.Any(target => !target.IsRunning));
        StopCaptureCommand = new RelayCommand(_ => StopCapture(), _ => IsCaptureRunning);
        ToggleCaptureCommand = new RelayCommand(_ => ToggleCapture());
        CompactDatabaseCommand =
            new RelayCommand(_unused => _ = CompactDatabaseAsync(),
                _ => !IsReloading && !IsCompactingDatabase && !IsUpdating);
        DeepCompactDatabaseCommand = new RelayCommand(_unused => _ = DeepCompactDatabaseAsync(),
            _ => !IsReloading && !IsCompactingDatabase && !IsCaptureRunning && !IsUpdating);
        CleanupRetiredStorageCommand = new RelayCommand(_unused => _ = CleanupRetiredStorageAsync(),
            _ => !IsReloading && !IsCompactingDatabase && !IsCaptureRunning && !IsUpdating);
        OpenShipDetailCommand = new RelayCommand(OpenShipDetail, parameter => SourceShip(parameter) is not null);
        CloseShipDetailCommand = new RelayCommand(_ => CloseShipDetail(), _ => IsShipDetailOpen);
        ThemeService.Apply(IsDarkMode);

        DatabasePath = string.IsNullOrWhiteSpace(settings.DatabasePath)
            ? ProjectLocator.DefaultDatabasePath()
            : settings.DatabasePath;
        InitializeCaptureTargets(settings.CaptureSerials);

        autoRefreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(2)
        };
        autoRefreshTimer.Tick += AutoRefreshTimerTick;
        InitializeLiveReload();
        InitializeSortieWatch();
        InitializeAccounts();
        operationStatusTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(2)
        };
        operationStatusTimer.Tick += OperationStatusTimerTick;
        if (startBackgroundServices) operationStatusTimer.Start();
        updateLogFlushTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(250)
        };
        updateLogFlushTimer.Tick += UpdateLogFlushTimerTick;
        searchRefreshTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(250)
        };
        searchRefreshTimer.Tick += SearchRefreshTimerTick;
        settingsSaveTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(500)
        };
        settingsSaveTimer.Tick += SettingsSaveTimerTick;
        RefreshOperationStatusRows();
        InitializeNavigation();
        if (startBackgroundServices)
        {
            RefreshAndroidToolStatus();
            _ = RefreshCaptureInterfacesAsync();
        }
    }
}
