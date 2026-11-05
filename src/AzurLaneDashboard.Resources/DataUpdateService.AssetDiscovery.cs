using System.Globalization;
using System.IO.Compression;
using System.Text;
using AzurLaneDashboard.Core.Services;

namespace AzurLaneDashboard.Resources;

public sealed partial class DataUpdateService
{
    private static bool IsMetadataFile(string fileName)
    {
        return fileName.StartsWith("version", StringComparison.OrdinalIgnoreCase) &&
               fileName.EndsWith(".txt", StringComparison.OrdinalIgnoreCase) ||
               fileName.StartsWith("hashes", StringComparison.OrdinalIgnoreCase) &&
               fileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<HashRow> ParseHashRows(string text)
    {
        var rows = new List<HashRow>();
        foreach (var rawLine in text.Split(['\r', '\n'],
                     StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = rawLine.Split(',');
            if (parts.Length < 3 ||
                !long.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var size))
            {
                continue;
            }

            rows.Add(new HashRow(parts[0].Trim(), size, parts[2].Trim()));
        }

        return rows;
    }

    private static IReadOnlyList<HashRow> ParseHashRowsFromDirectory(string regionDirectory, IList<string> logs)
    {
        var hashFile = Path.Combine(regionDirectory, "hashes.csv");
        if (!File.Exists(hashFile))
        {
            logs.Add($"hash file not found: {hashFile}");
            return [];
        }

        try
        {
            var rows = ParseHashRows(File.ReadAllText(hashFile));
            logs.Add($"hash rows loaded: file=hashes.csv rows={rows.Count.ToString(CultureInfo.InvariantCulture)}");
            return rows;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logs.Add($"hash file read failed: {hashFile}: {ex.Message}");
            return [];
        }
    }

    private static IReadOnlyList<string> NormalizeAssetCategories(IReadOnlyList<string> categories)
    {
        var normalized = categories
            .Where(category => !string.IsNullOrWhiteSpace(category))
            .Select(category => category.Trim().ToLowerInvariant())
            .Where(category => !category.Equals("painting", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return normalized.Length > 0 ? normalized : DefaultCategories;
    }

    private static ISet<string> AssetDiscoveryCategories(ISet<string> categories)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var category in categories)
        {
            if (IsSafeAssetCategory(category))
            {
                result.Add(category);
            }
        }

        return result;
    }

    private static async Task<IReadOnlyList<HashRow>> DiscoverLiveAssetRowsAsync(
        UpdateContext context,
        AndroidBridge bridge,
        string remoteRoot,
        ISet<string> categories,
        IList<string> logs,
        CancellationToken cancellationToken)
    {
        var safeCategories = categories
            .Where(IsSafeAssetCategory)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (safeCategories.Length == 0)
        {
            return [];
        }

        var script = new StringBuilder();
        script.Append("cd ").Append(ShellQuote($"{remoteRoot}/AssetBundles")).Append(" 2>/dev/null || exit 0; ");
        foreach (var category in safeCategories)
        {
            script.Append("if [ -d ").Append(ShellQuote(category)).Append(" ]; then find ")
                .Append(ShellQuote(category)).Append(" -type f 2>/dev/null; fi; ");
        }

        var result = await RunAndroidBridgeCaptureAsync(
            context,
            bridge,
            ["shell", script.ToString()],
            logs,
            false,
            cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0)
        {
            logs.Add(
                $"remote AssetBundles listing failed: {FirstNonEmpty(result.StandardError.Trim(), result.StandardOutput.Trim(), result.ExitCode.ToString(CultureInfo.InvariantCulture))}");
            return [];
        }

        var rows = SplitLines(result.StandardOutput)
            .Select(NormalizeRemoteAssetPath)
            .Where(ValidAssetPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(path => new HashRow(path, -1, ""))
            .OrderBy(row => row.FilePath, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        logs.Add($"remote AssetBundles discovered: {rows.Length.ToString(CultureInfo.InvariantCulture)} files");
        return rows;
    }

    private static IReadOnlyList<HashRow> DiscoverObbAssetRows(
        string gameResourcesDirectory,
        string packageName,
        ISet<string> categories,
        IList<string> logs)
    {
        var safeCategories = categories
            .Where(IsSafeAssetCategory)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (safeCategories.Count == 0)
        {
            return [];
        }

        var archives = FindObbArchives(gameResourcesDirectory, packageName);
        if (archives.Count == 0)
        {
            return [];
        }

        var rows = new Dictionary<string, HashRow>(StringComparer.OrdinalIgnoreCase);
        foreach (var archivePath in archives)
        {
            try
            {
                using var stream = File.OpenRead(archivePath);
                using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
                foreach (var entry in archive.Entries)
                {
                    if (entry.Length <= 0)
                    {
                        continue;
                    }

                    var filePath = AssetPathFromObbEntry(entry.FullName);
                    if (string.IsNullOrWhiteSpace(filePath) ||
                        !ValidAssetPath(filePath))
                    {
                        continue;
                    }

                    var firstSegment = filePath.Split('/', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
                    if (firstSegment is null || !safeCategories.Contains(firstSegment))
                    {
                        continue;
                    }

                    rows[filePath] = new HashRow(filePath, entry.Length, "");
                }
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
            {
                logs.Add($"OBB discovery failed: {archivePath}: {ex.Message}");
            }
        }

        if (rows.Count > 0)
        {
            logs.Add($"OBB AssetBundles discovered: {rows.Count.ToString(CultureInfo.InvariantCulture)} files");
        }

        return rows.Values
            .OrderBy(row => row.FilePath, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string AssetPathFromObbEntry(string entryName)
    {
        var normalized = entryName.Trim().Replace('\\', '/');
        const string prefix = "assets/AssetBundles/";
        if (!normalized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return "";
        }

        var filePath = normalized[prefix.Length..];
        return filePath.EndsWith(".ys", StringComparison.OrdinalIgnoreCase)
            ? filePath[..^3]
            : filePath;
    }
}
