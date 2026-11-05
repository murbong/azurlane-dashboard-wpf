using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using AzurLaneDashboard.Core.Services;
using AzurLaneDashboard.Data.Services;

namespace AzurLaneDashboard.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private async Task<AndroidDeviceCheckResult> CheckAndroidDeviceReadyAsync(
        AndroidBridge bridge,
        CancellationToken cancellationToken)
    {
        if (!bridge.IsAvailable)
        {
            return new AndroidDeviceCheckResult(false, "ADB/MuMuManager를 찾지 못했습니다.");
        }

        // The adb server finds emulators only when it starts; one that restarted later has to be announced.
        if (bridge.Kind == AndroidBridgeKind.Adb && AdbServer.EmulatorAdbPort(Serial) is { } port &&
            AdbServer.AnnounceEmulator(port))
        {
            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
        }

        if (bridge.Kind == AndroidBridgeKind.Adb && ExternalToolLocator.IsTcpAdbSerial(Serial))
        {
            // MuMu's adb can block on `adb connect` even when the serial is already attached,
            // so only connect when the device is not reachable yet.
            var current = await GetAndroidDeviceStateAsync(bridge, cancellationToken).ConfigureAwait(false);
            if (current.IsReady)
            {
                return current;
            }

            try
            {
                var connect = await RunAndroidBridgeProcessAsync(
                    bridge,
                    ExternalToolLocator.BuildAdbConnectArguments(Serial),
                    cancellationToken).ConfigureAwait(false);
                if (connect.ExitCode != 0)
                {
                    var connectDetail = FirstNonEmpty(connect.StandardError.Trim(), connect.StandardOutput.Trim(),
                        $"exit code {connect.ExitCode.ToString(CultureInfo.InvariantCulture)}");
                    return new AndroidDeviceCheckResult(false,
                        $"{bridge.DisplayName} ADB 연결 실패: {connectDetail}");
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (TimeoutException)
            {
                // The ADB server may still finish attaching in the background; the state check below decides.
            }
            catch (Exception ex) when (ex is Win32Exception or IOException or InvalidOperationException)
            {
                return new AndroidDeviceCheckResult(false, $"{bridge.DisplayName} ADB 연결 실행 실패: {ex.Message}");
            }
        }

        return await GetAndroidDeviceStateAsync(bridge, cancellationToken).ConfigureAwait(false);
    }

    private async Task<AndroidDeviceCheckResult> GetAndroidDeviceStateAsync(
        AndroidBridge bridge,
        CancellationToken cancellationToken)
    {
        var arguments = ExternalToolLocator.BuildAndroidBridgeArguments(bridge, Serial, ["get-state"]);
        if (arguments.Count == 0)
        {
            return new AndroidDeviceCheckResult(false, "Android 도구 실행 인자를 만들지 못했습니다.");
        }

        try
        {
            var result = await RunAndroidBridgeProcessAsync(bridge, arguments, cancellationToken).ConfigureAwait(false);
            var stdout = result.StandardOutput.Trim();
            var stderr = result.StandardError.Trim();
            var stateLines = stdout.Split(
                ['\r', '\n'],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (result.ExitCode == 0 &&
                stateLines.Any(line => line.Equals("device", StringComparison.OrdinalIgnoreCase)))
            {
                return new AndroidDeviceCheckResult(true, "Android 장치가 준비되었습니다.");
            }

            var detail = FirstNonEmpty(stderr, stdout, $"exit code {result.ExitCode}");
            return new AndroidDeviceCheckResult(false, $"{bridge.DisplayName} 장치 상태가 준비되지 않았습니다. ({detail})");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is Win32Exception or IOException or InvalidOperationException or TimeoutException)
        {
            return new AndroidDeviceCheckResult(false, $"{bridge.DisplayName} 실행 실패: {ex.Message}");
        }
    }

    private static async Task<AndroidBridgeProcessResult> RunAndroidBridgeProcessAsync(
        AndroidBridge bridge,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = bridge.ExecutablePath,
            WorkingDirectory = ProjectLocator.FindProjectRoot(),
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process();
        process.StartInfo = startInfo;
        if (!process.Start())
        {
            throw new InvalidOperationException($"{bridge.DisplayName} 실행을 시작하지 못했습니다.");
        }

        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(6));
        try
        {
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            TryKillProcess(process);
            throw new TimeoutException($"{bridge.DisplayName} 응답 시간이 초과되었습니다.");
        }

        return new AndroidBridgeProcessResult(
            process.ExitCode,
            await stdoutTask.ConfigureAwait(false),
            await stderrTask.ConfigureAwait(false));
    }

    private static void TryKillProcess(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
        }
        catch (Win32Exception)
        {
        }
    }

    private static string FirstNonEmpty(params string[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return "";
    }
}
