using AzurLaneDashboard.Core.Configuration;

namespace AzurLaneDashboard.Core.Models;

public sealed class AppSettings
{
    public static IReadOnlyList<string> DefaultCategories => AppDefaults.DefaultAssetCategories;

    public string DatabasePath { get; init; } = "";
    public IReadOnlyList<ShipGrowthGoal> GrowthGoals { get; init; } = [];
    public bool IsDarkMode { get; init; } = true;
    public bool IsAutoRefreshEnabled { get; init; } = true;
    /// <summary>Write live_state.json next to the database for macro tools.</summary>
    public bool IsLiveStateExportEnabled { get; init; }
    /// <summary>The ALAuto folder (macros, assets, config.json) the macro page runs from.</summary>
    public string AlautoRoot { get; init; } = "";
    /// <summary>Windows notification when a macro run stops by itself (not when stopped from the page or with F8).</summary>
    public bool NotifyMacroEnd { get; init; } = true;
    /// <summary>Windows notification when the game's 연속 작전 ends or stalls, read from captured packets.</summary>
    public bool NotifySortieEnd { get; init; } = true;
    public string RarityFilter { get; init; } = "전체";
    public string DockShipSortOption { get; init; } = "Lv";
    public string LevelShipSortOption { get; init; } = "우선순위";
    public string FleetDamageSortOption { get; init; } = "함대";
    public bool DockShipSortAscending { get; init; }
    public bool LevelShipSortAscending { get; init; }
    public bool FleetDamageSortAscending { get; init; } = true;
    public string ShipTypeFilter { get; init; } = "전체";
    public string NationFilter { get; init; } = "전체";
    public string ShipExtraFilter { get; init; } = "제한 없음";
    public string EquipmentSortOption { get; init; } = "레어도";
    public bool EquipmentSortAscending { get; init; }
    public string EquipmentTypeFilter { get; init; } = "전체";
    public string EquipmentExtraFilter { get; init; } = "제한 없음";
    public int SelectedMainTabIndex { get; init; }
    /// <summary>Manual driver count for the leveling planner; null follows the automatic rule.</summary>
    public int? LevelingDriverCount { get; init; }
    public string LevelingStageKey { get; init; } = "";
    /// <summary>META boss chosen in the planner; 0 follows the rotation.</summary>
    public int MetaBossTemplateId { get; init; }
    /// <summary>"move" also takes gear off ships outside the fleet; "bag" uses warehouse spares only.</summary>
    public string GearSourceMode { get; init; } = "move";
    public string Region { get; init; } = AppDefaults.DefaultRegion;
    public string Serial { get; init; } = AppDefaults.DefaultAdbSerial;
    /// <summary>
    /// The emulators the adb backend captures at once, each with its own account. Empty means just
    /// <see cref="Serial"/>, as before several emulators could be captured.
    /// </summary>
    public IReadOnlyList<string> CaptureSerials { get; init; } = [];
    public string AdbPath { get; init; } = AppDefaults.DefaultAdbExecutable;
    public string MuMuManagerPath { get; init; } = "";
    public string DataDirectory { get; init; } = AppDefaults.DefaultDataDirectory;
    public string CaptureBackend { get; init; } = AppDefaults.CaptureBackendDumpcap;
    public string CaptureInterface { get; init; } = AppDefaults.DefaultCaptureInterface;
    public string CaptureDumpcapPath { get; init; } = AppDefaults.DefaultDumpcapExecutable;
    public int CaptureServerPort { get; init; } = AppDefaults.DefaultServerPort;
    public string CaptureServerHost { get; init; } = "";
    public int CaptureStatusInterval { get; init; } = AppDefaults.DefaultCaptureStatusInterval;
    public bool CaptureVerbose { get; init; }
    public int GuildDockCapacityBonus { get; init; }
    public string LuaRepoDirectory { get; init; } = AppDefaults.DefaultLuaRepoDirectory;
    public string LuaBranch { get; init; } = AppDefaults.DefaultLuaBranch;
    public string MapsDirectory { get; init; } = AppDefaults.DefaultMapsDirectory;
    public string GameResourcesDirectory { get; init; } = AppDefaults.DefaultGameResourcesDirectory;
    public string StaticAssetsDirectory { get; init; } = AppDefaults.DefaultStaticAssetsDirectory;
    public string LocalAssetManifest { get; init; } = AppDefaults.DefaultLocalAssetManifest;
    public string UpdateStatePath { get; init; } = AppDefaults.DefaultUpdateStatePath;
    public IReadOnlyList<string> Categories { get; init; } = DefaultCategories;
    public int ProgressEvery { get; init; } = AppDefaults.DefaultProgressEvery;
    public bool SkipGit { get; init; }
    public bool SkipRegal { get; init; }
    public bool SkipGameResources { get; init; }
    public bool RequireGameResources { get; init; }
    public bool MetadataOnly { get; init; }
    public bool SkipScripts { get; init; }
    public bool ForceGameResources { get; init; }
    public bool SkipIconExtract { get; init; }
    public bool ForceIconExtract { get; init; }
}
