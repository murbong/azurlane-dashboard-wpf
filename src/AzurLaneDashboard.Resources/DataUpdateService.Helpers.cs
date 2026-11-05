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
    private static void Report(
        IProgress<UpdateProgress>? progress,
        string stage,
        string message,
        int? current = null,
        int? total = null,
        bool isIndeterminate = false)
    {
        double? percent = null;
        if (current.HasValue && total is > 0)
        {
            percent = Math.Clamp(current.Value * 100.0 / total.Value, 0, 100);
        }

        progress?.Report(new UpdateProgress(stage, message, current, total, percent, isIndeterminate));
    }

    private static bool ShouldReportProgress(int current, int total, int progressEvery)
    {
        return current == 1 ||
               current == total ||
               progressEvery > 0 && current % progressEvery == 0;
    }

    private static string PosixFileName(string value)
    {
        var normalized = value.Trim().Replace('\\', '/').TrimEnd('/');
        var index = normalized.LastIndexOf('/');
        return index >= 0 ? normalized[(index + 1)..] : normalized;
    }

    private static void WriteLivePullManifest(string regionDirectory, IReadOnlyList<HashRow> rows)
    {
        var manifest = new
        {
            version = 1,
            updated_at = DateTimeOffset.Now.ToString("yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture),
            files = rows.Select(row => new
            {
                filepath = row.FilePath,
                size = row.Size,
                md5 = row.Md5Hash
            })
        };
        File.WriteAllText(Path.Combine(regionDirectory, LivePullManifestFileName),
            JsonSerializer.Serialize(manifest, JsonOptions));
    }

    private static string PathFromPosix(string value)
    {
        return Path.Combine(value.Split('/', StringSplitOptions.RemoveEmptyEntries));
    }

    private static string DataDirectoryFromDatabasePath(string projectRoot, string databasePath)
    {
        if (string.IsNullOrWhiteSpace(databasePath))
        {
            return Path.Combine(projectRoot, AppDefaults.DefaultDataDirectory);
        }

        var fullPath = Path.GetFullPath(databasePath);
        var directory = Path.GetDirectoryName(fullPath);
        return string.IsNullOrWhiteSpace(directory)
            ? Path.Combine(projectRoot, AppDefaults.DefaultDataDirectory)
            : directory;
    }

    private static string ShellQuote(string value)
    {
        return "'" + value.Replace("'", "'\\''", StringComparison.Ordinal) + "'";
    }

    private static string ResolveProjectPath(string projectRoot, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return projectRoot;
        }

        return Path.IsPathRooted(value) ? Path.GetFullPath(value) : Path.GetFullPath(Path.Combine(projectRoot, value));
    }

    private static bool HasUsableLocalAssetManifestEntries(
        string path,
        string staticAssetsDirectory,
        string assetDirectory,
        IReadOnlyCollection<string> categories,
        IList<string> logs)
    {
        try
        {
            if (!File.Exists(path) || new FileInfo(path).Length == 0)
            {
                return false;
            }

            using var stream = File.OpenRead(path);
            using var document = JsonDocument.Parse(stream);
            if (document.RootElement.ValueKind != JsonValueKind.Array ||
                document.RootElement.GetArrayLength() == 0)
            {
                return false;
            }

            staticAssetsDirectory = Path.GetFullPath(staticAssetsDirectory);
            var activeCategories = categories
                .Where(category => !string.IsNullOrWhiteSpace(category))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var total = 0;
            var existing = 0;
            var missing = 0;
            var manifestCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in document.RootElement.EnumerateArray())
            {
                if (entry.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var category = JsonText(entry, "category");
                if (activeCategories.Count > 0 &&
                    !activeCategories.Contains(category))
                {
                    continue;
                }

                var file = FirstNonEmpty(JsonText(entry, "file"), FileFromAssetUrl(JsonText(entry, "url")));
                if (string.IsNullOrWhiteSpace(file))
                {
                    continue;
                }

                manifestCounts[category] = manifestCounts.GetValueOrDefault(category) + 1;
                total++;
                var localPath = Path.Combine(staticAssetsDirectory, file.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(localPath))
                {
                    existing++;
                }
                else
                {
                    missing++;
                }
            }

            if (total == 0)
            {
                logs.Add($"local asset manifest has no file entries: {path}");
                return false;
            }

            if (missing > 0)
            {
                logs.Add(
                    $"local asset manifest exists but static PNGs are incomplete: existing={existing.ToString(CultureInfo.InvariantCulture)} missing={missing.ToString(CultureInfo.InvariantCulture)} under {staticAssetsDirectory}; extraction will rebuild");
                return false;
            }

            foreach (var category in activeCategories)
            {
                var sourceDirectory = Path.Combine(assetDirectory, category);
                if (!Directory.Exists(sourceDirectory))
                {
                    continue;
                }

                var sourceFiles = Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories)
                    .Count();
                var manifestFiles = manifestCounts.GetValueOrDefault(category);
                if (sourceFiles > manifestFiles)
                {
                    logs.Add(
                        $"local asset manifest is older than AssetBundles for {category}: manifest={manifestFiles.ToString(CultureInfo.InvariantCulture)} source={sourceFiles.ToString(CultureInfo.InvariantCulture)}; extraction will rebuild");
                    return false;
                }
            }

            return existing > 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return false;
        }
    }

    private static string JsonText(JsonElement element, string key)
    {
        return element.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? ""
            : "";
    }

    private static string FileFromAssetUrl(string url)
    {
        var normalized = url.Trim().Replace('\\', '/').TrimStart('/');
        return normalized.StartsWith(AppDefaults.GameAssetsPrefix, StringComparison.OrdinalIgnoreCase)
            ? normalized[AppDefaults.GameAssetsPrefix.Length..]
            : "";
    }

    private static string ReadText(string path)
    {
        try
        {
            return File.Exists(path) ? File.ReadAllText(path).Trim() : "";
        }
        catch (IOException)
        {
            return "";
        }
    }

    private static IEnumerable<string> SplitLines(string text)
    {
        return text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static string FirstNonEmpty(params string[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? "";
    }

    private static void AddFlag(List<string> command, bool enabled, string flag)
    {
        if (enabled)
        {
            command.Add(flag);
        }
    }

    private static void AddOption(List<string> command, string value, string option)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        command.Add(option);
        command.Add(value);
    }

    private static string QuoteIfNeeded(string value)
    {
        return value.Contains(' ') ? $"\"{value}\"" : value;
    }
}