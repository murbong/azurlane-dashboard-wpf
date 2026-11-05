using System.Diagnostics;
using AzurLaneDashboard.Core.Configuration;
using AzurLaneDashboard.Data.Services;
using SharpPcap;

namespace AzurLaneDashboard.Capture;

public sealed partial class DumpcapCaptureService
{
    private async Task DrainStderrAsync(Process proc, bool verbose, CancellationToken cancellationToken)
    {
        var buffer = new char[4096];
        while (!cancellationToken.IsCancellationRequested && !proc.HasExited)
        {
            var read = await proc.StandardError.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read <= 0)
            {
                break;
            }

            var text = new string(buffer, 0, read).Trim();
            if (verbose && !string.IsNullOrWhiteSpace(text))
            {
                lastMessage = text;
                AppendLog(text);
            }
        }
    }

    private void ResetLogs()
    {
        lock (logLock)
        {
            logLines.Clear();
            logVersion++;
        }
    }

    /// <summary>A captured packet changed an account's stored game state (raised on the capture thread).</summary>
    public event Action<GameAccount, StateApplyResult>? StateApplied;

    /// <summary>A packet decoded and stored for the first time (raised on the capture thread).</summary>
    public event Action<CapturedPacket>? PacketDecoded;

    /// <summary>
    /// A login named the account this capture now writes to: the previous one, if any, then the new one (raised on
    /// the capture thread).
    /// </summary>
    public event Action<GameAccount?, GameAccount>? AccountChanged;

    private void AppendLog(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        lock (logLock)
        {
            logLines.Add(text.Trim());
            if (logLines.Count > 500)
            {
                logLines.RemoveRange(0, logLines.Count - 500);
            }

            logVersion++;
        }
    }

    private static string NormalizeBackend(string? value)
    {
        if (string.Equals(value, AppDefaults.CaptureBackendAdb, StringComparison.OrdinalIgnoreCase))
        {
            return AppDefaults.CaptureBackendAdb;
        }

        if (string.Equals(value, AppDefaults.CaptureBackendNpcap, StringComparison.OrdinalIgnoreCase))
        {
            return AppDefaults.CaptureBackendNpcap;
        }

        return AppDefaults.CaptureBackendDumpcap;
    }

    private static string DeviceDisplayName(ICaptureDevice device)
    {
        return string.IsNullOrWhiteSpace(device.Description)
            ? device.Name
            : $"{device.Description} ({device.Name})";
    }

    private static string QuoteIfNeeded(string value)
    {
        return value.Contains(' ') ? $"\"{value}\"" : value;
    }

    private void StopProcess()
    {
        var candidate = process;
        if (!IsProcessRunning(candidate))
        {
            return;
        }

        try
        {
            candidate!.Kill(entireProcessTree: true);
        }
        catch
        {
            // Capture shutdown must not crash the UI.
        }
    }

    private void StopNativeCapture()
    {
        var device = nativeDevice;
        if (device is null)
        {
            return;
        }

        try
        {
            device.StopCapture();
        }
        catch
        {
            // Capture shutdown must not crash the UI.
        }
    }
}
