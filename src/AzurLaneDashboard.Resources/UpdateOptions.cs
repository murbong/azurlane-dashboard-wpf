using AzurLaneDashboard.Core.Configuration;

namespace AzurLaneDashboard.Resources;

public sealed class UpdateOptions
{
    public string ProjectRoot { get; init; } = "";
    public string DatabasePath { get; init; } = "";
    public string Region { get; init; } = AppDefaults.DefaultRegion;
    public string Serial { get; init; } = AppDefaults.DefaultAdbSerial;
    public string AdbPath { get; init; } = AppDefaults.DefaultAdbExecutable;
    public string MuMuManagerPath { get; init; } = "";
    public string DataDirectory { get; init; } = AppDefaults.DefaultDataDirectory;
    public string MapsDirectory { get; init; } = AppDefaults.DefaultMapsDirectory;
    public string LuaRepoDirectory { get; init; } = AppDefaults.DefaultLuaRepoDirectory;
    public string LuaBranch { get; init; } = AppDefaults.DefaultLuaBranch;
    public string GameResourcesDirectory { get; init; } = AppDefaults.DefaultGameResourcesDirectory;
    public string StaticAssetsDirectory { get; init; } = AppDefaults.DefaultStaticAssetsDirectory;
    public string LocalAssetManifest { get; init; } = AppDefaults.DefaultLocalAssetManifest;
    public string UpdateStatePath { get; init; } = AppDefaults.DefaultUpdateStatePath;
    public IReadOnlyList<string> Categories { get; init; } = [];
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
    public IProgress<UpdateProgress>? Progress { get; init; }
}

public sealed record UpdateProgress(
    string Stage,
    string Message,
    int? Current = null,
    int? Total = null,
    double? Percent = null,
    bool IsIndeterminate = false,
    bool AppendLog = false);

public sealed record UpdateResult(
    bool Success,
    DateTimeOffset StartedAt,
    DateTimeOffset FinishedAt,
    int ExitCode,
    IReadOnlyList<string> Command,
    string Error,
    IReadOnlyList<string> LogLines);