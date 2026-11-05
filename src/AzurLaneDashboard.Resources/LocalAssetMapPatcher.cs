using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using AzurLaneDashboard.Core.Configuration;

namespace AzurLaneDashboard.Resources;

internal static class LocalAssetMapPatcher
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static int Patch(string manifestPath, string mapsDirectory, string staticAssetsDirectory, IList<string> logs)
    {
        manifestPath = Path.GetFullPath(manifestPath);
        mapsDirectory = Path.GetFullPath(mapsDirectory);
        staticAssetsDirectory = Path.GetFullPath(staticAssetsDirectory);
        if (!File.Exists(manifestPath))
        {
            logs.Add($"local asset manifest not found: {manifestPath}; map icon patch skipped");
            return 0;
        }

        var manifest = JsonNode.Parse(File.ReadAllText(manifestPath)) as JsonArray;
        if (manifest is null)
        {
            logs.Add($"invalid local asset manifest: {manifestPath}; map icon patch skipped");
            return 0;
        }

        var index = BuildAssetIndex(manifest, staticAssetsDirectory, logs);
        var total = 0;
        total += PatchMapFile(Path.Combine(mapsDirectory, "equipment.json"), item =>
        {
            var url = PickUrl(index, ["equips", "props", "skillicon"], CandidateKeys(Get(item, "icon_id"), Get(item, "id"), Get(item, "base")));
            return SetIfChanged(item, "icon_url", url);
        }, logs);
        total += PatchMapFile(Path.Combine(mapsDirectory, "equipment_skins.json"), item =>
        {
            var url = PickUrl(index, ["equips", "props"], CandidateKeys(Get(item, "icon_id"), Get(item, "id")));
            return SetIfChanged(item, "icon_url", url);
        }, logs);
        total += PatchMapFile(Path.Combine(mapsDirectory, "spweapons.json"), item =>
        {
            var url = PickUrl(index, ["spweapon", "props"], CandidateKeys(Get(item, "icon_id"), Get(item, "id")));
            return SetIfChanged(item, "icon_url", url);
        }, logs);
        total += PatchMapFile(Path.Combine(mapsDirectory, "skills.json"), item =>
        {
            var url = PickUrl(index, ["skillicon"], CandidateKeys(Get(item, "id")));
            return SetIfChanged(item, "icon_url", url);
        }, logs);
        total += PatchMapFile(Path.Combine(mapsDirectory, "skins.json"), item =>
        {
            var changed = 0;
            var keys = CandidateKeys(Get(item, "painting"), Get(item, "prefab"), Get(item, "id"));
            var icon = PickUrl(index, ["squareicon"], keys);
            var qicon = PickUrl(index, ["qicon"], keys);
            var shipyard = PickUrl(index, ["shipyardicon"], keys);
            var painting = PickUrl(index, ["painting"], keys);
            var displayIcon = FirstNonEmpty(icon, shipyard, qicon);
            changed += SetIfChanged(item, "icon_url", displayIcon);
            changed += SetIfChanged(item, "qicon_url", qicon);
            changed += SetIfChanged(item, "shipyard_url", shipyard);
            changed += SetIfChanged(item, "painting_url", painting);
            return changed;
        }, logs);

        logs.Add($"patched local asset URLs: {total.ToString(CultureInfo.InvariantCulture)}");
        return total;
    }

    private static int PatchMapFile(string path, Func<JsonObject, int> patcher, IList<string> logs)
    {
        if (!File.Exists(path))
        {
            return 0;
        }

        var root = JsonNode.Parse(File.ReadAllText(path)) as JsonObject;
        if (root is null)
        {
            return 0;
        }

        var changed = 0;
        foreach (var item in root.Select(pair => pair.Value).OfType<JsonObject>())
        {
            changed += patcher(item);
        }

        if (changed > 0)
        {
            File.WriteAllText(path, root.ToJsonString(JsonOptions));
        }

        logs.Add($"{Path.GetFileName(path)}: {changed.ToString(CultureInfo.InvariantCulture)} local URLs");
        return changed;
    }

    private static Dictionary<string, Dictionary<string, string>> BuildAssetIndex(
        JsonArray entries,
        string staticAssetsDirectory,
        IList<string> logs)
    {
        var index = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        var missingFiles = 0;
        foreach (var node in entries.OfType<JsonObject>())
        {
            var category = Get(node, "category").ToLowerInvariant();
            var url = Get(node, "url");
            if (string.IsNullOrWhiteSpace(category) || string.IsNullOrWhiteSpace(url))
            {
                continue;
            }

            var file = FirstNonEmpty(Get(node, "file"), FileFromAssetUrl(url));
            if (!string.IsNullOrWhiteSpace(file))
            {
                var localPath = Path.Combine(staticAssetsDirectory, file.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(localPath))
                {
                    missingFiles++;
                    continue;
                }
            }

            if (!index.TryGetValue(category, out var categoryIndex))
            {
                categoryIndex = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                index[category] = categoryIndex;
            }

            foreach (var key in new[]
                     {
                         Path.GetFileNameWithoutExtension(file),
                         Path.GetFileNameWithoutExtension(Get(node, "bundle_path").Replace('\\', '/')),
                         Get(node, "texture_name")
                     })
            {
                if (!string.IsNullOrWhiteSpace(key))
                {
                    categoryIndex.TryAdd(key.ToLowerInvariant(), url);
                }
            }
        }

        if (missingFiles > 0)
        {
            logs.Add($"local asset manifest skipped {missingFiles.ToString(CultureInfo.InvariantCulture)} missing PNG entries under {staticAssetsDirectory}");
        }

        return index;
    }

    private static string FileFromAssetUrl(string url)
    {
        var normalized = url.Trim().Replace('\\', '/').TrimStart('/');
        return normalized.StartsWith(AppDefaults.GameAssetsPrefix, StringComparison.OrdinalIgnoreCase)
            ? normalized[AppDefaults.GameAssetsPrefix.Length..]
            : "";
    }

    private static IReadOnlyList<string> CandidateKeys(params string[] values)
    {
        var result = new List<string>();
        foreach (var value in values)
        {
            var raw = value.Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            Add(raw);
            Add(raw.Replace("_n", "", StringComparison.Ordinal));
            if (raw.All(char.IsDigit))
            {
                Add($"skill_{raw}");
                Add($"equips_{raw}");
                Add($"spweapon_{raw}");
            }
        }

        return result;

        void Add(string key)
        {
            if (!result.Contains(key, StringComparer.OrdinalIgnoreCase))
            {
                result.Add(key);
            }
        }
    }

    private static string PickUrl(
        IReadOnlyDictionary<string, Dictionary<string, string>> index,
        IReadOnlyList<string> categories,
        IReadOnlyList<string> keys)
    {
        foreach (var category in categories)
        {
            if (!index.TryGetValue(category, out var categoryIndex))
            {
                continue;
            }

            foreach (var key in keys)
            {
                if (categoryIndex.TryGetValue(key, out var url))
                {
                    return url;
                }
            }
        }

        return "";
    }

    private static int SetIfChanged(JsonObject item, string key, string value)
    {
        if (string.IsNullOrWhiteSpace(value) || Get(item, key) == value)
        {
            return 0;
        }

        item[key] = value;
        return 1;
    }

    private static string Get(JsonObject item, string key)
    {
        return item.TryGetPropertyValue(key, out var value) ? NodeToString(value) : "";
    }

    private static string NodeToString(JsonNode? node)
    {
        if (node is null)
        {
            return "";
        }

        try
        {
            return node.GetValue<string>() ?? "";
        }
        catch (InvalidOperationException)
        {
            return Convert.ToString(node.GetValue<object>(), CultureInfo.InvariantCulture) ?? "";
        }
    }

    private static string FirstNonEmpty(params string[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? "";
    }
}
