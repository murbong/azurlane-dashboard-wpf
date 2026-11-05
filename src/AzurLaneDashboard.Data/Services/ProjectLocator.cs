using System.IO;
using AzurLaneDashboard.Core.Configuration;

namespace AzurLaneDashboard.Data.Services;

public static class ProjectLocator
{
    private static readonly Lazy<string> ProjectRoot = new(LocateProjectRoot);
    private static readonly Lazy<string> MapsDirectory = new(LocateMapsDirectory);

    public static string FindProjectRoot() => ProjectRoot.Value;

    private static string LocateProjectRoot()
    {
        var baseDirectory = Path.GetFullPath(AppContext.BaseDirectory);
        var isDevelopmentBuild = IsDevelopmentBuildDirectory(baseDirectory);

        if (!isDevelopmentBuild && IsContentRoot(baseDirectory))
        {
            return baseDirectory;
        }

        if (isDevelopmentBuild)
        {
            var sourceRoot = FindAncestor(baseDirectory, IsSourceRoot);
            if (!string.IsNullOrWhiteSpace(sourceRoot))
            {
                return sourceRoot;
            }
        }

        var currentDirectory = Path.GetFullPath(Environment.CurrentDirectory);
        if (IsSourceRoot(currentDirectory))
        {
            return currentDirectory;
        }

        if (!IsSourceRoot(currentDirectory) && IsContentRoot(currentDirectory))
        {
            return currentDirectory;
        }

        if (IsContentRoot(baseDirectory))
        {
            return baseDirectory;
        }

        var contentRoot =
            FindAncestor(baseDirectory, directory => !IsSourceRoot(directory) && IsContentRoot(directory));
        if (!string.IsNullOrWhiteSpace(contentRoot))
        {
            return contentRoot;
        }

        var fallbackSourceRoot = FindAncestor(baseDirectory, IsSourceRoot);
        return fallbackSourceRoot ?? baseDirectory;
    }

    public static string FindMapsDirectory() => MapsDirectory.Value;

    private static string LocateMapsDirectory()
    {
        var contentRoot = FindProjectRoot();
        var mutableMaps = Path.Combine(contentRoot, AppDefaults.DefaultMapsDirectory);
        var defaults = FindBundledDefaultMapsDirectory(contentRoot);
        if (IsMapsDirectory(mutableMaps))
        {
            if (!string.IsNullOrWhiteSpace(defaults))
            {
                SeedMutableMaps(defaults, mutableMaps);
            }

            return mutableMaps;
        }

        if (!string.IsNullOrWhiteSpace(defaults))
        {
            SeedMutableMaps(defaults, mutableMaps);
            return mutableMaps;
        }

        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, AppDefaults.DefaultMapsDirectory),
            Path.Combine(Environment.CurrentDirectory, AppDefaults.DefaultMapsDirectory)
        };
        return candidates.FirstOrDefault(IsMapsDirectory) ?? mutableMaps;
    }

    public static string DefaultDataDirectory() => Path.Combine(FindProjectRoot(), AppDefaults.DefaultDataDirectory);

    public static string DefaultDatabasePath() =>
        Path.Combine(DefaultDataDirectory(), AppDefaults.DefaultDatabaseFileName);

    public static bool IsPortableDistribution()
    {
        var contentRoot = FindProjectRoot();
        return (IsMapsDirectory(Path.Combine(contentRoot, AppDefaults.DefaultMapsDirectory)) ||
                IsMapsDirectory(Path.Combine(contentRoot, AppDefaults.BundledDefaultMapsDirectory))) &&
               !IsSourceRoot(contentRoot) &&
               !IsDevelopmentBuildDirectory(AppContext.BaseDirectory);
    }

    public static string ResolveContentPath(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return FindProjectRoot();
        }

        return Path.IsPathFullyQualified(value)
            ? Path.GetFullPath(value)
            : Path.GetFullPath(Path.Combine(FindProjectRoot(), value));
    }

    private static bool IsContentRoot(string directory)
    {
        return IsSourceRoot(directory) ||
               IsMapsDirectory(Path.Combine(directory, AppDefaults.DefaultMapsDirectory)) ||
               IsMapsDirectory(Path.Combine(directory, AppDefaults.BundledDefaultMapsDirectory));
    }

    private static string? FindBundledDefaultMapsDirectory(string contentRoot)
    {
        var candidates = new[]
        {
            Path.Combine(contentRoot, AppDefaults.BundledDefaultMapsDirectory),
            Path.Combine(AppContext.BaseDirectory, AppDefaults.BundledDefaultMapsDirectory),
            Path.Combine(Environment.CurrentDirectory, AppDefaults.BundledDefaultMapsDirectory)
        };
        return candidates.FirstOrDefault(IsMapsDirectory);
    }

    private static void SeedMutableMaps(string defaultMapsDirectory, string mutableMapsDirectory)
    {
        Directory.CreateDirectory(mutableMapsDirectory);
        foreach (var sourcePath in Directory.EnumerateFiles(defaultMapsDirectory, "*.json", SearchOption.TopDirectoryOnly))
        {
            var targetPath = Path.Combine(mutableMapsDirectory, Path.GetFileName(sourcePath));
            if (ShouldCopyDefaultMap(sourcePath, targetPath))
            {
                File.Copy(sourcePath, targetPath, true);
            }
        }
    }

    private static bool ShouldCopyDefaultMap(string sourcePath, string targetPath)
    {
        if (!File.Exists(targetPath))
        {
            return true;
        }

        var source = new FileInfo(sourcePath);
        var target = new FileInfo(targetPath);
        return source.LastWriteTimeUtc > target.LastWriteTimeUtc;
    }

    private static bool IsSourceRoot(string directory)
    {
        return File.Exists(Path.Combine(directory, "AzurLaneDashboard.slnx")) ||
               File.Exists(Path.Combine(directory, "azurlane_dashboard.py"));
    }

    private static bool IsDevelopmentBuildDirectory(string directory)
    {
        var current = new DirectoryInfo(directory);
        while (current is not null)
        {
            if ((current.Name.Equals("bin", StringComparison.OrdinalIgnoreCase) ||
                 current.Name.Equals("obj", StringComparison.OrdinalIgnoreCase)) &&
                current.Parent is not null &&
                File.Exists(Path.Combine(current.Parent.FullName, "AzurLaneDashboard.App.csproj")))
            {
                return true;
            }

            current = current.Parent;
        }

        return false;
    }

    private static string? FindAncestor(string startDirectory, Func<string, bool> predicate)
    {
        var directory = new DirectoryInfo(startDirectory);
        while (directory is not null)
        {
            if (predicate(directory.FullName))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return null;
    }

    private static bool IsMapsDirectory(string directory)
    {
        return File.Exists(Path.Combine(directory, "ships.json")) &&
               File.Exists(Path.Combine(directory, "equipment.json")) &&
               File.Exists(Path.Combine(directory, "skins.json"));
    }
}
