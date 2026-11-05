using System.Buffers.Binary;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using AzurLaneDashboard.Core.Services;

namespace AzurLaneDashboard.Automation.Device;

/// <summary>
/// An emulator or phone reached through adb.exe: screencap for the screen and input for touches. Emulators such as
/// MuMu 12 run apps on their own virtual display, so both go to the display that shows Azur Lane.
/// </summary>
public sealed partial class AdbDevice(string adbPath, string serial) : IAutomationDevice
{
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(20);
    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    public string AdbPath { get; } = adbPath;
    public string Serial { get; } = serial;

    /// <summary>The display input and screenshots go to; null uses the device's default display.</summary>
    public GameDisplay? Display { get; private set; }

    /// <summary>
    /// Connects a TCP serial (127.0.0.1:16384), finds the display showing the game and measures it from a
    /// screenshot (wm size reports the default display, which may hold the launcher instead).
    /// </summary>
    public DeviceProbe Probe()
    {
        try
        {
            if (Serial.Contains(':', StringComparison.Ordinal))
            {
                Run($"connect {Serial}", withSerial: false);
            }
            else if (AdbServer.EmulatorAdbPort(Serial) is { } port && !IsOnline())
            {
                // The adb server finds emulators only when it starts; one that restarted later has to be announced.
                AdbServer.AnnounceEmulator(port);
                for (var attempt = 0; attempt < 6 && !IsOnline(); attempt++) Thread.Sleep(500);
            }

            var display = ResolveGameDisplay();
            var (width, height) = PngSize(Capture());
            return new DeviceProbe(true, width, height, "", display);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or TimeoutException)
        {
            return new DeviceProbe(false, 0, 0, ex.Message, null);
        }
    }

    /// <summary>
    /// The display whose resumed activity is Azur Lane (dumpsys activity), with the physical id screencap needs
    /// (dumpsys display). Null when the game is not running; the default display is used until then.
    /// </summary>
    public GameDisplay? ResolveGameDisplay()
    {
        Display = FindGameDisplay(Encoding.UTF8.GetString(Run("shell dumpsys activity activities")),
            () => Encoding.UTF8.GetString(Run("shell dumpsys display")));
        return Display;
    }

    /// <summary>Parses the dumpsys output; the display list is read only when the game is not on display 0.</summary>
    public static GameDisplay? FindGameDisplay(string activities, Func<string> displays)
    {
        int? logical = null;
        var current = -1;
        foreach (var line in activities.Split('\n'))
        {
            var header = DisplayHeaderRegex().Match(line);
            if (header.Success)
            {
                current = int.Parse(header.Groups["id"].Value, CultureInfo.InvariantCulture);
                continue;
            }

            if (current >= 0 && line.Contains("topResumedActivity=", StringComparison.Ordinal) && IsGameActivity(line))
            {
                logical = current;
                break;
            }
        }

        if (logical is null) return null;
        if (logical == 0) return new GameDisplay(0, "", "");
        var device = DisplayDeviceRegex().Matches(displays())
            .FirstOrDefault(match => match.Groups["logical"].Value == logical.Value.ToString(CultureInfo.InvariantCulture));
        return device is null
            ? null
            : new GameDisplay(logical.Value, device.Groups["physical"].Value, device.Groups["name"].Value.Trim());
    }

    public byte[] Capture() =>
        StripToPng(Run(Display is { PhysicalId.Length: > 0 } display
            ? $"exec-out screencap -p -d {display.PhysicalId}"
            : "exec-out screencap -p"));

    public void Tap(int x, int y, int durationMs) =>
        Run(string.Create(CultureInfo.InvariantCulture, $"shell input{DisplayArgument} swipe {x} {y} {x} {y} {durationMs}"));

    public void Swipe(int x1, int y1, int x2, int y2, int durationMs) =>
        Run(string.Create(CultureInfo.InvariantCulture, $"shell input{DisplayArgument} swipe {x1} {y1} {x2} {y2} {durationMs}"));

