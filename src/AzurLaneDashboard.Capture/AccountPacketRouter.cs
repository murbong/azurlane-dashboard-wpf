using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using AzurLaneDashboard.Data.Services;
using AzurLaneDashboard.Protocol;

namespace AzurLaneDashboard.Capture;

/// <summary>
/// Sends one capture's packets to the database of the account logged in on it. A login (CS 10022, then SC 10023
/// with the user id, then SC 11003 with the profile) names the account. Packets wait in memory until an account is
/// certain, then go to its database in their original order:
/// <list type="bullet">
/// <item>while a login is in flight;</item>
/// <item>when the capture started after login: the account this emulator saw last is only a guess (the accounts may
/// have been swapped while nothing was captured), so it waits for the first sortie, fleet, gear or commission packet
/// and checks its ship uids, which are unique per account, against every account's ships;</item>
/// <item>when no account is known at all.</item>
/// </list>
/// The game keeps one session per account, so when the account logs in on another capture this one stops writing
/// until its emulator logs in again.
/// </summary>
public sealed class AccountPacketRouter : IDisposable
{
    public const int MaxPendingPackets = 20_000;

    // Packets still arriving after the account logged in elsewhere: a session the game ended sends nothing, so
    // this many means the other capture is the same emulator under another serial.
    public const int DuplicateAfterPackets = 30;

    private readonly AccountRegistry registry;
    private readonly string captureTarget;
    private readonly Action<string> log;
    private readonly Queue<PendingPacket> pending = new();
    private readonly ConcurrentDictionary<long, AccountLogin> loginsElsewhere = new();
    private LivePacketStore? store;
    private long storeUid;
    private bool loginPending;
    private bool loginAnnounced;
    private long boundSequence;
    private long releaseLoggedSequence;
    private int packetsAfterRelease;
    private int? loginServerId;
    private string? loginServerName;
    // The gateway's server list (SC 10021), which arrives just before the game login names its server.
    private Dictionary<int, string> serverNames = new();
    private bool overflowLogged;
    private volatile string duplicateOf = "";

    public AccountPacketRouter(AccountRegistry registry, string captureTarget, Action<string>? log = null)
    {
        this.registry = registry;
        this.captureTarget = captureTarget;
        this.log = log ?? (_ => { });
        registry.LoggedIn += OnLoggedIn;
        Account = registry.LastOn(captureTarget);
        IsAccountAssumed = Account is not null;
        this.log(Account is null
            ? "계정 확인 대기: 게임에 로그인하면 그 계정의 DB에 기록합니다."
            : $"이 에뮬레이터의 마지막 계정은 {Account.DisplayName}(UID {Uid(Account)})입니다. 계정이 바뀌었을 수 있어 출격·편성·장비·의뢰 패킷의 함선으로 확인한 뒤 기록합니다.");
    }

    /// <summary>A packet was stored in an account's database and changed its state (capture thread).</summary>
    public event Action<GameAccount, StateApplyResult>? StateApplied;

    /// <summary>A packet decoded and stored for the first time (capture thread).</summary>
    public event Action<CapturedPacket>? PacketDecoded;

    /// <summary>
    /// The capture now writes to a different account (capture thread). The first is the account it left when a
    /// login on this emulator switched accounts; null when ships named the account instead.
    /// </summary>
    public event Action<GameAccount?, GameAccount>? AccountChanged;

    /// <summary>The account written to, or the one guessed while <see cref="IsAccountAssumed"/>.</summary>
    public GameAccount? Account { get; private set; }

    /// <summary>The account is this emulator's last login, not yet confirmed by a login or by ships.</summary>
    public bool IsAccountAssumed { get; private set; }

    /// <summary>The ships of a sortie or fleet packet belong to no account seen so far.</summary>
    public bool ShipsUnmatched { get; private set; }

    /// <summary>Nothing is written yet: packets wait in memory, or are dropped after a login elsewhere.</summary>
    public bool IsWaitingForLogin => IsHolding || LoggedInElsewhere.Length > 0 || duplicateOf.Length > 0;

