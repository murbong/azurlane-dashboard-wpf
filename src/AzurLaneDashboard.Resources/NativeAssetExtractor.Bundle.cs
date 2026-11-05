using System.Diagnostics;
using System.Globalization;
using System.Runtime;
using AssetsTools.NET;
using AssetsTools.NET.Extra;
using AssetsTools.NET.Texture;

namespace AzurLaneDashboard.Resources;

internal sealed partial class NativeAssetExtractor
{
    private static BundleExtractionResult ExtractBundleImages(
        string assetDirectory,
        string bundlePath,
        string outputDirectory,
        bool force,
        IList<string> logs,
        CancellationToken cancellationToken)
    {
        var relativePath = RelativePosix(assetDirectory, bundlePath);
        var category = relativePath.Split('/').FirstOrDefault() ?? "unknown";
        var manager = new AssetsManager
        {
            UseQuickLookup = true
        };

        try
        {
            var images = ReadTextureImages(manager, bundlePath, cancellationToken)
                .Where(image => !string.Equals(image.TextureName, "UISprite", StringComparison.Ordinal))
                .ToList();
            if (images.Count == 0)
            {
                return new BundleExtractionResult([], 0);
            }

            return WriteBundleImages(relativePath, category, bundlePath, outputDirectory, force, images, logs,
                cancellationToken);
        }
        finally
        {
            manager.UnloadAll(true);
        }
    }

    private static BundleExtractionResult WriteBundleImages(
        string relativePath,
        string category,
        string bundlePath,
        string outputDirectory,
        bool force,
        IReadOnlyList<TextureAsset> images,
        IList<string> logs,
        CancellationToken cancellationToken)
    {
        if (images.Count == 0)
        {
            return new BundleExtractionResult([], 0);
        }

        var skipped = 0;
        var entries = new List<AssetManifestEntry>();
        if (images.Count == 1)
        {
            var image = images[0];
            var outputRelative = PosixJoin(category, RelativeDirectory(relativePath),
                SanitizeFileName(Path.GetFileName(bundlePath)) + ".png");
            var outputPath = Path.Combine(outputDirectory, outputRelative.Replace('/', Path.DirectorySeparatorChar));
            var outcome = WriteTexture(image, outputPath, force, logs, cancellationToken);
            if (outcome is TextureWriteOutcome.Empty or TextureWriteOutcome.TooLarge)
            {
                skipped++;
                return new BundleExtractionResult(entries, skipped);
            }

            if (outcome == TextureWriteOutcome.Existing)
            {
                skipped++;
            }

            entries.Add(CreateEntry(category, relativePath, image.TextureName, outputRelative, false));

            var aliasRelative = PosixJoin(
                category,
                RelativeDirectory(relativePath),
                SanitizeFileName(image.TextureName) + ".png");
            if (!string.Equals(aliasRelative, outputRelative, StringComparison.OrdinalIgnoreCase))
            {
                var aliasPath = Path.Combine(outputDirectory, aliasRelative.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(aliasPath) ?? outputDirectory);
                if ((force || !File.Exists(aliasPath)) && File.Exists(outputPath))
                {
                    File.Copy(outputPath, aliasPath, true);
                }

                entries.Add(CreateEntry(category, relativePath, image.TextureName, aliasRelative, true));
            }

            return new BundleExtractionResult(entries, skipped);
        }

        var bundleName = Path.GetFileNameWithoutExtension(relativePath.Replace('/', Path.DirectorySeparatorChar));
        if (string.IsNullOrWhiteSpace(bundleName))
        {
            bundleName = SanitizeFileName(Path.GetFileName(bundlePath));
        }

        foreach (var image in images)
        {
            var outputRelative = PosixJoin(category, RelativeDirectory(relativePath), SanitizeFileName(bundleName),
                SanitizeFileName(image.TextureName) + ".png");
            var outputPath = Path.Combine(outputDirectory, outputRelative.Replace('/', Path.DirectorySeparatorChar));
            var outcome = WriteTexture(image, outputPath, force, logs, cancellationToken);
            if (outcome is TextureWriteOutcome.Empty or TextureWriteOutcome.TooLarge)
            {
                skipped++;
                continue;
            }

            if (outcome == TextureWriteOutcome.Existing)
            {
                skipped++;
            }

            entries.Add(CreateEntry(category, relativePath, image.TextureName, outputRelative, false));
        }

        return new BundleExtractionResult(entries, skipped);
    }

