using System.Globalization;
using System.IO;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AzurLaneDashboard.Core.Configuration;
using AzurLaneDashboard.Data.Services;

namespace AzurLaneDashboard.App.Converters;

public sealed class AssetImageConverter : IValueConverter
{
    private const string StaticPrefix = "static/";
    private const long MaxImageCacheBytes = 256L * 1024 * 1024;
    private const int DefaultIconDecodePixelWidth = 96;
    private const int MediumDecodePixelWidth = 256;
    private const int LargeDecodePixelWidth = 768;
    private const int MaxDecodePixelWidth = 2048;
    private const int MaxMissingCacheEntries = 2048;
    private static readonly TimeSpan RootCacheRefreshInterval = TimeSpan.FromSeconds(2);
    private static readonly Lazy<string> ContentRoot = new(ProjectLocator.FindProjectRoot);
    private static readonly object CacheLock = new();
    private static readonly Dictionary<string, LinkedListNode<ImageCacheEntry>> Cache =
        new(StringComparer.OrdinalIgnoreCase);

    private static readonly LinkedList<ImageCacheEntry> CacheLru = new();
    private static readonly Dictionary<string, DateTime> MissingCache = new(StringComparer.OrdinalIgnoreCase);
    private static long cacheBytes;
    private static readonly object RootCacheLock = new();
    private static DateTime rootCacheCheckedUtc = DateTime.MinValue;
    private static string rootCacheSignature = "";
    private static AssetRootSet rootCache = new([], "");

    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var raw = System.Convert.ToString(value, CultureInfo.InvariantCulture);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var rootSet = AssetRoots();
        var roots = rootSet.Roots;
        var decodePixelWidth = DecodePixelWidth(raw, parameter);
        var cacheKey = string.Concat(raw, "|", decodePixelWidth.ToString(CultureInfo.InvariantCulture), "|",
            rootSet.CacheKey);
        lock (CacheLock)
        {
            if (Cache.TryGetValue(cacheKey, out var cached))
            {
                CacheLru.Remove(cached);
                CacheLru.AddLast(cached);
                return cached.Value.Source;
            }

            if (MissingCache.TryGetValue(cacheKey, out var retryAfterUtc))
            {
                if (retryAfterUtc > DateTime.UtcNow)
                {
                    return null;
                }

                MissingCache.Remove(cacheKey);
            }
        }

        var entry = LoadImage(cacheKey, ResolvePath(raw, roots), decodePixelWidth);
        if (entry is not null)
        {
            lock (CacheLock)
            {
                AddCacheEntry(entry);
            }
        }
        else
        {
            lock (CacheLock)
            {
                if (MissingCache.Count >= MaxMissingCacheEntries)
                {
                    MissingCache.Clear();
                }

                MissingCache[cacheKey] = DateTime.UtcNow + RootCacheRefreshInterval;
            }
        }

        return entry?.Source;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        Binding.DoNothing;

    private static string? ResolvePath(string value, IReadOnlyList<string> roots)
    {
        var normalized = value.Trim().Replace('\\', '/');
        if (normalized.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            normalized.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (Path.IsPathFullyQualified(value))
        {
            return File.Exists(value) ? value : null;
        }

        if (normalized.StartsWith('/'))
        {
            normalized = normalized.TrimStart('/');
        }

        if (normalized.StartsWith(AppDefaults.GameAssetsPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var relative = normalized[AppDefaults.GameAssetsPrefix.Length..];
            foreach (var root in roots)
            {
                var path = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(path))
                {
                    return path;
                }
            }

            return null;
        }

        if (normalized.StartsWith(StaticPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var projectPath = Path.Combine(ContentRoot.Value, normalized.Replace('/', Path.DirectorySeparatorChar));
            return File.Exists(projectPath) ? projectPath : null;
        }

        var fallbackPath = Path.Combine(ContentRoot.Value, normalized.Replace('/', Path.DirectorySeparatorChar));
        return File.Exists(fallbackPath) ? fallbackPath : null;
    }

    private static ImageCacheEntry? LoadImage(string key, string? path, int decodePixelWidth)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.UriSource = new Uri(path, UriKind.Absolute);
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            image.DecodePixelWidth = decodePixelWidth;
            image.EndInit();
            image.Freeze();
            return new ImageCacheEntry(key, image, EstimateBitmapBytes(image));
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or NotSupportedException or
                                       UnauthorizedAccessException or UriFormatException)
        {
            return null;
        }
    }

