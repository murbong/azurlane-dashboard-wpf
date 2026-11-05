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
    private static async Task PullInstalledObbFilesAsync(
        UpdateContext context,
        AndroidBridge bridge,
        string packageName,
        IList<string> logs,
        CancellationToken cancellationToken)
    {
        var rawDirectory = Path.Combine(context.GameResourcesDirectory, "raw");
        Directory.CreateDirectory(rawDirectory);

        var remoteObbDirectory = $"/sdcard/Android/obb/{packageName}";
        var result = await RunAndroidBridgeCaptureAsync(
            context,
            bridge,
            ["shell", $"ls -1 {ShellQuote(remoteObbDirectory)}/*.obb 2>/dev/null || true"],
            logs,
            false,
            cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0)
        {
            logs.Add($"OBB listing failed: {FirstNonEmpty(result.StandardError.Trim(), result.StandardOutput.Trim())}");
            return;
        }

        var remoteFiles = SplitLines(result.StandardOutput)
            .Where(path => path.EndsWith(".obb", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (remoteFiles.Length == 0)
        {
            logs.Add($"no installed OBB found under {remoteObbDirectory}");
            return;
        }

        var pulled = 0;
        var skipped = 0;
        foreach (var remotePath in remoteFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var fileName = PosixFileName(remotePath);
            if (string.IsNullOrWhiteSpace(fileName) ||
                !fileName.EndsWith(".obb", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var localPath = Path.Combine(rawDirectory, fileName);
            if (!context.ForceGameResources && File.Exists(localPath) && new FileInfo(localPath).Length > 0)
            {
                skipped++;
                continue;
            }

            Report(context.Progress, "Game resources", $"OBB pull: {fileName}", null, null, true);
            if (await AdbPullFileAsync(context, bridge, remotePath, localPath, logs, cancellationToken)
                    .ConfigureAwait(false))
            {
                pulled++;
            }
        }

        logs.Add(
            $"OBB cache: pulled={pulled.ToString(CultureInfo.InvariantCulture)} skipped={skipped.ToString(CultureInfo.InvariantCulture)} -> {rawDirectory}");
    }

    private static void ExtractObbMetadataIfNeeded(
        UpdateContext context,
        string regionDirectory,
        string packageName,
        IList<string> logs,
        CancellationToken cancellationToken)
    {
        var archives = FindObbArchives(context.GameResourcesDirectory, packageName);
        if (archives.Count == 0)
        {
            return;
        }

        var copied = 0;
        foreach (var archivePath in archives)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                using var stream = File.OpenRead(archivePath);
                using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
                foreach (var entry in archive.Entries)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var name = entry.FullName.Replace('\\', '/');
                    if (!name.StartsWith("assets/", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var fileName = name["assets/".Length..];
                    if (fileName.Contains('/', StringComparison.Ordinal) ||
                        !IsMetadataFile(fileName))
                    {
                        continue;
                    }

                    var target = Path.Combine(regionDirectory, fileName);
                    if (File.Exists(target))
                    {
                        continue;
                    }

                    Directory.CreateDirectory(regionDirectory);
                    entry.ExtractToFile(target, overwrite: false);
                    copied++;
                }
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
            {
                logs.Add($"OBB metadata read failed: {archivePath}: {ex.Message}");
            }
        }

        if (copied > 0)
        {
            logs.Add($"OBB metadata extracted: {copied.ToString(CultureInfo.InvariantCulture)} files");
        }
    }

    private static ObbUnpackResult UnpackSelectedObbAssets(
        UpdateContext context,
        string packageName,
        string assetDirectory,
        IReadOnlyList<HashRow> rows,
        IList<string> logs,
        CancellationToken cancellationToken)
    {
        var archives = FindObbArchives(context.GameResourcesDirectory, packageName);
        if (archives.Count == 0)
        {
            return new ObbUnpackResult(0, 0, 0, 0);
        }

        Directory.CreateDirectory(assetDirectory);
        var pending = new Dictionary<string, HashRow>(StringComparer.OrdinalIgnoreCase);
        var skipped = 0;
        foreach (var row in rows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!ValidAssetPath(row.FilePath))
            {
                continue;
            }

            var localPath = Path.Combine(assetDirectory, PathFromPosix(row.FilePath));
            if (!context.ForceGameResources && LocalAssetIsCurrent(localPath, row))
            {
                skipped++;
                continue;
            }

            pending.TryAdd(row.FilePath, row);
        }

        if (pending.Count == 0)
        {
            return new ObbUnpackResult(archives.Count, 0, skipped, 0);
        }

        var copied = 0;
        foreach (var archivePath in archives)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                using var stream = File.OpenRead(archivePath);
                using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
                foreach (var assetPath in pending.Keys.ToArray())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var entry = FindAssetEntry(archive, assetPath);
                    if (entry is null)
                    {
                        continue;
                    }

                    var localPath = Path.Combine(assetDirectory, PathFromPosix(assetPath));
                    CopyZipEntryToFile(entry, localPath);
                    copied++;
                    pending.Remove(assetPath);
                }
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
            {
                logs.Add($"OBB asset read failed: {archivePath}: {ex.Message}");
            }

            if (pending.Count == 0)
            {
                break;
            }
        }

        return new ObbUnpackResult(archives.Count, copied, skipped, pending.Count);
    }

    private static IReadOnlyList<string> FindObbArchives(string gameResourcesDirectory, string packageName)
    {
        var rawDirectory = Path.Combine(gameResourcesDirectory, "raw");
        if (!Directory.Exists(rawDirectory))
        {
            return [];
        }

        var files = Directory.EnumerateFiles(rawDirectory, "*.obb", SearchOption.TopDirectoryOnly)
            .ToArray();
        var matching = files
            .Where(path => Path.GetFileName(path).Contains(packageName, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (matching.Length > 0)
        {
            files = matching;
        }

        return files
            .OrderBy(ObbSortRank)
            .ThenBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static int ObbSortRank(string path)
    {
        var name = Path.GetFileName(path);
        if (name.StartsWith("main.", StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        return name.StartsWith("patch.", StringComparison.OrdinalIgnoreCase) ? 1 : 2;
    }

    private static ZipArchiveEntry? FindAssetEntry(ZipArchive archive, string assetPath)
    {
        var normalized = assetPath.Replace('\\', '/').TrimStart('/');
        var entry = archive.GetEntry($"assets/AssetBundles/{normalized}");
        if (entry is not null)
        {
            return entry;
        }

        var fileName = PosixFileName(normalized);
        return fileName.Contains('.', StringComparison.Ordinal)
            ? null
            : archive.GetEntry($"assets/AssetBundles/{normalized}.ys");
    }

    private static void CopyZipEntryToFile(ZipArchiveEntry entry, string targetPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(targetPath) ?? Directory.GetCurrentDirectory());
        var tempPath = targetPath + ".tmp";
        using (var source = entry.Open())
        using (var target = File.Create(tempPath))
        {
            source.CopyTo(target);
        }

        if (File.Exists(targetPath))
        {
            File.Delete(targetPath);
        }

        File.Move(tempPath, targetPath);
    }
}