namespace AzurLaneDashboard.Core.Configuration;

public static class AppDefaults
{
    public const string DefaultRegion = "KR";
    public const string DefaultAdbSerial = "127.0.0.1:16384";
    public const string DefaultDataDirectory = "azurlane_tcp_export";
    public const string DefaultDatabaseFileName = "inventory.sqlite";
    public const string DefaultMapsDirectory = "maps";
    public const string BundledDefaultsDirectory = "defaults";
    public const string BundledDefaultMapsDirectory = "defaults/maps";
    public const string DefaultLuaRepoDirectory = "external/AzurLaneLuaScripts";
    public const string DefaultLuaBranch = "main";
    public const string DefaultGameResourcesDirectory = "game_resources";
    public const string DefaultStaticAssetsDirectory = "static/game-assets";
    public const string DefaultLocalAssetManifest = "maps/local_assets.json";
    public const string DefaultUpdateStatePath = "maps/update_state.json";
    public const string SettingsFileName = "settings.json";

    public const string CaptureBackendDumpcap = "dumpcap";
    public const string CaptureBackendAdb = "adb";
    public const string CaptureBackendNpcap = "npcap";
    public const string DefaultAdbExecutable = "adb";
    public const string DefaultAdbInterface = "any";
    public const string DefaultDumpcapExecutable = "dumpcap";
    public const string DefaultCaptureInterface = "1";
    public const int DefaultServerPort = 80;
    public const int DefaultCaptureStatusInterval = 10;
    public const int DefaultProgressEvery = 200;

    public const string GameAssetsPrefix = "static/game-assets/";
    public const string GameAssetsUrlPrefix = "/static/game-assets/";

    public static IReadOnlyList<string> DefaultAssetCategories { get; } =
    [
        "squareicon",
        "shipyardicon",
        "equips",
        "skillicon",
        "spweapon",
        "props"
    ];

    /// <summary>The default before props (resource and item icons) joined; settings saved with it follow the new one.</summary>
    public static IReadOnlyList<string> PreviousDefaultAssetCategories { get; } =
    [
        "squareicon",
        "shipyardicon",
        "equips",
        "skillicon",
        "spweapon"
    ];

    public static string PackageNameForRegion(string region)
    {
        return region.ToUpperInvariant() switch
        {
            "KR" => "kr.txwy.and.blhx",
            "EN" => "com.YoStarEN.AzurLane",
            "JP" => "com.YoStarJP.AzurLane",
            "CN" => "com.bilibili.azurlane",
            _ => "kr.txwy.and.blhx"
        };
    }
}
