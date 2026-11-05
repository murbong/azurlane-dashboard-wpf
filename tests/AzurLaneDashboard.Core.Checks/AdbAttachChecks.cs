using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using AzurLaneDashboard.Capture;
using AzurLaneDashboard.Core.Services;

/// <summary>
/// Capture start finding the emulator: an emulator serial the adb server lost is announced to it, and a serial that
/// stays missing falls back to the only device adb lists. A fake adb.cmd and a fake adb server stand in for the
/// real ones, so the checks never touch the running adb server.
/// </summary>
internal static class AdbAttachChecks
{
    public static async Task RunAsync()
    {
        Ensure(AdbServer.EmulatorAdbPort("emulator-5554") == 5555 && AdbServer.EmulatorAdbPort(" emulator-5556 ") == 5557,
            "an emulator serial maps to the adb port next to its console port");
        Ensure(AdbServer.EmulatorAdbPort("127.0.0.1:16384") is null && AdbServer.EmulatorAdbPort("R58M123ABC") is null &&
               AdbServer.EmulatorAdbPort("") is null, "TCP and USB serials are not emulator serials");

        var folder = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"fake-adb-{Guid.NewGuid():N}")).FullName;
        var adb = Path.Combine(folder, "adb.cmd");
        // get-state answers "device" once a marker file says the serial is attached; devices lists 16384 the same way.
        File.WriteAllText(adb, string.Join("\r\n",
            "@echo off",
            "if \"%1\"==\"devices\" goto devices",
            "if \"%1\"==\"connect\" exit /b 0",
            "if \"%3\"==\"get-state\" goto state",
            "exit /b 1",
            ":devices",
            "echo List of devices attached",
            "if exist \"%~dp0online-16384\" echo 127.0.0.1:16384\tdevice",
            "echo emulator-5556\toffline",
            "exit /b 0",
            ":state",
            "if \"%2\"==\"emulator-5554\" if exist \"%~dp0online-5554\" goto online",
            "if \"%2\"==\"127.0.0.1:16384\" if exist \"%~dp0online-16384\" goto online",
            "echo error: device '%2' not found 1>&2",
            "exit /b 1",
            ":online",
            "echo device",
            "exit /b 0",
            ""));
        var previousPort = Environment.GetEnvironmentVariable("ANDROID_ADB_SERVER_PORT");
        try
        {
            await CheckAnnouncedEmulator(adb, folder);
            await CheckFallbacks(adb, folder);
        }
        finally
        {
            Environment.SetEnvironmentVariable("ANDROID_ADB_SERVER_PORT", previousPort);
            Directory.Delete(folder, recursive: true);
        }

        Console.WriteLine("ADB device attach checks passed.");
    }

    // The fake server attaches emulator-5554 when told host:emulator:5555, as the real one does.
    private static async Task CheckAnnouncedEmulator(string adb, string folder)
    {
        var server = new TcpListener(IPAddress.Loopback, 0);
        server.Start();
        Environment.SetEnvironmentVariable("ANDROID_ADB_SERVER_PORT", ((IPEndPoint)server.LocalEndpoint).Port.ToString());
        var request = Task.Run(async () =>
        {
            using var client = await server.AcceptTcpClientAsync();
            var buffer = new byte[64];
            var read = await client.GetStream().ReadAsync(buffer);
            var text = Encoding.ASCII.GetString(buffer, 0, read);
            if (text == "0012host:emulator:5555") File.WriteAllText(Path.Combine(folder, "online-5554"), "");
            return text;
        });
        try
        {
            var serial = await FindDevice(adb, "emulator-5554");
            Ensure(await request == "0012host:emulator:5555", "the adb server is told the emulator listens on 5555");
            Ensure(serial == "emulator-5554", "a lost emulator serial is attached again and kept");
        }
        finally
        {
            server.Stop();
        }
    }

    private static async Task CheckFallbacks(string adb, string folder)
    {
        // A port nothing listens on, so the announcement fails like it does with no adb server running.
        var closed = new TcpListener(IPAddress.Loopback, 0);
        closed.Start();
        Environment.SetEnvironmentVariable("ANDROID_ADB_SERVER_PORT", ((IPEndPoint)closed.LocalEndpoint).Port.ToString());
        closed.Stop();
        File.Delete(Path.Combine(folder, "online-5554"));

        File.WriteAllText(Path.Combine(folder, "online-16384"), "");
        Ensure(await FindDevice(adb, "emulator-5554") == "127.0.0.1:16384",
            "a serial that stays missing falls back to the only online device, skipping offline ones");

        File.Delete(Path.Combine(folder, "online-16384"));
        try
        {
            await FindDevice(adb, "emulator-5554");
            Ensure(false, "no device must stop the capture");
        }
        catch (InvalidOperationException error)
        {
            Ensure(error.Message.StartsWith("에뮬레이터를 찾지 못했습니다 (emulator-5554)", StringComparison.Ordinal),
                "with no device the error names the serial it looked for: " + error.Message);
        }
    }

    private static async Task<string> FindDevice(string adb, string serial)
    {
        var find = typeof(DumpcapCaptureService).GetMethod("FindDeviceAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
        try
        {
            return await (Task<string>)find.Invoke(new DumpcapCaptureService(), [adb, serial, false, CancellationToken.None])!;
        }
        catch (TargetInvocationException error) when (error.InnerException is not null)
        {
            throw error.InnerException;
        }
    }

    private static void Ensure(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
