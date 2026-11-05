using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using AzurLaneDashboard.Core.Configuration;

namespace AzurLaneDashboard.Resources;

internal sealed partial class NativeAssetExtractor
{
    private static AssetManifestEntry CreateEntry(string category, string bundlePath, string textureName, string file,
        bool alias)
    {
        return new AssetManifestEntry(
            category,
            bundlePath,
            textureName,
            file,
            AppDefaults.GameAssetsUrlPrefix + file,
            alias);
    }

    private static List<JsonObject> MergeManifest(
        string manifestPath,
        IReadOnlySet<string> replacedCategories,
        IEnumerable<AssetManifestEntry> entries)
    {
        var merged = new List<JsonObject>();
        if (File.Exists(manifestPath))
        {
            try
            {
                var existing = JsonNode.Parse(File.ReadAllText(manifestPath)) as JsonArray;
                if (existing is not null)
                {
                    merged.AddRange(existing.OfType<JsonObject>()
                        .Where(node => !replacedCategories.Contains(GetString(node, "category").ToLowerInvariant()))
                        .Select(node => node.DeepClone().AsObject()));
                }
            }
            catch (JsonException)
            {
            }
            catch (IOException)
            {
            }
        }

        foreach (var entry in entries)
        {
            var node = new JsonObject
            {
                ["category"] = entry.Category,
                ["bundle_path"] = entry.BundlePath,
                ["texture_name"] = entry.TextureName,
                ["file"] = entry.File,
                ["url"] = entry.Url
            };
            if (entry.Alias)
            {
                node["alias"] = true;
            }

            merged.Add(node);
        }

        return merged;
    }

    private static bool IsSelected(string path, string root, IReadOnlySet<string> categories)
    {
        var relative = RelativePosix(root, path);
        var first = relative.Split('/').FirstOrDefault();
        return !string.IsNullOrWhiteSpace(first) && categories.Contains(first.ToLowerInvariant());
    }

    private static string RelativePosix(string root, string path)
    {
        return Path.GetRelativePath(root, path).Replace('\\', '/');
    }

    private static string RelativeDirectory(string relativePath)
    {
        var directory = Path.GetDirectoryName(relativePath.Replace('/', Path.DirectorySeparatorChar));
        if (string.IsNullOrWhiteSpace(directory))
        {
            return "";
        }

        var parts = directory.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries).Skip(1);
        return string.Join('/', parts);
    }

    private static string PosixJoin(params string[] parts)
    {
        return string.Join('/',
            parts.Where(part => !string.IsNullOrWhiteSpace(part)).Select(part => part.Replace('\\', '/').Trim('/')));
    }

    private static string SanitizeFileName(string value)
    {
        var sanitized = UnsafeFileNameRegex.Replace(value.Trim(), "_").Trim('_');
        return string.IsNullOrWhiteSpace(sanitized) ? "asset" : sanitized;
    }

    private static string GetString(JsonObject node, string key)
    {
        if (!node.TryGetPropertyValue(key, out var value) || value is null)
        {
            return "";
        }

        try
        {
            return value.GetValue<string>() ?? "";
        }
        catch (InvalidOperationException)
        {
            return Convert.ToString(value.GetValue<object>(), CultureInfo.InvariantCulture) ?? "";
        }
    }
}
