using System.Globalization;
using AzurLaneDashboard.Core.Configuration;
using AzurLaneDashboard.Core.Services;

namespace AzurLaneDashboard.Resources;

public sealed partial class DataUpdateService
{
    private static async Task PullLiveResourcesAsync(
        UpdateContext context,
        IList<string> logs,
        CancellationToken cancellationToken)
    {
        if (context.SkipGameResources)
        {
            Report(context.Progress, "게임 리소스", "게임 리소스 동기화를 건너뜀", null, null, true);
            logs.Add("live game resource sync skipped by option");
            return;
        }

        var bridge = ExternalToolLocator.ResolveAndroidBridge(
            context.AdbPath,
            context.MuMuManagerPath,
            context.ProjectRoot);
        if (!bridge.IsAvailable)
        {
            var message =
                "ADB 또는 MuMuManager를 찾지 못해 live game resource sync를 건너뜁니다. 기존 game_resources가 있으면 그 캐시는 계속 사용됩니다.";
            if (context.RequireGameResources)
            {
                throw new InvalidOperationException(message);
            }

            Report(context.Progress, "게임 리소스", message, null, null, true);
            logs.Add(message);
            return;
        }

        logs.Add($"android bridge: {bridge.DisplayName}");
        await EnsureAndroidBridgeConnectedAsync(context, bridge, logs, cancellationToken).ConfigureAwait(false);
        Report(context.Progress, "게임 리소스", $"{bridge.DisplayName} 연결 상태 확인 중", null, null, true);
        var state = await RunAndroidBridgeCaptureAsync(
            context,
            bridge,
            ["get-state"],
            logs,
            false,
            cancellationToken).ConfigureAwait(false);
        if (state.ExitCode != 0 || !state.StandardOutput.Trim().Equals("device", StringComparison.OrdinalIgnoreCase))
        {
            var detail = FirstNonEmpty(state.StandardError.Trim(), state.StandardOutput.Trim(), "no output");
            var message = $"Android bridge not ready; skipping live game resource sync ({detail})";
            if (context.RequireGameResources)
            {
                throw new InvalidOperationException(message);
            }

            Report(context.Progress, "게임 리소스", message, null, null, true);
            logs.Add(message);
            return;
        }

        var regionDirectory = Path.Combine(context.GameResourcesDirectory, context.Region);
        var assetDirectory = Path.Combine(regionDirectory, "AssetBundles");
        Directory.CreateDirectory(regionDirectory);
        Directory.CreateDirectory(assetDirectory);
        var packageName = AppDefaults.PackageNameForRegion(context.Region);
        var remoteRoot = $"/sdcard/Android/data/{packageName}/files";
        Report(context.Progress, "게임 리소스", "원격 리소스 메타데이터 목록 조회 중", null, null, true);
        var listing = await RunAndroidBridgeCaptureAsync(
            context,
            bridge,
            ["shell", $"cd {remoteRoot} && ls -1 version*.txt hashes*.csv 2>/dev/null || true"],
            logs,
            false,
            cancellationToken).ConfigureAwait(false);

        var metadataFiles = listing.StandardOutput
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(IsMetadataFile)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var pulledMetadata = 0;
        foreach (var fileName in metadataFiles)
        {
            Report(context.Progress, "게임 리소스", $"메타데이터 pull: {fileName}", null, null, true);
            if (await AdbPullFileAsync(
                    context,
                    bridge,
                    $"{remoteRoot}/{fileName}",
                    Path.Combine(regionDirectory, fileName),
                    logs,
                    cancellationToken).ConfigureAwait(false))
            {
                pulledMetadata++;
            }
        }

        logs.Add($"metadata: {pulledMetadata.ToString(CultureInfo.InvariantCulture)} files -> {regionDirectory}");
        await PullInstalledObbFilesAsync(context, bridge, packageName, logs, cancellationToken)
            .ConfigureAwait(false);
        ExtractObbMetadataIfNeeded(context, regionDirectory, packageName, logs, cancellationToken);
        var versionFile = Path.Combine(regionDirectory, "version.txt");
        if (File.Exists(versionFile))
        {
            logs.Add($"live version: {ReadText(versionFile)}");
        }

        if (context.MetadataOnly)
        {
            Report(context.Progress, "게임 리소스", "메타데이터만 동기화하고 AssetBundle pull은 건너뜀", null, null, true);
            return;
        }

        var rows = ParseHashRowsFromDirectory(regionDirectory, logs);
        var categories = new HashSet<string>(context.Categories.Select(item => item.ToLowerInvariant()),
            StringComparer.OrdinalIgnoreCase);
        var selectedByPath = rows
            .Where(row => ShouldPullHashRow(row, categories, context.SkipScripts))
            .ToDictionary(row => row.FilePath, row => row, StringComparer.OrdinalIgnoreCase);
        var discoveredRows = await DiscoverLiveAssetRowsAsync(
            context,
            bridge,
            remoteRoot,
            AssetDiscoveryCategories(categories),
            logs,
            cancellationToken).ConfigureAwait(false);
        foreach (var row in discoveredRows.Where(row => ShouldPullHashRow(row, categories, context.SkipScripts)))
        {
            selectedByPath.TryAdd(row.FilePath, row);
        }

        foreach (var row in DiscoverObbAssetRows(context.GameResourcesDirectory, packageName, categories, logs)
                     .Where(row => ShouldPullHashRow(row, categories, context.SkipScripts)))
        {
            selectedByPath.TryAdd(row.FilePath, row);
        }

        var remoteAvailablePaths = discoveredRows
            .Select(row => row.FilePath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var selectedRows = selectedByPath.Values
            .OrderBy(row => row.FilePath, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (selectedRows.Count == 0)
        {
            Report(context.Progress, "게임 리소스", "선택된 AssetBundle 항목 없음", null, null, true);
            logs.Add("no asset rows selected from hashes.csv");
            return;
        }

        Report(context.Progress, "게임 리소스", $"AssetBundle pull 준비: {selectedRows.Count:N0}개", 0,
            selectedRows.Count);
        var obbResult = UnpackSelectedObbAssets(
            context,
            packageName,
            assetDirectory,
            selectedRows,
            logs,
            cancellationToken);
        if (obbResult.Archives > 0)
        {
            logs.Add(
                $"OBB assets: archives={obbResult.Archives.ToString(CultureInfo.InvariantCulture)} copied={obbResult.Copied.ToString(CultureInfo.InvariantCulture)} skipped={obbResult.Skipped.ToString(CultureInfo.InvariantCulture)} missing={obbResult.Missing.ToString(CultureInfo.InvariantCulture)}");
        }

        var pulled = 0;
        var skipped = 0;
        var failed = 0;
        for (var index = 0; index < selectedRows.Count; index++)
        {
            var row = selectedRows[index];
            var localPath = Path.Combine(assetDirectory, PathFromPosix(row.FilePath));
            if (!context.ForceGameResources && LocalAssetIsCurrent(localPath, row))
            {
                skipped++;
            }
            else if (!context.ForceGameResources &&
                     File.Exists(localPath) &&
                     !remoteAvailablePaths.Contains(row.FilePath) &&
                     !IsScriptAsset(row))
            {
                skipped++;
            }
            else if ((remoteAvailablePaths.Contains(row.FilePath) || IsScriptAsset(row)) &&
                     await AdbPullFileAsync(
                         context,
                         bridge,
                         $"{remoteRoot}/AssetBundles/{row.FilePath}",
                         localPath,
                         logs,
                         cancellationToken).ConfigureAwait(false))
            {
                pulled++;
            }
            else
            {
                if (File.Exists(localPath))
                {
                    skipped++;
                }
                else
                {
                    failed++;
                }
            }

            var current = index + 1;
            if (ShouldReportProgress(current, selectedRows.Count, context.ProgressEvery))
            {
                Report(context.Progress, "게임 리소스",
                    $"AssetBundle 동기화 {current:N0}/{selectedRows.Count:N0} · pull {pulled:N0}, skip {skipped:N0}, fail {failed:N0}",
                    current, selectedRows.Count);
                logs.Add(
                    $"[{current.ToString(CultureInfo.InvariantCulture)}/{selectedRows.Count.ToString(CultureInfo.InvariantCulture)}] pulled={pulled.ToString(CultureInfo.InvariantCulture)} skipped={skipped.ToString(CultureInfo.InvariantCulture)} failed={failed.ToString(CultureInfo.InvariantCulture)}");
            }
        }

        WriteLivePullManifest(regionDirectory, selectedRows);
        logs.Add(
            $"live assets: selected={selectedRows.Count.ToString(CultureInfo.InvariantCulture)} pulled={pulled.ToString(CultureInfo.InvariantCulture)} skipped={skipped.ToString(CultureInfo.InvariantCulture)} failed={failed.ToString(CultureInfo.InvariantCulture)} -> {assetDirectory}");
        if (failed > 0 && context.RequireGameResources)
        {
            throw new InvalidOperationException(
                $"live resource sync failed for {failed.ToString(CultureInfo.InvariantCulture)} files");
        }
    }
}
