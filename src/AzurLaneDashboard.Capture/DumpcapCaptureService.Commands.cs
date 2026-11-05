using System.Diagnostics;
using System.Text;
using AzurLaneDashboard.Core.Configuration;
using AzurLaneDashboard.Core.Services;

namespace AzurLaneDashboard.Capture;

public sealed partial class DumpcapCaptureService
{
    // serial: the device found at start, which may differ from the configured one (see FindDeviceAsync).
    private static List<string> BuildCaptureCommand(CaptureOptions options, string? serial = null)
    {
        return NormalizeBackend(options.Backend) == AppDefaults.CaptureBackendAdb
            ? BuildAdbCommand(options, serial)
            : BuildDumpcapCommand(options);
    }

    private static List<string> BuildDumpcapCommand(CaptureOptions options)
    {
        var filter = BuildTcpdumpFilter(options.ServerPort, HostFilters(options.ServerHost));
        return
        [
            ResolveDumpcapPath(options.DumpcapPath),
            "-i",
            string.IsNullOrWhiteSpace(options.CaptureInterface)
                ? AppDefaults.DefaultCaptureInterface
                : options.CaptureInterface,
            "-f",
            filter,
            "-s",
            "0",
            "-P",
            "-w",
            "-"
        ];
    }

    private static List<string> BuildAdbCommand(CaptureOptions options, string? serial = null)
    {
        var filter = BuildTcpdumpFilter(options.ServerPort, HostFilters(options.ServerHost));
        var command = new List<string>
        {
            ResolveAdbExecutable(options)
        };
        serial ??= options.Serial;
        if (!string.IsNullOrWhiteSpace(serial))
        {
            command.AddRange(["-s", serial.Trim()]);
        }

        command.AddRange(
        [
            "exec-out",
            "sh",
            "-c",
            "exec tcpdump \"$@\" 2>/dev/null",
            "tcpdump",
            "-i",
            string.IsNullOrWhiteSpace(options.AdbInterface) ? AppDefaults.DefaultAdbInterface : options.AdbInterface,
            "-s",
            "0",
            "-U",
            "-w",
            "-",
            filter
        ]);
        return command;
    }

