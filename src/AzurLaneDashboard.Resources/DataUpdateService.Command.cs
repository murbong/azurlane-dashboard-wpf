using System.Collections;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using AzurLaneDashboard.Core.Configuration;
using AzurLaneDashboard.Core.Services;
using AzurLaneDashboard.Data.Services;

namespace AzurLaneDashboard.Resources;

public sealed partial class DataUpdateService
{
    private static List<string> BuildNativeCommand(UpdateOptions options)
    {
        var command = new List<string>
        {
            "native-update",
            "--region",
            string.IsNullOrWhiteSpace(options.Region) ? AppDefaults.DefaultRegion : options.Region.ToUpperInvariant()
        };
        AddOption(command, options.AdbPath, "--adb");
        AddOption(command, options.MuMuManagerPath, "--mumu-manager");
        AddFlag(command, options.SkipGit, "--skip-git");
        AddFlag(command, options.SkipRegal, "--skip-regal");
        AddFlag(command, options.SkipGameResources, "--skip-game-resources");
        AddFlag(command, options.RequireGameResources, "--require-game-resources");
        AddFlag(command, options.MetadataOnly, "--metadata-only");
        AddFlag(command, options.SkipScripts, "--skip-scripts");
        AddFlag(command, options.ForceGameResources, "--force-game-resources");
        AddFlag(command, options.SkipIconExtract, "--skip-icon-extract");
        AddFlag(command, options.ForceIconExtract, "--force-icon-extract");
        return command;
    }

    private static async Task SyncLuaRepoAsync(UpdateContext context, IList<string> logs,
        CancellationToken cancellationToken)
    {
        if (!CommandExists("git"))
        {
            throw new InvalidOperationException("git was not found on PATH");
        }

        if (!Directory.Exists(context.LuaRepoDirectory))
        {
            Report(context.Progress, "LuaScripts", "LuaScripts 저장소 clone 중", null, null, true);
            Directory.CreateDirectory(Path.GetDirectoryName(context.LuaRepoDirectory) ?? context.ProjectRoot);
            await RunProcessAsync(
                "git",
                ["clone", "--filter=blob:none", "--branch", context.LuaBranch, LuaRepoUrl, context.LuaRepoDirectory],
                context.ProjectRoot,
                logs,
                cancellationToken).ConfigureAwait(false);
            return;
        }

        if (!Directory.Exists(Path.Combine(context.LuaRepoDirectory, ".git")))
        {
            throw new InvalidOperationException($"{context.LuaRepoDirectory} exists but is not a git checkout");
        }

        Report(context.Progress, "LuaScripts", "LuaScripts fetch 중", null, null, true);
        await RunProcessAsync(
            "git",
            ["fetch", "--prune", "origin"],
            context.LuaRepoDirectory,
            logs,
            cancellationToken).ConfigureAwait(false);
        Report(context.Progress, "LuaScripts", "LuaScripts pull 중", null, null, true);
        await RunProcessAsync(
            "git",
            ["pull", "--ff-only", "origin", context.LuaBranch],
            context.LuaRepoDirectory,
            logs,
            cancellationToken).ConfigureAwait(false);
    }

    private static void WriteUpdateState(UpdateContext context, IList<string> logs)
    {
        var regionDirectory = Path.Combine(context.GameResourcesDirectory, context.Region);
        var luaVersion = ReadText(Path.Combine(context.LuaRepoDirectory, "versions", $"{context.Region}.txt"));
        var gameVersion = ReadText(Path.Combine(regionDirectory, "version.txt"));
        var state = new Dictionary<string, object?>
        {
            ["updated_at"] = DateTimeOffset.Now.ToString("yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture),
            ["region"] = context.Region,
            ["mode"] = "native-csharp",
            ["lua_scripts_version"] = luaVersion,
            ["game_resource_version"] = gameVersion,
            ["maps_dir"] = context.MapsDirectory,
            ["static_assets_dir"] = context.StaticAssetsDirectory,
            ["local_asset_manifest"] = context.LocalAssetManifest,
            ["sources"] = new Dictionary<string, object?>
            {
                ["metadata"] = LuaRepoUrl,
                ["community"] = "https://azur-lane-regal.suchiguma.com/",
                ["game_resources"] = regionDirectory
            }
        };
        if (!string.IsNullOrWhiteSpace(luaVersion) &&
            !string.IsNullOrWhiteSpace(gameVersion) &&
            !luaVersion.Equals(gameVersion, StringComparison.OrdinalIgnoreCase))
        {
            state["version_warning"] = $"LuaScripts {luaVersion} != game resources {gameVersion}";
            logs.Add($"WARNING: {state["version_warning"]}");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(context.UpdateStatePath) ?? context.ProjectRoot);
        File.WriteAllText(context.UpdateStatePath, JsonSerializer.Serialize(state, JsonOptions));
        logs.Add($"update state: {context.UpdateStatePath}");
    }

    private static bool CommandExists(string fileName)
    {
        var extensions = OperatingSystem.IsWindows()
            ? (Environment.GetEnvironmentVariable("PATHEXT") ?? ".COM;.EXE;.BAT;.CMD").Split(';',
                StringSplitOptions.RemoveEmptyEntries)
            : [""];
        if (Path.GetExtension(fileName).Length > 0 && File.Exists(fileName))
        {
            return true;
        }

        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(directory))
            {
                continue;
            }

            foreach (var extension in extensions)
            {
                var candidate = Path.Combine(directory.Trim(), fileName + extension);
                if (File.Exists(candidate))
                {
                    return true;
                }
            }
        }

        return false;
    }
}