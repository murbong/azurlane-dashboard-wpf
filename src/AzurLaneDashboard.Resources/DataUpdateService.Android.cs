using System.Collections;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using AzurLaneDashboard.Core.Configuration;
using AzurLaneDashboard.Core.Services;
using AzurLaneDashboard.Data.Services;

namespace AzurLaneDashboard.Resources;

public sealed partial class DataUpdateService
{
    private static async Task<bool> AdbPullFileAsync(
        UpdateContext context,
        AndroidBridge bridge,
        string remotePath,
        string localPath,
        IList<string> logs,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(localPath) ?? context.ProjectRoot);
        var result = await RunAndroidBridgeCaptureAsync(
            context,
            bridge,
            ["pull", remotePath, localPath],
            logs,
            false,
            cancellationToken).ConfigureAwait(false);
        return result.ExitCode == 0;
    }

    private static async Task EnsureAndroidBridgeConnectedAsync(
        UpdateContext context,
        AndroidBridge bridge,
        IList<string> logs,
        CancellationToken cancellationToken)
    {
        // The adb server finds emulators only when it starts; one that restarted later has to be announced.
        if (bridge.Kind == AndroidBridgeKind.Adb && AdbServer.EmulatorAdbPort(context.Serial) is { } port &&
            AdbServer.AnnounceEmulator(port))
        {
            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
            return;
        }

        if (bridge.Kind != AndroidBridgeKind.Adb || !ExternalToolLocator.IsTcpAdbSerial(context.Serial))
        {
            return;
        }

        // MuMu's adb can block on `adb connect` even when the serial is already attached,
        // so skip it when the device already answers.
        var state = await RunProcessCaptureAsync(
            bridge.ExecutablePath,
            ExternalToolLocator.BuildAndroidBridgeArguments(bridge, context.Serial, ["get-state"]),
            context.ProjectRoot,
            logs,
            false,
            cancellationToken).ConfigureAwait(false);
        if (state.ExitCode == 0 && state.StandardOutput.Trim().Equals("device", StringComparison.OrdinalIgnoreCase))
        {
            logs.Add($"adb {context.Serial}: 이미 연결됨");
            return;
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        try
        {
            var result = await RunProcessCaptureAsync(
                bridge.ExecutablePath,
                ExternalToolLocator.BuildAdbConnectArguments(context.Serial),
                context.ProjectRoot,
                logs,
                false,
                timeout.Token).ConfigureAwait(false);
            var detail = FirstNonEmpty(result.StandardOutput.Trim(), result.StandardError.Trim(),
                $"exit code {result.ExitCode.ToString(CultureInfo.InvariantCulture)}");
            logs.Add($"adb connect {context.Serial}: {detail}");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // The ADB server may still finish attaching in the background; later adb calls will report failures.
            logs.Add($"adb connect {context.Serial}: 응답 시간 초과, 연결 상태로 계속 진행");
        }
    }

    private static Task<ProcessResult> RunAndroidBridgeCaptureAsync(
        UpdateContext context,
        AndroidBridge bridge,
        IReadOnlyList<string> command,
        IList<string> logs,
        bool logOutput,
        CancellationToken cancellationToken)
    {
        var arguments = ExternalToolLocator.BuildAndroidBridgeArguments(bridge, context.Serial, command);
        return RunProcessCaptureAsync(
            bridge.ExecutablePath,
            arguments,
            context.ProjectRoot,
            logs,
            logOutput,
            cancellationToken);
    }

    private static async Task RunProcessAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        IList<string> logs,
        CancellationToken cancellationToken)
    {
        var result = await RunProcessCaptureAsync(
            fileName,
            arguments,
            workingDirectory,
            logs,
            true,
            cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"{fileName} exited with code {result.ExitCode.ToString(CultureInfo.InvariantCulture)}");
        }
    }

    private static async Task<ProcessResult> RunProcessCaptureAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        IList<string> logs,
        bool logOutput,
        CancellationToken cancellationToken)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };
        foreach (var argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        logs.Add($"> {fileName} {string.Join(' ', arguments.Select(QuoteIfNeeded))}");
        try
        {
            process.Start();
        }
        catch (Win32Exception ex)
        {
            return new ProcessResult(9009, "", ex.Message);
        }

        using var cancellationRegistration = cancellationToken.Register(() => TryKillProcess(process));
        var stdoutTask = ReadOutputAsync(process.StandardOutput, "stdout");
        var stderrTask = ReadOutputAsync(process.StandardError, "stderr");
        async Task<string> ReadOutputAsync(TextReader reader, string streamName)
        {
            try
            {
                return await ReadToEndLimitedAsync(reader, streamName, logOutput, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch
            {
                // A faulted reader no longer drains its pipe. Stop the producer immediately,
                // otherwise WhenAll waits forever for a process blocked on a full pipe.
                TryKillProcess(process);
                throw;
            }
        }
        string stdout;
        string stderr;
        try
        {
            await Task.WhenAll(process.WaitForExitAsync(cancellationToken), stdoutTask, stderrTask)
                .ConfigureAwait(false);
            stdout = await stdoutTask.ConfigureAwait(false);
            stderr = await stderrTask.ConfigureAwait(false);
        }
        catch (ProcessOutputLimitExceededException ex)
        {
            TryKillProcess(process);
            try
            {
                await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (InvalidOperationException)
            {
            }

            logs.Add(
                $"{fileName} {ex.StreamName} exceeded {MaxProcessOutputChars.ToString(CultureInfo.InvariantCulture)} characters; process stopped");
            return new ProcessResult(124, ex.PartialText, ex.Message);
        }

        if (logOutput)
        {
            foreach (var line in SplitLines(stdout))
            {
                logs.Add(line);
            }

            foreach (var line in SplitLines(stderr))
            {
                logs.Add(line);
            }
        }
        else if (process.ExitCode != 0 && !string.IsNullOrWhiteSpace(stderr))
        {
            logs.Add(stderr.Trim());
        }

        return new ProcessResult(process.ExitCode, stdout, stderr);
    }

    private static async Task<string> ReadToEndLimitedAsync(
        TextReader reader,
        string streamName,
        bool truncateOutput,
        CancellationToken cancellationToken)
    {
        var builder = new StringBuilder();
        var buffer = new char[4096];
        var truncated = false;
        while (true)
        {
            var read = await reader.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)
                .ConfigureAwait(false);
            if (read == 0)
            {
                return truncated
                    ? builder + $"\n[{streamName} log truncated at {MaxProcessOutputChars} characters]"
                    : builder.ToString();
            }

            if (builder.Length + read > MaxProcessOutputChars)
            {
                var remaining = Math.Max(0, MaxProcessOutputChars - builder.Length);
                if (remaining > 0)
                {
                    builder.Append(buffer, 0, remaining);
                }

                if (truncateOutput)
                {
                    // Human-readable logs can be bounded, but the pipe must still be drained.
                    truncated = true;
                    continue;
                }
                throw new ProcessOutputLimitExceededException(
                    streamName,
                    builder.ToString(),
                    $"{streamName} output exceeded {MaxProcessOutputChars.ToString(CultureInfo.InvariantCulture)} characters");
            }

            builder.Append(buffer, 0, read);
        }
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
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
        {
        }
    }
}
