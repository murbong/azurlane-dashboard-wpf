using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Channels;
using AzurLaneDashboard.Core.Configuration;
using AzurLaneDashboard.Core.Services;
using AzurLaneDashboard.Data.Services;
using AzurLaneDashboard.Protocol;
using SharpPcap;

namespace AzurLaneDashboard.Capture;

public sealed partial class DumpcapCaptureService
{
    private const int NativeCaptureQueueLimit = 8192;

    private readonly GamePayloadDecoder payloadDecoder = new();
    private Process? process;
    private ICaptureDevice? nativeDevice;
    private volatile AccountPacketRouter? router;
    private CancellationTokenSource? stopTokenSource;
    private volatile bool nativeRunning;
    private int packetsSeen;
    private int packetsDecoded;
    private int decodeFailures;
    private DateTimeOffset? startedAt;
    private DateTimeOffset? finishedAt;
    private int? exitCode;
    private string lastMessage = "";
    private readonly object logLock = new();
    private readonly List<string> logLines = new();
    private long logVersion;

    public CaptureStatus Status
    {
        get
        {
            var current = router;
            return new CaptureStatus(
                IsProcessRunning(process) || nativeRunning,
                startedAt,
                finishedAt,
                exitCode,
                packetsSeen,
                packetsDecoded,
                decodeFailures,
                lastMessage)
            {
                Account = current?.Account,
                AccountAssumed = current?.IsAccountAssumed ?? false,
                WaitingForLogin = current?.IsWaitingForLogin ?? false,
                LoggedInElsewhere = current?.LoggedInElsewhere ?? "",
                LikelyDuplicate = current?.LikelyDuplicate ?? false,
                ShipsUnmatched = current?.ShipsUnmatched ?? false,
                PendingPackets = current?.PendingCount ?? 0
            };
        }
    }

    public IReadOnlyList<string> Logs
    {
        get
        {
            lock (logLock)
            {
                return logLines.ToList();
            }
        }
    }

    public CaptureLogSnapshot GetLogSnapshot()
    {
        lock (logLock)
        {
            return new CaptureLogSnapshot(logVersion, logLines.ToArray());
        }
    }

    public async Task StartAsync(CaptureOptions options, CancellationToken cancellationToken = default)
    {
        if (IsProcessRunning(process) || nativeRunning)
        {
            throw new InvalidOperationException("capture is already running");
        }

        packetsSeen = 0;
        packetsDecoded = 0;
        decodeFailures = 0;
        startedAt = DateTimeOffset.Now;
        finishedAt = null;
        exitCode = null;
        lastMessage = "starting";
        router = null;
        ResetLogs();

        stopTokenSource?.Dispose();
        stopTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var token = stopTokenSource.Token;
        var backend = NormalizeBackend(options.Backend);

        if (backend == AppDefaults.CaptureBackendNpcap)
        {
            AppendLog("Starting npcap capture.");
            try
            {
                await RunNativePcapCaptureAsync(options, token).ConfigureAwait(false);
                exitCode = 0;
            }
            finally
            {
                StopNativeCapture();
                finishedAt = DateTimeOffset.Now;
                lastMessage = "stopped";
                AppendLog("Capture stopped.");
            }

            return;
        }

        string? serial = null;
        if (backend == AppDefaults.CaptureBackendAdb)
        {
            serial = await EnsureAdbRootAsync(options, token).ConfigureAwait(false);
        }

        var command = BuildCaptureCommand(options, serial);
        AppendLog($"Starting {backend} capture.");
        AppendLog(string.Join(' ', command.Select(QuoteIfNeeded)));
        process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = command[0],
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardErrorEncoding = Encoding.UTF8,
                UseShellExecute = false,
                CreateNoWindow = true
            },
            EnableRaisingEvents = true
        };
        foreach (var argument in command.Skip(1))
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        process.Start();
        _ = Task.Run(() => DrainStderrAsync(process, options.Verbose, token), CancellationToken.None);

        try
        {
            await RunDecodeLoopAsync(process.StandardOutput.BaseStream, options, backend, token).ConfigureAwait(false);
        }
        finally
        {
            StopProcess();
            if (process is not null)
            {
                await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
                exitCode = process.ExitCode;
                AppendLog($"Capture exited with code {exitCode}.");
                process.Dispose();
                process = null;
            }

            finishedAt = DateTimeOffset.Now;
            lastMessage = "stopped";
            AppendLog("Capture stopped.");
        }
    }

    public void Stop()
    {
        AppendLog("Stop requested.");
        stopTokenSource?.Cancel();
        StopProcess();
        StopNativeCapture();
    }

    private static bool IsProcessRunning(Process? candidate)
    {
        try
        {
            return candidate is not null && !candidate.HasExited;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    public static string ResolveDumpcapPath(string value)
    {
        var candidates = new[]
        {
            value,
            ExternalToolLocator.FindOnPath(value),
            ExternalToolLocator.FindOnPath(AppDefaults.DefaultDumpcapExecutable),
            @"C:\Program Files\Wireshark\dumpcap.exe",
            @"C:\Program Files (x86)\Wireshark\dumpcap.exe"
        };
        return candidates.FirstOrDefault(candidate => !string.IsNullOrWhiteSpace(candidate) && File.Exists(candidate))
               ?? (string.IsNullOrWhiteSpace(value) ? AppDefaults.DefaultDumpcapExecutable : value);
    }

    public static async IAsyncEnumerable<DumpcapInterface> ListInterfacesAsync(
        string dumpcapPath = AppDefaults.DefaultDumpcapExecutable,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using var proc = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = ResolveDumpcapPath(dumpcapPath),
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };
        proc.StartInfo.ArgumentList.Add("-D");
        proc.Start();
        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var line = await proc.StandardOutput.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                if (line is null)
                {
                    break;
                }

                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                var dot = line.IndexOf('.');
                if (dot > 0)
                {
                    yield return new DumpcapInterface(line[..dot].Trim(), line[(dot + 1)..].Trim());
                }
            }

            await proc.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            if (!proc.HasExited)
            {
                try
                {
                    proc.Kill(entireProcessTree: true);
                }
                catch
                {
                    // Best effort cleanup for a canceled interface scan.
                }
            }
        }
    }

    public static async IAsyncEnumerable<DumpcapInterface> ListNpcapInterfacesAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var devices = CaptureDeviceList.Instance;
        for (var index = 0; index < devices.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var device = devices[index];
            var name = string.IsNullOrWhiteSpace(device.Description)
                ? device.Name
                : device.Description;
            yield return new DumpcapInterface(device.Name, name);

            await Task.Yield();
        }
    }

}
