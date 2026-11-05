using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AzurLaneDashboard.Resources;

internal sealed partial class NativeAssetExtractor
{
    private const int Texture2DTypeId = 28;
    private const long MaxDecodedTextureBytes = 16L * 1024 * 1024;
    private const long CleanupDecodedTextureBytes = 256L * 1024 * 1024;
    private static long decodedTextureBytesSinceCleanup;

    private static readonly Regex UnsafeFileNameRegex =
        new(@"[^A-Za-z0-9._-]+", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public Task<AssetExtractionResult> ExtractAsync(
        string assetDirectory,
        string outputDirectory,
        string manifestPath,
        IReadOnlyCollection<string> categories,
        bool force,
        int progressEvery,
        IList<string> logs,
        IProgress<UpdateProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(
            () => Extract(assetDirectory, outputDirectory, manifestPath, categories, force, progressEvery, logs,
                progress, cancellationToken),
            cancellationToken);
    }

    private static AssetExtractionResult Extract(
        string assetDirectory,
        string outputDirectory,
        string manifestPath,
        IReadOnlyCollection<string> categories,
        bool force,
        int progressEvery,
        IList<string> logs,
        IProgress<UpdateProgress>? progress,
        CancellationToken cancellationToken)
    {
        assetDirectory = Path.GetFullPath(assetDirectory);
        outputDirectory = Path.GetFullPath(outputDirectory);
        manifestPath = Path.GetFullPath(manifestPath);

        if (!Directory.Exists(assetDirectory))
        {
            logs.Add($"asset bundle directory not found: {assetDirectory}; native icon extraction skipped");
            return new AssetExtractionResult(0, 0, 0, 0, manifestPath);
        }

        var selectedCategories = new HashSet<string>(
            categories.Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value.ToLowerInvariant()),
            StringComparer.OrdinalIgnoreCase);
        if (selectedCategories.Count == 0)
        {
            logs.Add("no asset categories selected; native icon extraction skipped");
            return new AssetExtractionResult(0, 0, 0, 0, manifestPath);
        }

        var selectedFiles = Directory.EnumerateFiles(assetDirectory, "*", SearchOption.AllDirectories)
            .Where(path => IsSelected(path, assetDirectory, selectedCategories))
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (selectedFiles.Length == 0)
        {
            logs.Add($"no selected asset bundles found under {assetDirectory}");
            return new AssetExtractionResult(0, 0, 0, 0, manifestPath);
        }

        Report(progress, $"PNG 추출 준비: {selectedFiles.Length:N0}개 번들", 0, selectedFiles.Length);
        Directory.CreateDirectory(outputDirectory);
        var entries = new List<AssetManifestEntry>();
        var failures = 0;
        var skipped = 0;

        for (var index = 0; index < selectedFiles.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = selectedFiles[index];
            try
            {
                var extracted = ExtractBundleImages(
                    assetDirectory,
                    path,
                    outputDirectory,
                    force,
                    logs,
                    cancellationToken);
                entries.AddRange(extracted.Entries);
                skipped += extracted.Skipped;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                failures++;
                if (failures <= 20)
                {
                    logs.Add($"asset extract failed: {RelativePosix(assetDirectory, path)}: {ex.Message}");
                }
            }

            var current = index + 1;
            if (ShouldReportProgress(current, selectedFiles.Length, progressEvery))
            {
                Report(progress,
                    $"PNG 추출 {current:N0}/{selectedFiles.Length:N0} · image {entries.Count:N0}, skip {skipped:N0}, fail {failures:N0}",
                    current, selectedFiles.Length);
                logs.Add(
                    $"[{current.ToString(CultureInfo.InvariantCulture)}/{selectedFiles.Length.ToString(CultureInfo.InvariantCulture)}] native images={entries.Count.ToString(CultureInfo.InvariantCulture)} skipped={skipped.ToString(CultureInfo.InvariantCulture)} failures={failures.ToString(CultureInfo.InvariantCulture)} memory={ProcessMemoryText()}");
            }
        }

        var merged = MergeManifest(manifestPath, selectedCategories, entries);
        Directory.CreateDirectory(Path.GetDirectoryName(manifestPath) ?? Directory.GetCurrentDirectory());
        File.WriteAllText(manifestPath, JsonSerializer.Serialize(merged, JsonOptions));
        logs.Add(
            $"native asset manifest: {manifestPath} ({merged.Count.ToString(CultureInfo.InvariantCulture)} images, {failures.ToString(CultureInfo.InvariantCulture)} failures)");
        if (failures > 20)
        {
            logs.Add($"native asset extraction suppressed {failures - 20} additional failure logs");
        }

        return new AssetExtractionResult(selectedFiles.Length, entries.Count, skipped, failures, manifestPath);
    }
}

internal sealed record AssetExtractionResult(
    int Bundles,
    int Images,
    int Skipped,
    int Failures,
    string ManifestPath);