    private static void AddCacheEntry(ImageCacheEntry entry)
    {
        if (entry.Bytes > MaxImageCacheBytes)
        {
            return;
        }

        if (Cache.TryGetValue(entry.Key, out var existing))
        {
            cacheBytes -= existing.Value.Bytes;
            CacheLru.Remove(existing);
            Cache.Remove(entry.Key);
        }

        var node = new LinkedListNode<ImageCacheEntry>(entry);
        CacheLru.AddLast(node);
        Cache[entry.Key] = node;
        cacheBytes += entry.Bytes;
        TrimCache();
    }

    private static void TrimCache()
    {
        while (cacheBytes > MaxImageCacheBytes && CacheLru.First is not null)
        {
            var oldest = CacheLru.First;
            CacheLru.RemoveFirst();
            Cache.Remove(oldest.Value.Key);
            cacheBytes -= oldest.Value.Bytes;
        }
    }

    private static long EstimateBitmapBytes(BitmapSource image)
    {
        var bytesPerPixel = Math.Max(4, (image.Format.BitsPerPixel + 7) / 8);
        try
        {
            return Math.Max(1, checked((long)image.PixelWidth * image.PixelHeight * bytesPerPixel));
        }
        catch (OverflowException)
        {
            return MaxImageCacheBytes + 1;
        }
    }

    private static int DecodePixelWidth(string raw, object parameter)
    {
        if (parameter is not null &&
            int.TryParse(System.Convert.ToString(parameter, CultureInfo.InvariantCulture), NumberStyles.Integer,
                CultureInfo.InvariantCulture, out var requested) &&
            requested > 0)
        {
            return Math.Min(requested, MaxDecodePixelWidth);
        }

        var normalized = raw.Replace('\\', '/').ToLowerInvariant();
        if (normalized.Contains("/painting/", StringComparison.Ordinal) ||
            normalized.Contains("painting/", StringComparison.Ordinal) ||
            normalized.Contains("/shipyard/", StringComparison.Ordinal) ||
            normalized.Contains("shipyard/", StringComparison.Ordinal))
        {
            return LargeDecodePixelWidth;
        }

        if (normalized.Contains("/qicon/", StringComparison.Ordinal) ||
            normalized.Contains("qicon/", StringComparison.Ordinal) ||
            normalized.Contains("/equips/", StringComparison.Ordinal) ||
            normalized.Contains("equips/", StringComparison.Ordinal) ||
            normalized.Contains("/skill/", StringComparison.Ordinal) ||
            normalized.Contains("skill/", StringComparison.Ordinal))
        {
            return DefaultIconDecodePixelWidth;
        }

        return MediumDecodePixelWidth;
    }

    private static AssetRootSet AssetRoots()
    {
        var now = DateTime.UtcNow;
        lock (RootCacheLock)
        {
            if (rootCache.Roots.Count > 0 && now - rootCacheCheckedUtc < RootCacheRefreshInterval)
            {
                return rootCache;
            }
        }

        var projectRoot = ContentRoot.Value;
        var settingsService = new AppSettingsService();
        var settingsPath = settingsService.SettingsPath;
        var settingsInfo = new FileInfo(settingsPath);
        var signature = string.Join(
            "|",
            projectRoot,
            AppContext.BaseDirectory,
            Environment.CurrentDirectory,
            settingsInfo.Exists ? settingsInfo.LastWriteTimeUtc.Ticks.ToString(CultureInfo.InvariantCulture) : "0",
            settingsInfo.Exists ? settingsInfo.Length.ToString(CultureInfo.InvariantCulture) : "0");

        lock (RootCacheLock)
        {
            rootCacheCheckedUtc = now;
            if (signature == rootCacheSignature)
            {
                return rootCache;
            }
        }

        var settings = settingsService.Load();
        var configured = ResolveRoot(projectRoot, settings.StaticAssetsDirectory);
        var roots = new[]
            {
                configured,
                Path.Combine(projectRoot, AppDefaults.DefaultStaticAssetsDirectory),
                Path.Combine(AppContext.BaseDirectory, AppDefaults.DefaultStaticAssetsDirectory),
                Path.Combine(Environment.CurrentDirectory, AppDefaults.DefaultStaticAssetsDirectory)
            }
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        lock (RootCacheLock)
        {
            rootCacheCheckedUtc = now;
            rootCacheSignature = signature;
            rootCache = new AssetRootSet(roots, signature);
            return rootCache;
        }
    }

    private static string ResolveRoot(string projectRoot, string configuredPath)
    {
        if (string.IsNullOrWhiteSpace(configuredPath))
        {
            return "";
        }

        return Path.IsPathFullyQualified(configuredPath)
            ? configuredPath
            : Path.Combine(projectRoot, configuredPath);
    }

    private sealed record ImageCacheEntry(string Key, ImageSource Source, long Bytes);

    private sealed record AssetRootSet(IReadOnlyList<string> Roots, string CacheKey);
}
