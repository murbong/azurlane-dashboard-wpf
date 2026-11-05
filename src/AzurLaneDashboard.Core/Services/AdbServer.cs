using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;

namespace AzurLaneDashboard.Core.Services;

/// <summary>
/// Requests sent straight to the local adb server. The server looks for emulators (emulator-5554 on port 5555)
/// only when it starts, so an emulator that boots or restarts after it stays missing from `adb devices`. Android's
/// own emulator announces itself with host:emulator:&lt;port&gt; when it boots; MuMu does not, so this does it.
/// </summary>
public static partial class AdbServer
{
    private const int DefaultServerPort = 5037;

    /// <summary>The adb port behind an emulator serial (emulator-5554 → 5555); null for TCP and USB serials.</summary>
    public static int? EmulatorAdbPort(string? serial)
    {
        var match = EmulatorSerialRegex().Match(serial?.Trim() ?? "");
        return match.Success && int.TryParse(match.Groups[1].Value, out var console) && console is > 0 and < 65535
            ? console + 1
            : null;
    }

    /// <summary>
    /// Asks the adb server to attach the emulator listening on the port as emulator-(port - 1). Harmless when it is
    /// already attached. False when no adb server runs; the next adb command starts one, which scans for emulators.
    /// </summary>
    public static bool AnnounceEmulator(int adbPort)
    {
        try
        {
            using var client = new TcpClient();
            if (!client.ConnectAsync("127.0.0.1", ServerPort()).Wait(TimeSpan.FromSeconds(2))) return false;
            var request = $"host:emulator:{adbPort}";
            var stream = client.GetStream();
            stream.Write(Encoding.ASCII.GetBytes($"{request.Length:X4}{request}"));
            // The server sends no reply; wait for it to close the connection so the request is read before we go.
            stream.ReadTimeout = 1000;
            try
            {
                _ = stream.Read(new byte[16]);
            }
            catch (IOException)
            {
            }

            return true;
        }
        catch (Exception ex) when (ex is SocketException or IOException or AggregateException)
        {
            return false;
        }
    }

    private static int ServerPort() =>
        int.TryParse(Environment.GetEnvironmentVariable("ANDROID_ADB_SERVER_PORT"), out var port) && port is > 0 and <= 65535
            ? port
            : DefaultServerPort;

    [GeneratedRegex(@"^emulator-(\d+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex EmulatorSerialRegex();
}
