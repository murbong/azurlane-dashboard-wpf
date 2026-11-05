using AzurLaneDashboard.Core.Configuration;
using AzurLaneDashboard.Data.Services;

namespace AzurLaneDashboard.Capture;

public sealed class CaptureOptions
{
    /// <summary>The directory whose accounts.json names each account's database (see <see cref="AccountRegistry"/>).</summary>
    public string DataDirectory { get; init; } = "";

    /// <summary>
    /// What this capture listens to: the ADB serial for the adb backend, otherwise the host interface. Remembered
    /// with each login so a capture started mid-session knows whose packets these are.
    /// </summary>
    public string CaptureTarget { get; init; } = "";

    public string Backend { get; init; } = AppDefaults.CaptureBackendDumpcap;
    public string AdbPath { get; init; } = AppDefaults.DefaultAdbExecutable;
    public string Serial { get; init; } = AppDefaults.DefaultAdbSerial;

    /// <summary>
    /// Fail rather than fall back to another emulator when the serial is missing: with several emulators captured
    /// at once, a fallback would capture one of them twice.
    /// </summary>
    public bool RequireExactSerial { get; init; }

    public string AdbInterface { get; init; } = AppDefaults.DefaultAdbInterface;
    public string DumpcapPath { get; init; } = AppDefaults.DefaultDumpcapExecutable;
    public string CaptureInterface { get; init; } = AppDefaults.DefaultCaptureInterface;
    public int ServerPort { get; init; } = AppDefaults.DefaultServerPort;
    public string ServerHost { get; init; } = "";
    public int StatusIntervalSeconds { get; init; } = AppDefaults.DefaultCaptureStatusInterval;
    public bool Verbose { get; init; }
}

public sealed record CaptureStatus(
    bool Running,
    DateTimeOffset? StartedAt,
    DateTimeOffset? FinishedAt,
    int? ExitCode,
    int PacketsSeen,
    int PacketsDecoded,
    int DecodeFailures,
    string LastMessage)
{
    /// <summary>The account this capture writes to; null until a login names one.</summary>
    public GameAccount? Account { get; init; }

    /// <summary>The account is the target's last login, not yet confirmed by a login or by ships.</summary>
    public bool AccountAssumed { get; init; }

    /// <summary>Nothing is written: packets wait for a login or ships to name their account.</summary>
    public bool WaitingForLogin { get; init; }

    /// <summary>The capture where this capture's account logged in since; empty when none did.</summary>
    public string LoggedInElsewhere { get; init; } = "";

    /// <summary>Packets keep coming after the account logged in elsewhere: one emulator captured twice.</summary>
    public bool LikelyDuplicate { get; init; }

    /// <summary>The ships this emulator sent out belong to no account seen so far.</summary>
    public bool ShipsUnmatched { get; init; }

    public int PendingPackets { get; init; }
}

/// <summary>
/// A decoded game packet: "CS" from the client or "SC" from the server, with its body as JSON, and the uid of the
/// account whose database stored it.
/// </summary>
public sealed record CapturedPacket(int PacketId, string Direction, string Json, long AccountUid = 0);

public sealed record CaptureLogSnapshot(long Version, IReadOnlyList<string> Lines);

public sealed record DumpcapInterface(string Id, string Name);
