using System.Globalization;
using System.Threading.Channels;
using AzurLaneDashboard.Core.Configuration;
using AzurLaneDashboard.Data.Services;
using AzurLaneDashboard.Protocol;
using SharpPcap;

namespace AzurLaneDashboard.Capture;

public sealed partial class DumpcapCaptureService
{
    private async Task RunDecodeLoopAsync(Stream pcapStream, CaptureOptions options,
        string backend,
        CancellationToken cancellationToken)
    {
        using var accountRouter = OpenRouter(options);
        var pcap = new PcapStreamReader(pcapStream);
        var decoder = new LiveTcpGameDecoder(options.ServerPort, HostFilters(options.ServerHost),
            SourceName(backend, options));
        await foreach (var frame in pcap.ReadFramesAsync(cancellationToken).ConfigureAwait(false))
        {
            ProcessFrame(frame, decoder, accountRouter);
        }
    }

    private async Task RunNativePcapCaptureAsync(CaptureOptions options, CancellationToken cancellationToken)
    {
        using var accountRouter = OpenRouter(options);
        var decoder = new LiveTcpGameDecoder(options.ServerPort, HostFilters(options.ServerHost),
            SourceName(AppDefaults.CaptureBackendNpcap, options));
        var frames = Channel.CreateBounded<PcapFrame>(new BoundedChannelOptions(NativeCaptureQueueLimit)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.DropOldest
        });
        var device = ResolveNpcapDevice(options.CaptureInterface);
        nativeDevice = device;
        nativeRunning = true;
        lastMessage = "capturing";

        void OnPacketArrival(object sender, PacketCapture capture)
        {
            try
            {
                var packet = capture.GetPacket();
                var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000d;
                frames.Writer.TryWrite(new PcapFrame(timestamp, (int)packet.LinkLayerType, packet.Data.ToArray()));
            }
            catch (ObjectDisposedException)
            {
            }
            catch (InvalidOperationException)
            {
            }
        }

        try
        {
            device.OnPacketArrival += OnPacketArrival;
            device.Open(DeviceModes.Promiscuous, 1000);
            var filter = BuildTcpdumpFilter(options.ServerPort, HostFilters(options.ServerHost));
            device.Filter = filter;
            AppendLog($"Npcap interface: {DeviceDisplayName(device)}");
            AppendLog($"BPF filter: {filter}");
            device.StartCapture();

            while (await frames.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
            {
                while (frames.Reader.TryRead(out var frame))
                {
                    ProcessFrame(frame, decoder, accountRouter);
                }
            }
        }
        finally
        {
            frames.Writer.TryComplete();
            try
            {
                device.StopCapture();
            }
            catch
            {
                // Capture shutdown must not crash the UI.
            }

            device.OnPacketArrival -= OnPacketArrival;
            if (device is IDisposable disposable)
            {
                disposable.Dispose();
            }

            if (ReferenceEquals(nativeDevice, device))
            {
                nativeDevice = null;
            }

            nativeRunning = false;
        }
    }

    private void ProcessFrame(PcapFrame frame, LiveTcpGameDecoder decoder, AccountPacketRouter accountRouter)
    {
        foreach (var packet in decoder.FeedFrame(frame))
        {
            packetsSeen++;
            var known = PacketIds.KnownPacketIds.Contains(packet.PacketId);
            string? json = null;
            string? error = null;
            if (known)
            {
                try
                {
                    json = payloadDecoder.DecodeToJson(packet);
                    packetsDecoded++;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    error = ex.Message;
                    decodeFailures++;
                }
            }

            accountRouter.Route(packet, known, json, error);
        }
    }

    // The account router for one run; its events are the service's, so the UI subscribes once per service.
    private AccountPacketRouter OpenRouter(CaptureOptions options)
    {
        var registry = AccountRegistry.ForDataDirectory(options.DataDirectory);
        var opened = new AccountPacketRouter(registry, options.CaptureTarget, AppendLog);
        opened.StateApplied += (account, result) => StateApplied?.Invoke(account, result);
        opened.PacketDecoded += packet => PacketDecoded?.Invoke(packet);
        opened.AccountChanged += (previous, account) => AccountChanged?.Invoke(previous, account);
        router = opened;
        return opened;
    }

    // Stored with each packet; names the emulator so two captures' rows can be told apart.
    private static string SourceName(string backend, CaptureOptions options) =>
        backend == AppDefaults.CaptureBackendAdb
            ? $"live:adb:{(string.IsNullOrWhiteSpace(options.Serial) ? "default" : options.Serial.Trim())}"
            : $"live:{backend}:{options.CaptureInterface}";

    private static ICaptureDevice ResolveNpcapDevice(string captureInterface)
    {
        var devices = CaptureDeviceList.Instance;
        if (devices.Count == 0)
        {
            throw new InvalidOperationException("Npcap capture interface was not found. Install Npcap and restart the app.");
        }

        var value = string.IsNullOrWhiteSpace(captureInterface)
            ? AppDefaults.DefaultCaptureInterface
            : captureInterface.Trim();
        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var oneBasedIndex) &&
            oneBasedIndex >= 1 &&
            oneBasedIndex <= devices.Count)
        {
            return devices[oneBasedIndex - 1];
        }

        foreach (var device in devices)
        {
            if (string.Equals(device.Name, value, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(device.Description, value, StringComparison.OrdinalIgnoreCase))
            {
                return device;
            }
        }

        throw new InvalidOperationException($"Npcap capture interface not found: {value}");
    }

}