    /// <summary>The capture where this capture's account logged in after it; empty when none did.</summary>
    public string LoggedInElsewhere => Superseding()?.CaptureTarget ?? "";

    /// <summary>
    /// The same emulator captured twice: packets keep coming after the account logged in elsewhere, or the ships
    /// named an account another capture is writing (the game keeps one session per account).
    /// </summary>
    public bool LikelyDuplicate =>
        duplicateOf.Length > 0 || LoggedInElsewhere.Length > 0 && packetsAfterRelease >= DuplicateAfterPackets;

    public int PendingCount => pending.Count;

    private bool IsHolding => Account is null || loginPending || IsAccountAssumed;

    /// <summary>
    /// One packet from the capture: <paramref name="json"/> is its decoded body when it is a known packet that
    /// decoded, <paramref name="decodeError"/> the reason when it did not.
    /// </summary>
    public void Route(GamePacket packet, bool knownPacket, string? json, string? decodeError)
    {
        if (json is not null) ObserveLogin(packet, json);
        if (!loginPending && duplicateOf.Length > 0) return;
        if (!loginPending && Superseding() is { } login)
        {
            if (IsAccountAssumed)
            {
                // The guess logged in on another emulator, so it is not this one's account.
                log($"{Account!.DisplayName}이 {login.CaptureTarget}에서 로그인해 이 에뮬레이터의 계정이 아닙니다. 함선이나 로그인으로 다시 확인합니다.");
                Account = null;
                IsAccountAssumed = false;
            }
            else
            {
                // The game ended this session; anything still arriving is the other capture's packets seen twice.
                var released = Account!;
                if (releaseLoggedSequence != login.Sequence)
                {
                    releaseLoggedSequence = login.Sequence;
                    log($"{released.DisplayName}이 {login.CaptureTarget}에서 로그인해 이 캡처는 다음 로그인을 기다립니다.");
                }

                packetsAfterRelease++;
                registry.ClearWriter(released.Uid, captureTarget);
                return;
            }
        }

        if (json is not null && IsHolding && !loginPending) IdentifyByShips(packet, json);
        if (duplicateOf.Length > 0)
        {
            // Copies of what the other capture already writes, held ones included.
            pending.Clear();
            return;
        }

        var item = new PendingPacket(packet, knownPacket, json, decodeError);
        if (IsHolding)
        {
            Enqueue(item);
            return;
        }

        FlushPending();
        Write(item);
    }

    public void Dispose()
    {
        registry.LoggedIn -= OnLoggedIn;
        CloseStore();
        pending.Clear();
    }

    // Any capture thread: keep the newest login per account seen on other captures.
    private void OnLoggedIn(AccountLogin login)
    {
        if (string.Equals(login.CaptureTarget, captureTarget, StringComparison.OrdinalIgnoreCase)) return;
        loginsElsewhere.AddOrUpdate(login.Uid, login,
            (_, known) => login.Sequence > known.Sequence ? login : known);
    }

    // A login of this capture's account on another capture after this capture's own login (or guess).
    private AccountLogin? Superseding() =>
        Account is { } account && loginsElsewhere.TryGetValue(account.Uid, out var login) &&
        login.Sequence > Interlocked.Read(ref boundSequence)
            ? login
            : null;

    private void ObserveLogin(GamePacket packet, string json)
    {
        switch (packet.Direction, packet.PacketId)
        {
            case ("SC", 10021):
                var names = AccountRegistry.ServerNames(json);
                if (names.Count > 0) serverNames = names;
                break;
            case ("CS", 10022):
                // A new game login: its account is unknown until the server answers, so hold everything until then.
                loginPending = true;
                loginAnnounced = false;
                loginServerId = ReadInt(json, "serverid");
                loginServerName = loginServerId is { } server ? serverNames.GetValueOrDefault(server) : null;
                break;
            case ("SC", 10023) when ReadInt(json, "result") is null or 0 && ReadLong(json, "user_id") is > 0 and var uid:
                Bind(uid, null, null);
                break;
            case ("SC", 11003) when ReadLong(json, "id") is > 0 and var id:
                Bind(id, ReadString(json, "name"), ReadInt(json, "level"));
                break;
        }
    }