    private static IReadOnlyList<TextureAsset> ReadTextureImages(
        AssetsManager manager,
        string bundlePath,
        CancellationToken cancellationToken)
    {
        var bundle = manager.LoadBundleFile(bundlePath, true);
        var result = new List<TextureAsset>();
        var fileNames = bundle.file.GetAllFileNames()
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToArray();
        foreach (var fileName in fileNames)
        {
            cancellationToken.ThrowIfCancellationRequested();
            AssetsFileInstance assetsFile;
            try
            {
                assetsFile = manager.LoadAssetsFileFromBundle(bundle, fileName, true);
            }
            catch
            {
                continue;
            }

            if (assetsFile is null || assetsFile.file is null)
            {
                continue;
            }

            foreach (var info in assetsFile.file.AssetInfos)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (info.TypeId != Texture2DTypeId)
                {
                    continue;
                }

                var baseField = manager.GetBaseField(assetsFile, info);
                var texture = TextureFile.ReadTextureFile(baseField);
                if (texture.m_Width <= 0 || texture.m_Height <= 0)
                {
                    continue;
                }

                result.Add(new TextureAsset(assetsFile, texture));
            }
        }

        return result;
    }

    private static TextureWriteOutcome WriteTexture(
        TextureAsset texture,
        string outputPath,
        bool force,
        IList<string> logs,
        CancellationToken cancellationToken)
    {
        if (!force && File.Exists(outputPath))
        {
            return TextureWriteOutcome.Existing;
        }

        cancellationToken.ThrowIfCancellationRequested();
        var decodedBytes = EstimateDecodedBytes(texture);
        if (decodedBytes > MaxDecodedTextureBytes)
        {
            logs.Add(
                $"asset texture skipped: {texture.TextureName} estimated decoded size {FormatBytes(decodedBytes)} exceeds {FormatBytes(MaxDecodedTextureBytes)}");
            return TextureWriteOutcome.TooLarge;
        }

        byte[]? data = null;
        try
        {
            data = texture.Texture.FillPictureData(texture.AssetsFile);
            if (data.Length == 0)
            {
                return TextureWriteOutcome.Empty;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? Directory.GetCurrentDirectory());
            var tempPath = outputPath + ".tmp";
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }

            var helper = new TextureFile
            {
                m_Name = texture.TextureName,
                m_Width = texture.Texture.m_Width,
                m_Height = texture.Texture.m_Height,
                m_TextureFormat = texture.Texture.m_TextureFormat
            };
            if (!helper.DecodeTextureImage(data, tempPath, ImageExportType.Png, 100))
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }

                throw new InvalidOperationException($"Texture decoder rejected {texture.TextureName}");
            }

            if (File.Exists(outputPath))
            {
                File.Delete(outputPath);
            }

            File.Move(tempPath, outputPath);
            return TextureWriteOutcome.Written;
        }
        finally
        {
            data = null;
            CleanupTextureDecodeMemory(decodedBytes);
        }
    }

    private static void CleanupTextureDecodeMemory(long decodedBytes)
    {
        if (decodedBytes <= 0 ||
            Interlocked.Add(ref decodedTextureBytesSinceCleanup, decodedBytes) < CleanupDecodedTextureBytes)
        {
            return;
        }

        Interlocked.Exchange(ref decodedTextureBytesSinceCleanup, 0);
        GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Optimized, blocking: true, compacting: true);
    }

    private static string ProcessMemoryText()
    {
        using var process = Process.GetCurrentProcess();
        return string.Format(
            CultureInfo.InvariantCulture,
            "working={0:N1}MB private={1:N1}MB",
            process.WorkingSet64 / 1024d / 1024d,
            process.PrivateMemorySize64 / 1024d / 1024d);
    }

    private static long EstimateDecodedBytes(TextureAsset texture)
    {
        try
        {
            return checked((long)texture.Texture.m_Width * texture.Texture.m_Height * 4);
        }
        catch (OverflowException)
        {
            return long.MaxValue;
        }
    }

    private static string FormatBytes(long bytes)
    {
        return bytes >= 1024L * 1024 * 1024
            ? $"{bytes / 1024d / 1024d / 1024d:0.##}GB"
            : $"{bytes / 1024d / 1024d:0.##}MB";
    }

    private static void Report(IProgress<UpdateProgress>? progress, string message, int current, int total)
    {
        var percent = total > 0 ? Math.Clamp(current * 100.0 / total, 0, 100) : 0;
        progress?.Report(new UpdateProgress("이미지", message, current, total, percent));
    }

    private static bool ShouldReportProgress(int current, int total, int progressEvery)
    {
        return current == 1 ||
               current == total ||
               progressEvery > 0 && current % progressEvery == 0;
    }
}
