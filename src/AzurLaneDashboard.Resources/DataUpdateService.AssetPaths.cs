using System.Security.Cryptography;

namespace AzurLaneDashboard.Resources;

public sealed partial class DataUpdateService
{
    private static bool ShouldPullHashRow(HashRow row, ISet<string> categories, bool skipScripts)
    {
        if (!ValidAssetPath(row.FilePath))
        {
            return false;
        }

        if (!skipScripts && (row.FilePath == "scripts32" || row.FilePath == "scripts64"))
        {
            return true;
        }

        var firstSegment = row.FilePath.Split('/', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return firstSegment is not null && categories.Contains(firstSegment);
    }

    private static bool IsScriptAsset(HashRow row)
    {
        return row.FilePath == "scripts32" || row.FilePath == "scripts64";
    }

    private static bool IsSafeAssetCategory(string value)
    {
        return value.Length > 0 &&
               value.All(ch => char.IsAsciiLetterOrDigit(ch) || ch is '_' or '-');
    }

    private static string NormalizeRemoteAssetPath(string value)
    {
        var normalized = value.Trim().Replace('\\', '/');
        while (normalized.StartsWith("./", StringComparison.Ordinal))
        {
            normalized = normalized[2..];
        }

        return normalized.TrimStart('/');
    }

    private static bool ValidAssetPath(string value)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            value.StartsWith("/", StringComparison.Ordinal) ||
            value.StartsWith("\\", StringComparison.Ordinal) ||
            value.Contains("..", StringComparison.Ordinal) ||
            Path.IsPathRooted(value))
        {
            return false;
        }

        return true;
    }

    private static bool LocalAssetIsCurrent(string localPath, HashRow row)
    {
        return row.HasHash ? LocalHashMatches(localPath, row) : File.Exists(localPath);
    }

    private static bool LocalHashMatches(string localPath, HashRow row)
    {
        var info = new FileInfo(localPath);
        if (!info.Exists || info.Length != row.Size)
        {
            return false;
        }

        using var stream = File.OpenRead(localPath);
        var hash = Convert.ToHexString(MD5.HashData(stream)).ToLowerInvariant();
        return hash.Equals(row.Md5Hash, StringComparison.OrdinalIgnoreCase);
    }
}