    // Returns the serial the capture runs on (see FindDeviceAsync).
    private async Task<string> EnsureAdbRootAsync(CaptureOptions options, CancellationToken cancellationToken)
    {
        var adb = ResolveAdbExecutable(options);
        var serial = await FindDeviceAsync(adb, options.Serial, options.RequireExactSerial, cancellationToken)
            .ConfigureAwait(false);
        var prefix = serial.Length == 0
            ? Array.Empty<string>()
            : new[] { "-s", serial };
        var root = await RunAdbControlAsync(
            adb, [.. prefix, "root"], cancellationToken).ConfigureAwait(false);
        if (root.ExitCode != 0 ||
            root.Output.Contains("cannot run as root", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Android tcpdump requires root ADB. {root.Output.Trim()}");
        }

        // `adb root` restarts adbd, which drops the transport; reattach best-effort and let wait-for-device decide.
        if (serial.Length > 0 && !root.Output.Contains("already running as root", StringComparison.OrdinalIgnoreCase))
        {
            await AttachSerialAsync(adb, serial, cancellationToken).ConfigureAwait(false);
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        try
        {
            var ready = await RunAdbControlAsync(
                adb, [.. prefix, "wait-for-device"], timeout.Token).ConfigureAwait(false);
            if (ready.ExitCode != 0)
            {
                throw new InvalidOperationException($"ADB device did not reconnect. {ready.Output.Trim()}");
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new InvalidOperationException("ADB device did not reconnect after enabling root.");
        }

        return serial;
    }

    // The configured serial once adb lists it. When it stays missing, the only device adb lists, else MuMu's default
    // port, so a stale serial in settings does not stop the capture; the log says which one was used. With several
    // emulators captured at once (exact) a fallback could land on one already captured, so it fails instead.
    private async Task<string> FindDeviceAsync(string adb, string? configured, bool exact,
        CancellationToken cancellationToken)
    {
        var serial = configured?.Trim() ?? "";
        if (serial.Length == 0 || await AttachSerialAsync(adb, serial, cancellationToken).ConfigureAwait(false))
        {
            return serial;
        }

        var online = await OnlineDevicesAsync(adb, cancellationToken).ConfigureAwait(false);
        if (exact)
        {
            throw new InvalidOperationException(online.Count == 0
                ? $"에뮬레이터 {serial}을 찾지 못했습니다. 에뮬레이터가 켜져 있는지 확인하세요."
                : $"에뮬레이터 {serial}이 adb에 보이지 않습니다. 연결된 기기: {string.Join(", ", online)}.");
        }

        if (online.Count == 1)
        {
            AppendLog($"ADB serial {serial} is not attached; using {online[0]}, the only device adb lists.");
            return online[0];
        }

        var fallback = AppDefaults.DefaultAdbSerial;
        if (!online.Contains(fallback, StringComparer.OrdinalIgnoreCase) &&
            !fallback.Equals(serial, StringComparison.OrdinalIgnoreCase) &&
            await AttachSerialAsync(adb, fallback, cancellationToken).ConfigureAwait(false))
        {
            AppendLog($"ADB serial {serial} is not attached; using {fallback}, MuMu's default port.");
            return fallback;
        }

        throw new InvalidOperationException(online.Count == 0
            ? $"에뮬레이터를 찾지 못했습니다 ({serial}). 에뮬레이터가 켜져 있는지, 설정의 ADB 시리얼이 맞는지 확인하세요."
            : $"설정한 ADB 시리얼 {serial}이 보이지 않습니다. adb에 연결된 기기: {string.Join(", ", online)}. 설정에서 시리얼을 맞춰 주세요.");
    }

    // True once adb lists the serial as a device. A TCP serial (MuMu's 127.0.0.1:16384) needs `adb connect`, which the
    // server forgets whenever it restarts. An emulator serial (emulator-5554) is found by the server only when it
    // starts, so an emulator that restarted later is announced to it; either way the server attaches it in the
    // background, so this waits a few seconds for it.
    private static async Task<bool> AttachSerialAsync(string adb, string serial, CancellationToken cancellationToken)
    {
        // MuMu's adb can block on `adb connect` even when the serial is already attached, so check first.
        if (await IsAdbDeviceOnlineAsync(adb, serial, cancellationToken).ConfigureAwait(false))
        {
            return true;
        }

        if (ExternalToolLocator.IsTcpAdbSerial(serial))
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            try
            {
                await RunAdbControlAsync(adb, ExternalToolLocator.BuildAdbConnectArguments(serial), timeout.Token)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // The server may still finish attaching in the background; the state checks below decide.
            }
        }
        else if (AdbServer.EmulatorAdbPort(serial) is { } port)
        {
            AdbServer.AnnounceEmulator(port);
        }
        else
        {
            return false;
        }

        for (var attempt = 0; attempt < 10; attempt++)
        {
            if (await IsAdbDeviceOnlineAsync(adb, serial, cancellationToken).ConfigureAwait(false))
            {
                return true;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken).ConfigureAwait(false);
        }

        return false;
    }

    /// <summary>The serials `adb devices` lists as ready, for picking the emulators to capture.</summary>
    public static async Task<IReadOnlyList<string>> ListAdbDevicesAsync(string adbPath,
        CancellationToken cancellationToken = default)
    {
        var adb = ExternalToolLocator.ResolveAdbPath(adbPath) ??
                  (string.IsNullOrWhiteSpace(adbPath) ? AppDefaults.DefaultAdbExecutable : adbPath);
        return await OnlineDevicesAsync(adb, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<List<string>> OnlineDevicesAsync(string adb, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        try
        {
            var devices = await RunAdbControlAsync(adb, ["devices"], timeout.Token).ConfigureAwait(false);
            return devices.ExitCode == 0 ? ParseOnlineDevices(devices.Output) : [];
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return [];
        }
    }

    // `adb devices`: a header, then "serial<TAB>state" per device; only "device" can run commands.
    private static List<string> ParseOnlineDevices(string output) =>
        output.Split('\n')
            .Select(line => line.Trim().Split('\t'))
            .Where(parts => parts.Length == 2 && parts[1].Trim().Equals("device", StringComparison.OrdinalIgnoreCase))
            .Select(parts => parts[0].Trim())
            .ToList();

    private static async Task<bool> IsAdbDeviceOnlineAsync(string adb, string serial, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        try
        {
            var state = await RunAdbControlAsync(adb, ["-s", serial, "get-state"], timeout.Token).ConfigureAwait(false);
            return state.ExitCode == 0 && state.Output.Trim().Equals("device", StringComparison.OrdinalIgnoreCase);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }

    private static string ResolveAdbExecutable(CaptureOptions options) =>
        ExternalToolLocator.ResolveAdbPath(options.AdbPath) ??
        (string.IsNullOrWhiteSpace(options.AdbPath) ? AppDefaults.DefaultAdbExecutable : options.AdbPath);

    private static async Task<(int ExitCode, string Output)> RunAdbControlAsync(
        string executable,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = executable,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };
        foreach (var argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        process.Start();
        var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }

            throw;
        }

        return (process.ExitCode, $"{await stdout.ConfigureAwait(false)}{await stderr.ConfigureAwait(false)}");
    }

    private static string BuildTcpdumpFilter(int serverPort, IReadOnlyCollection<string> hosts)
    {
        var expr = serverPort <= 0 ? "tcp" : $"tcp port {serverPort}";
        if (hosts.Count == 0)
        {
            return expr;
        }

        var hostExpr = string.Join(" or ", hosts.Select(host => $"host {host}"));
        return $"({hostExpr}) and {expr}";
    }

    private static string[] HostFilters(string value)
    {
        return (value ?? "")
            .Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }
}