    public void Back() => Run($"shell input{DisplayArgument} keyevent 4");

    private string DisplayArgument => Display is { LogicalId: > 0 } display
        ? string.Create(CultureInfo.InvariantCulture, $" -d {display.LogicalId}")
        : "";

    /// <summary>
    /// The PNG inside screencap's output. Emulators with several displays (MuMu 12) print a "[Warning] Multiple
    /// displays were found" line on stdout before the image, which a decoder cannot read past.
    /// </summary>
    public static byte[] StripToPng(byte[] output)
    {
        var start = output.AsSpan().IndexOf(PngSignature);
        return start <= 0 ? output : output[start..];
    }

    // Package or activity of every Azur Lane client: kr.txwy.and.blhx, com.YoStarEN.AzurLane, com.bilibili.azurlane …
    public static bool IsGameActivity(string line) =>
        line.Contains("azurlane", StringComparison.OrdinalIgnoreCase) || line.Contains("blhx", StringComparison.OrdinalIgnoreCase);

    private static (int Width, int Height) PngSize(byte[] png) =>
        png.Length >= 24 && png.AsSpan(0, 8).SequenceEqual(PngSignature)
            ? (BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16, 4)), BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20, 4)))
            : (0, 0);

    private bool IsOnline()
    {
        try
        {
            return Encoding.UTF8.GetString(Run("get-state")).Trim() == "device";
        }
        catch (Exception ex) when (ex is InvalidOperationException or TimeoutException)
        {
            return false;
        }
    }

    private byte[] Run(string arguments, bool withSerial = true)
    {
        var start = new ProcessStartInfo
        {
            FileName = AdbPath,
            Arguments = withSerial ? $"-s {Serial} {arguments}" : arguments,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        using var process = Process.Start(start) ?? throw new InvalidOperationException("adb를 실행하지 못했습니다.");
        using var output = new MemoryStream();
        var copy = process.StandardOutput.BaseStream.CopyToAsync(output);
        var error = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(CommandTimeout))
        {
            try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            throw new TimeoutException($"adb {arguments} 응답이 {CommandTimeout.TotalSeconds:0}초 안에 오지 않았습니다.");
        }

        copy.Wait(CommandTimeout);
        if (process.ExitCode != 0)
        {
            var message = error.Wait(CommandTimeout) ? error.Result.Trim() : "";
            throw new InvalidOperationException(message.Length > 0 ? message : $"adb {arguments} 실패 (코드 {process.ExitCode})");
        }

        return output.ToArray();
    }

    [GeneratedRegex(@"^Display #(?<id>\d+)", RegexOptions.CultureInvariant)]
    private static partial Regex DisplayHeaderRegex();

    // dumpsys display: "mDisplayId=2" then "mPrimaryDisplayDevice=mumuscreen001(local:4619827767814508545)".
    [GeneratedRegex(@"mDisplayId=(?<logical>\d+)\s*\r?\n\s*mPrimaryDisplayDevice=(?<name>[^\r\n]*?)\(local:(?<physical>\d+)\)",
        RegexOptions.CultureInvariant)]
    private static partial Regex DisplayDeviceRegex();
}

/// <summary>The display showing the game: the logical id for input, the physical id for screencap.</summary>
public sealed record GameDisplay(int LogicalId, string PhysicalId, string Name)
{
    public string Text => LogicalId == 0
        ? "기본 화면"
        : Name.Length > 0 ? $"디스플레이 {LogicalId} ({Name})" : $"디스플레이 {LogicalId}";
}

public sealed record DeviceProbe(bool Connected, int Width, int Height, string Error, GameDisplay? Display)
{
    public bool IsExpectedSize => Width == 1920 && Height == 1080 || Width == 1080 && Height == 1920;
    public bool GameFound => Display is not null;
    /// <summary>Long side first: emulators often report the landscape game screen as 1080x1920.</summary>
    public string SizeText => $"{Math.Max(Width, Height)}×{Math.Min(Width, Height)}";
}