    private void Bind(long uid, string? name, int? level)
    {
        var previous = Account;
        // Only an account this emulator was really playing is one it left; a guess, or an account that already
        // moved to another emulator, was not.
        var left = IsAccountAssumed || Superseding() is not null ? null : previous;
        var account = registry.Observe(uid, captureTarget, loginServerId, name, level, loginServerName);
        if (!loginAnnounced || previous?.Uid != uid)
        {
            // Numbered across captures: whichever saw this account log in last is the one that writes it.
            Interlocked.Exchange(ref boundSequence, registry.AnnounceLogin(uid, captureTarget).Sequence);
            loginAnnounced = true;
        }

        Account = account;
        IsAccountAssumed = false;
        ShipsUnmatched = false;
        duplicateOf = "";
        loginPending = false;
        packetsAfterRelease = 0;
        if (previous?.Uid == uid)
        {
            return;
        }

        log($"계정 로그인: {account.DisplayName} (UID {Uid(account)}) → {account.DatabasePath}");
        AccountChanged?.Invoke(left, account);
    }

    // Without a login, the ships this emulator sends out tell whose session it is.
    private void IdentifyByShips(GamePacket packet, string json)
    {
        var ships = OwnShipUids(packet, json);
        if (ships.Count == 0) return;
        var guess = Account;
        GameAccount? best = null;
        var bestCount = 0;
        foreach (var candidate in registry.Accounts.OrderByDescending(item => item.Uid == guess?.Uid))
        {
            var count = AccountRegistry.CountOwnedShips(candidate.DatabasePath, ships);
            if (count > bestCount)
            {
                best = candidate;
                bestCount = count;
            }
        }

        // Most of them, not all: a ship built while nothing was captured is in no database yet.
        if (best is null || bestCount * 2 < ships.Count)
        {
            if (!ShipsUnmatched)
            {
                log($"함선 {ships.Count}척이 아는 계정의 함선과 맞지 않습니다. 새 계정이면 게임에서 다시 로그인하세요.");
            }

            ShipsUnmatched = true;
            Account = null;
            IsAccountAssumed = false;
            return;
        }

        ShipsUnmatched = false;
        if (registry.WriterOf(best.Uid) is { } writer &&
            !string.Equals(writer, captureTarget, StringComparison.OrdinalIgnoreCase))
        {
            // The game lets an account play in one place, so another capture writing it sees this same emulator.
            Account = best;
            IsAccountAssumed = false;
            duplicateOf = writer;
            log($"{best.DisplayName}은 {writer} 캡처가 이미 기록 중입니다. 같은 에뮬레이터를 두 시리얼로 잡았다면 하나를 목록에서 빼세요.");
            return;
        }

        // Remembered as this emulator's account, so the next capture started mid-session guesses right.
        Account = registry.Observe(best.Uid, captureTarget);
        IsAccountAssumed = false;
        if (guess?.Uid == best.Uid)
        {
            log($"함선 {bestCount}척으로 {best.DisplayName} 계정을 확인했습니다.");
            return;
        }

        log($"함선이 {best.DisplayName} 계정 것이라 {best.DisplayName} DB에 기록합니다 (UID {Uid(best)}).");
        AccountChanged?.Invoke(null, best);
    }

