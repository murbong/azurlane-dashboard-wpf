using AzurLaneDashboard.Core.Configuration;

namespace AzurLaneDashboard.Core.Services;

public enum AndroidBridgeKind
{
    None,
    Adb,
    MuMuManager
}

public sealed record AndroidBridge(AndroidBridgeKind Kind, string ExecutablePath, string DisplayName)
{
    public bool IsAvailable => Kind != AndroidBridgeKind.None && !string.IsNullOrWhiteSpace(ExecutablePath);
}

public static class ExternalToolLocator
{
    private const string MuMuManagerFileName = "MuMuManager.exe";
    private const string PlatformToolsDirectory = "platform-tools";
    private const string ToolsDirectory = "tools";

    public static AndroidBridge ResolveAndroidBridge(
        string? adbPath,
        string? mumuManagerPath,
        string? contentRoot = null)
    {
        var resolvedAdb = ResolveAdbPath(adbPath, contentRoot);
        if (!string.IsNullOrWhiteSpace(resolvedAdb))
        {
            return new AndroidBridge(AndroidBridgeKind.Adb, resolvedAdb, $"ADB: {resolvedAdb}");
        }

        var resolvedManager = ResolveMuMuManagerPath(mumuManagerPath, contentRoot);
        return string.IsNullOrWhiteSpace(resolvedManager)
            ? new AndroidBridge(AndroidBridgeKind.None, "", "ADB/MuMuManager 없음")
            : new AndroidBridge(AndroidBridgeKind.MuMuManager, resolvedManager, $"MuMuManager: {resolvedManager}");
    }

    public static string? ResolveAdbPath(string? configuredPath, string? contentRoot = null)
    {
        foreach (var candidate in AdbCandidates(configuredPath, contentRoot))
        {
            var resolved = ResolveExecutable(candidate);
            if (!string.IsNullOrWhiteSpace(resolved) && IsAdbPath(resolved))
            {
                return resolved;
            }
        }

        return null;
    }

    public static string? ResolveMuMuManagerPath(string? configuredPath, string? contentRoot = null)
    {
        foreach (var candidate in MuMuManagerCandidates(configuredPath, contentRoot))
        {
            var resolved = ResolveExecutable(candidate);
            if (!string.IsNullOrWhiteSpace(resolved) && IsMuMuManagerPath(resolved))
            {
                return resolved;
            }
        }

        return null;
    }

    public static IReadOnlyList<string> BuildAndroidBridgeArguments(
        AndroidBridge bridge,
        string serial,
        IReadOnlyList<string> adbCommand)
    {
        return bridge.Kind switch
        {
            AndroidBridgeKind.Adb => BuildAdbArguments(serial, adbCommand),
            AndroidBridgeKind.MuMuManager => ["adb", "--vmindex", "0", "--cmd", JoinCommand(adbCommand)],
            _ => []
        };
    }

    public static IReadOnlyList<string> BuildAdbConnectArguments(string serial)
    {
        return ["connect", serial.Trim()];
    }

    public static bool IsTcpAdbSerial(string? serial)
    {
        if (string.IsNullOrWhiteSpace(serial))
        {
            return false;
        }

        var trimmed = serial.Trim();
        var separator = trimmed.LastIndexOf(':');
        if (separator <= 0 || separator == trimmed.Length - 1)
        {
            return false;
        }

        return int.TryParse(trimmed[(separator + 1)..], out var port) && port is > 0 and <= 65535;
    }

    public static string? FindOnPath(string? executable)
    {
        if (string.IsNullOrWhiteSpace(executable))
        {
            return null;
        }

        if (Path.IsPathFullyQualified(executable) && File.Exists(executable))
        {
            return Path.GetFullPath(executable);
        }

        var extensions = OperatingSystem.IsWindows()
            ? (Environment.GetEnvironmentVariable("PATHEXT") ?? ".EXE;.BAT;.CMD;.COM")
            .Split(';', StringSplitOptions.RemoveEmptyEntries)
            : [""];
        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? "")
                 .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            foreach (var extension in extensions)
            {
                var fileName = executable.EndsWith(extension, StringComparison.OrdinalIgnoreCase)
                    ? executable
                    : executable + extension;
                var candidate = Path.Combine(directory, fileName);
                if (File.Exists(candidate))
                {
                    return Path.GetFullPath(candidate);
                }
            }
        }

        return null;
    }

    public static bool IsAdbPath(string path)
    {
        return Path.GetFileName(path).Equals("adb.exe", StringComparison.OrdinalIgnoreCase) ||
               Path.GetFileName(path).Equals("adb", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsMuMuManagerPath(string path)
    {
        return Path.GetFileName(path).Equals(MuMuManagerFileName, StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> AdbCandidates(string? configuredPath, string? contentRoot)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath) &&
            !IsMuMuManagerPath(configuredPath) &&
            !configuredPath.Equals(AppDefaults.DefaultAdbExecutable, StringComparison.OrdinalIgnoreCase))
        {
            yield return configuredPath;
        }

        if (!string.IsNullOrWhiteSpace(contentRoot))
        {
            yield return Path.Combine(contentRoot, ToolsDirectory, PlatformToolsDirectory, "adb.exe");
            yield return Path.Combine(contentRoot, PlatformToolsDirectory, "adb.exe");
        }

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrWhiteSpace(localAppData))
        {
            yield return Path.Combine(localAppData, "Android", "Sdk", PlatformToolsDirectory, "adb.exe");
        }

        yield return @"C:\Program Files\Netease\MuMuPlayer\nx_main\adb.exe";
        yield return @"C:\Program Files (x86)\Netease\MuMuPlayer\nx_main\adb.exe";
        yield return AppDefaults.DefaultAdbExecutable;
    }

    private static IEnumerable<string> MuMuManagerCandidates(string? configuredPath, string? contentRoot)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            yield return configuredPath;
        }

        if (!string.IsNullOrWhiteSpace(contentRoot))
        {
            yield return Path.Combine(contentRoot, ToolsDirectory, "MuMuManager", MuMuManagerFileName);
            yield return Path.Combine(contentRoot, ToolsDirectory, MuMuManagerFileName);
        }

        yield return @"C:\Program Files\Netease\MuMuPlayer\nx_main\MuMuManager.exe";
        yield return @"C:\Program Files (x86)\Netease\MuMuPlayer\nx_main\MuMuManager.exe";
    }

    private static string? ResolveExecutable(string candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return null;
        }

        if (Path.IsPathFullyQualified(candidate))
        {
            return File.Exists(candidate) ? Path.GetFullPath(candidate) : null;
        }

        return FindOnPath(candidate);
    }

    private static IReadOnlyList<string> BuildAdbArguments(string serial, IReadOnlyList<string> command)
    {
        if (string.IsNullOrWhiteSpace(serial))
        {
            return command;
        }

        return ["-s", serial, .. command];
    }

    private static string JoinCommand(IReadOnlyList<string> command)
    {
        return string.Join(' ', command.Select(QuoteForCommand));
    }

    private static string QuoteForCommand(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "\"\"";
        }

        return value.Contains(' ') || value.Contains('"')
            ? $"\"{value.Replace("\"", "\\\"", StringComparison.Ordinal)}\""
            : value;
    }
}
