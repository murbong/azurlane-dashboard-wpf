using AzurLaneDashboard.Core.Models;

namespace AzurLaneDashboard.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    public string LuaRepoDirectory
    {
        get => luaRepoDirectory;
        set
        {
            if (SetProperty(ref luaRepoDirectory, value))
            {
                SaveSettings();
            }
        }
    }

    public string LuaBranch
    {
        get => luaBranch;
        set
        {
            if (SetProperty(ref luaBranch, value))
            {
                SaveSettings();
            }
        }
    }

    public string MapsDirectory
    {
        get => mapsDirectory;
        set
        {
            if (SetProperty(ref mapsDirectory, value))
            {
                SaveSettings();
            }
        }
    }

    public string GameResourcesDirectory
    {
        get => gameResourcesDirectory;
        set
        {
            if (SetProperty(ref gameResourcesDirectory, value))
            {
                SaveSettings();
            }
        }
    }

    public string StaticAssetsDirectory
    {
        get => staticAssetsDirectory;
        set
        {
            if (SetProperty(ref staticAssetsDirectory, value))
            {
                SaveSettings();
            }
        }
    }

    public string LocalAssetManifest
    {
        get => localAssetManifest;
        set
        {
            if (SetProperty(ref localAssetManifest, value))
            {
                SaveSettings();
            }
        }
    }

    public string UpdateStatePath
    {
        get => updateStatePath;
        set
        {
            if (SetProperty(ref updateStatePath, value))
            {
                SaveSettings();
            }
        }
    }

    public string CategoriesText
    {
        get => categoriesText;
        set
        {
            if (SetProperty(ref categoriesText, value))
            {
                SaveSettings();
            }
        }
    }

    public int ProgressEvery
    {
        get => progressEvery;
        set
        {
            if (SetProperty(ref progressEvery, Math.Max(0, value)))
            {
                SaveSettings();
            }
        }
    }

    public bool SkipGit
    {
        get => skipGit;
        set
        {
            if (SetProperty(ref skipGit, value))
            {
                SaveSettings();
            }
        }
    }

    public bool SkipRegal
    {
        get => skipRegal;
        set
        {
            if (SetProperty(ref skipRegal, value))
            {
                SaveSettings();
            }
        }
    }

    public bool SkipGameResources
    {
        get => skipGameResources;
        set
        {
            if (SetProperty(ref skipGameResources, value))
            {
                SaveSettings();
            }
        }
    }

    public bool RequireGameResources
    {
        get => requireGameResources;
        set
        {
            if (SetProperty(ref requireGameResources, value))
            {
                SaveSettings();
            }
        }
    }

    public bool MetadataOnly
    {
        get => metadataOnly;
        set
        {
            if (SetProperty(ref metadataOnly, value))
            {
                SaveSettings();
            }
        }
    }

    public bool SkipScripts
    {
        get => skipScripts;
        set
        {
            if (SetProperty(ref skipScripts, value))
            {
                SaveSettings();
            }
        }
    }

    public bool ForceGameResources
    {
        get => forceGameResources;
        set
        {
            if (SetProperty(ref forceGameResources, value))
            {
                SaveSettings();
            }
        }
    }

    public bool SkipIconExtract
    {
        get => skipIconExtract;
        set
        {
            if (SetProperty(ref skipIconExtract, value))
            {
                SaveSettings();
            }
        }
    }

    public bool ForceIconExtract
    {
        get => forceIconExtract;
        set
        {
            if (SetProperty(ref forceIconExtract, value))
            {
                SaveSettings();
            }
        }
    }

    private static IReadOnlyList<string> ParseCategories(string value)
    {
        var categories = value
            .Split([',', ';', '\r', '\n', '\t', ' '],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(category => !category.Equals("painting", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return categories.Count > 0 ? categories : AppSettings.DefaultCategories;
    }

    private static IReadOnlyList<string> NormalizeAssetCategories(IEnumerable<string> categories)
    {
        var normalized = categories
            .Where(category => !string.IsNullOrWhiteSpace(category))
            .Select(category => category.Trim())
            .Where(category => !category.Equals("painting", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return normalized.Count > 0 ? normalized : AppSettings.DefaultCategories;
    }
}