    // The player's own ships in client requests: sortie, fleet edit, commission, gear change and lesson.
    private static List<long> OwnShipUids(GamePacket packet, string json)
    {
        if (packet.Direction != "CS") return [];
        var field = packet.PacketId switch
        {
            40001 or 13003 => "ship_id_list",
            12102 => "ship_list",
            12006 or 22201 => "ship_id",
            _ => null
        };
        if (field is null || ReadProperty(json, field) is not { } value) return [];
        var values = value.ValueKind == JsonValueKind.Array ? value.EnumerateArray().ToList() : [value];
        return values.Where(item => item.ValueKind == JsonValueKind.Number && item.TryGetInt64(out var uid) && uid > 0)
            .Select(item => item.GetInt64())
            .Distinct()
            .ToList();
    }

    private void Enqueue(PendingPacket item)
    {
        pending.Enqueue(item);
        if (pending.Count <= MaxPendingPackets) return;
        pending.Dequeue();
        if (overflowLogged) return;
        overflowLogged = true;
        log($"계정을 모르는 패킷이 {MaxPendingPackets:N0}개를 넘어 오래된 것부터 버립니다. 게임에서 다시 로그인하세요.");
    }

    private void FlushPending()
    {
        if (pending.Count == 0) return;
        var count = pending.Count;
        while (pending.TryDequeue(out var item)) Write(item);
        overflowLogged = false;
        // A login itself holds back a handful (10022 and the gateway); only a longer wait is worth a line.
        if (count > 20) log($"계정 확인 전에 받은 패킷 {count:N0}개를 {Account?.DisplayName} DB에 기록했습니다.");
    }

    private void Write(PendingPacket item)
    {
        var account = Account!;
        var target = StoreFor(account);
        var packet = item.Packet;
        target.ObservePacket(packet.Source, packet.StreamId, packet.Direction, packet.PacketId, packet.Index,
            packet.Payload, item.Known);
        if (!item.Known) return;

        if (item.Json is null)
        {
            target.AppendDecodeFailure(packet.Source, packet.StreamId, packet.Direction, packet.PacketId,
                packet.Index, packet.Payload, "decode_error", item.DecodeError ?? "");
            return;
        }

        bool added;
        try
        {
            added = target.AppendDecoded(packet.Source, packet.StreamId, packet.Direction, packet.PacketId,
                packet.Index, item.Json);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            target.AppendDecodeFailure(packet.Source, packet.StreamId, packet.Direction, packet.PacketId,
                packet.Index, packet.Payload, "decode_error", ex.Message);
            return;
        }

        if (added)
        {
            PacketDecoded?.Invoke(new CapturedPacket(packet.PacketId, packet.Direction, item.Json, account.Uid));
        }
    }

    private LivePacketStore StoreFor(GameAccount account)
    {
        if (store is not null && storeUid == account.Uid) return store;
        CloseStore();
        var opened = new LivePacketStore(account.DatabasePath);
        var uid = account.Uid;
        registry.SetWriter(uid, captureTarget);
        opened.StateApplied += result => StateApplied?.Invoke(registry.Find(uid) ?? account, result);
        store = opened;
        storeUid = uid;
        return opened;
    }

    private void CloseStore()
    {
        if (store is not null) registry.ClearWriter(storeUid, captureTarget);
        store?.Dispose();
        store = null;
        storeUid = 0;
    }

    private static string Uid(GameAccount account) => account.Uid.ToString(CultureInfo.InvariantCulture);

    private static long? ReadLong(string json, string name)
    {
        var value = ReadProperty(json, name);
        return value is { ValueKind: JsonValueKind.Number } number && number.TryGetInt64(out var parsed) ? parsed : null;
    }

    private static int? ReadInt(string json, string name)
    {
        var value = ReadLong(json, name);
        return value is >= int.MinValue and <= int.MaxValue ? (int)value.Value : null;
    }

    private static string? ReadString(string json, string name) =>
        ReadProperty(json, name) is { ValueKind: JsonValueKind.String } text ? text.GetString() : null;

    private static JsonElement? ReadProperty(string json, string name)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.ValueKind == JsonValueKind.Object &&
                   document.RootElement.TryGetProperty(name, out var value)
                ? value.Clone()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed record PendingPacket(GamePacket Packet, bool Known, string? Json, string? DecodeError);
}
