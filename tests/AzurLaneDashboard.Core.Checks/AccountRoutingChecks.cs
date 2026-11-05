using AzurLaneDashboard.Capture;
using AzurLaneDashboard.Data.Services;
using AzurLaneDashboard.Protocol;
using Microsoft.Data.Sqlite;

/// <summary>
/// Several accounts on several emulators in one data directory: each login sends its packets to that account's
/// own database, the database from before accounts were told apart keeps its path, a capture started after login
/// checks its guess with the ships it sees, and a login elsewhere stops the capture that held the account.
/// </summary>
internal static class AccountRoutingChecks
{
    private const long MainUid = 100000001;
    private const long AltUid = 300000001;
    private const long NewUid = 400000001;
    private static int index;

    public static void Run()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"azurlane-accounts-check-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        try
        {
            var legacyPath = Path.Combine(folder, "inventory.sqlite");
            WriteLegacyDatabase(legacyPath);
            var registry = AccountRegistry.ForDataDirectory(folder);

            CheckServerNameDecoding();
            CheckLegacyRegistration(registry, legacyPath);
            CheckLoginOnNewEmulator(registry, folder);
            CheckGuessConfirmedByShips(registry, legacyPath);
            CheckSwapWhileNotCapturing(registry);
            CheckUnknownShipsWaitForLogin(registry);
            CheckLoginElsewhereStopsTheOldCapture(registry, legacyPath);
            CheckSwitchOnOneEmulator(registry);
            CheckSameEmulatorUnderTwoSerials(registry);
            CheckSameEmulatorStartedAfterLogin(registry);
            CheckRegistryFile(folder);
            Console.WriteLine("Multi-account routing checks passed.");
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            try
            {
                Directory.Delete(folder, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }

    // The old single database: one account logged in, captured before accounts.json existed.
    private static void WriteLegacyDatabase(string path)
    {
        using var store = new LivePacketStore(path);
        // Captured before the server list was read as CP949: the name is lost, the server number is not.
        store.AppendDecoded("check", "g", "SC", 10021, 0, """{"result":0,"serverlist":[{"ids":[3],"name":"�산도"}]}""");
        store.AppendDecoded("check", "s", "CS", 10022, 0, """{"account_id":339391,"serverid":3}""");
        store.AppendDecoded("check", "s", "SC", 11003, 1, Profile(MainUid, "TestAdmiral", 143));
        store.AppendDecoded("check", "s", "SC", 12001, 2, Ships(1001, 1002, 1003));
    }

    // The KR gateway names servers in CP949; the game's own text is UTF-8.
    private static void CheckServerNameDecoding()
    {
        Ensure(ProtobufReader.DecodeString([0xC7, 0xD1, 0xBB, 0xEA, 0xB5, 0xB5]) == "한산도",
            "a server name that is not UTF-8 is read as CP949");
        Ensure(ProtobufReader.DecodeString(System.Text.Encoding.UTF8.GetBytes("부계정 TestAdmiral")) == "부계정 TestAdmiral",
            "UTF-8 text stays UTF-8");
    }

    private static void CheckLegacyRegistration(AccountRegistry registry, string legacyPath)
    {
        var account = registry.RegisterDatabase(legacyPath, "emulator-5554");
        Ensure(account is { Uid: MainUid, Name: "TestAdmiral", Level: 143 },
            "the existing database registers under the uid, name and level of its own profile");
        Ensure(account is { ServerId: 3, ServerName: "", ServerLabel: "서버 3" },
            "its last login gives the server; a name stored broken is left out and the number shown instead");
        Ensure(string.Equals(account!.DatabasePath, Path.GetFullPath(legacyPath), StringComparison.OrdinalIgnoreCase),
            "the existing database keeps its path: " + account.DatabasePath);
        Ensure(registry.RegisterDatabase(legacyPath)?.Uid == MainUid && registry.Accounts.Count == 1,
            "registering the same file twice does not add a second account");
    }

    private static void CheckLoginOnNewEmulator(AccountRegistry registry, string folder)
    {
        using var router = new AccountPacketRouter(registry, "emulator-5556");
        Ensure(router is { Account: null, IsWaitingForLogin: true },
            "an emulator that never logged in waits for a login instead of writing to some account");

        Route(router, "SC", 11000, """{"timestamp":1793431383}""");
        Ensure(router.PendingCount == 1, "packets before the login wait in memory");
        Route(router, "SC", 10021, """{"result":0,"serverlist":[{"ids":[1],"name":"제1서버"},{"ids":[3],"name":"한산도"}]}""");
        Route(router, "CS", 10022, """{"account_id":339391,"serverid":3}""");
        Route(router, "SC", 10023, $$"""{"result":0,"user_id":{{AltUid}}}""");
        Ensure(router.Account?.Uid == AltUid && !router.IsWaitingForLogin && router.PendingCount == 0,
            "SC 10023 names the account and releases the held packets");
        Route(router, "SC", 11003, Profile(AltUid, "부계정", 52));
        Route(router, "SC", 12001, Ships(2001, 2002, 2003));

        var alt = registry.Find(AltUid);
        var expected = Path.Combine(folder, AccountRegistry.AccountsDirectoryName, AltUid.ToString(),
            "inventory.sqlite");
        Ensure(alt is { Name: "부계정", Level: 52, ServerId: 3, ServerName: "한산도", LastCaptureTarget: "emulator-5556" },
            "the login records the account's name, level, server with its name from the gateway list, and emulator");
        Ensure(string.Equals(alt!.DatabasePath, expected, StringComparison.OrdinalIgnoreCase) && File.Exists(expected),
            "a new account gets a database of its own: " + alt.DatabasePath);
        Ensure(Count(expected) == 6 && Text(expected, "SELECT value_json FROM current_commander_profile WHERE key = 'id'") == AltUid.ToString(),
            "every packet of the login, including the held one, lands in the new account's database");
    }

    // Started while the game was logged in: the emulator's last account is only a guess until ships confirm it.
    private static void CheckGuessConfirmedByShips(AccountRegistry registry, string legacyPath)
    {
        using var router = new AccountPacketRouter(registry, "emulator-5554");
        Ensure(router is { Account.Uid: MainUid, IsAccountAssumed: true, IsWaitingForLogin: true },
            "a capture started after login guesses the emulator's last account but does not write yet");
        var before = Count(legacyPath);
        Route(router, "SC", 11000, """{"timestamp":1793431999}""");
        Ensure(Count(legacyPath) == before && router.PendingCount == 1, "nothing is written on a guess");
        // A ship built while nothing was captured is in no database; most of the fleet still decides.
        Route(router, "CS", 40001, Sortie(1001, 1002, 9999));
        Ensure(router is { Account.Uid: MainUid, IsAccountAssumed: false, IsWaitingForLogin: false },
            "the sortie's ships confirm the guess");
        Ensure(Count(legacyPath) == before + 2, "and the held packets are written to that account");
    }

    // The accounts were swapped between emulators while nothing was captured.
    private static void CheckSwapWhileNotCapturing(AccountRegistry registry)
    {
        var altPath = registry.Find(AltUid)!.DatabasePath;
        using var router = new AccountPacketRouter(registry, "emulator-5554");
        GameAccount? left = new();
        GameAccount? changed = null;
        router.AccountChanged += (previous, account) => (left, changed) = (previous, account);
        Ensure(router.Account?.Uid == MainUid, "the emulator still guesses its last account");
        var before = Count(altPath);
        Route(router, "CS", 12102, """{"id":1,"ship_list":[2001,2002]}""");
        Ensure(router is { Account.Uid: AltUid, IsWaitingForLogin: false } && changed?.Uid == AltUid && left is null,
            "the fleet's ships belong to the other account, so the capture writes there instead");
        Ensure(Count(altPath) == before + 1, "into that account's database");
        Ensure(registry.LastOn("emulator-5554")?.Uid == AltUid,
            "and the emulator now remembers that account for the next guess");
    }

    private static void CheckUnknownShipsWaitForLogin(AccountRegistry registry)
    {
        using var router = new AccountPacketRouter(registry, "emulator-5556");
        Ensure(router.Account is null, "the account that left this emulator is not guessed for it");
        Route(router, "CS", 40001, Sortie(7001, 7002));
        Ensure(router is { ShipsUnmatched: true, IsWaitingForLogin: true, Account: null, PendingCount: 1 },
            "ships of no known account hold the packets: it may be a new account");
        Route(router, "CS", 10022, """{"account_id":5,"serverid":3}""");
        Route(router, "SC", 10023, $$"""{"result":0,"user_id":{{NewUid}}}""");
        Ensure(router is { Account.Uid: NewUid, ShipsUnmatched: false, IsWaitingForLogin: false },
            "a login names the new account");
        Ensure(Count(registry.Find(NewUid)!.DatabasePath) == 3, "and its database gets the held packets");

        Route(router, "CS", 10022, """{"account_id":1,"serverid":3}""");
        Route(router, "SC", 10023, """{"result":1,"user_id":0}""");
        Ensure(router.IsWaitingForLogin && router.Account?.Uid == NewUid,
            "a failed login names no account and keeps holding packets");
    }

    // The game keeps one session per account: logging it in on another emulator ends the session here.
    private static void CheckLoginElsewhereStopsTheOldCapture(AccountRegistry registry, string legacyPath)
    {
        using var first = new AccountPacketRouter(registry, "emulator-5554");
        using var second = new AccountPacketRouter(registry, "emulator-5556");
        Login(first, MainUid);
        Ensure(first is { Account.Uid: MainUid, IsWaitingForLogin: false }, "the first emulator writes the account");
        Login(second, MainUid);
        var before = Count(legacyPath);
        Route(first, "SC", 11000, """{"timestamp":1793500001}""");
        Ensure(Count(legacyPath) == before && first.IsWaitingForLogin && first.LoggedInElsewhere == "emulator-5556" &&
               !first.LikelyDuplicate,
            "once the account logs in on the other emulator, the old capture stops writing it");
        Route(second, "SC", 11000, """{"timestamp":1793500002}""");
        Ensure(Count(legacyPath) == before + 1 && !second.IsWaitingForLogin, "the new one writes it");

        GameAccount? left = new();
        first.AccountChanged += (previous, _) => left = previous;
        Login(first, AltUid);
        Ensure(first is { Account.Uid: AltUid, IsWaitingForLogin: false, LoggedInElsewhere: "" } && left is null,
            "the old emulator logging in to the other account writes that one, without having 'left' the first");
    }

    private static void CheckSwitchOnOneEmulator(AccountRegistry registry)
    {
        using var router = new AccountPacketRouter(registry, "emulator-5558");
        GameAccount? left = null;
        GameAccount? changed = null;
        var applied = new List<long>();
        router.AccountChanged += (previous, account) => (left, changed) = (previous, account);
        router.StateApplied += (account, _) => applied.Add(account.Uid);
        Login(router, MainUid);
        applied.Clear();
        Login(router, NewUid);
        Ensure(changed?.Uid == NewUid && left?.Uid == MainUid,
            "logging in to another account on the same emulator switches the database and says which one it left");
        Ensure(applied.Count > 0 && applied.All(uid => uid == NewUid),
            "state changes report the account they were written to");
    }

    // MuMu lists one instance as emulator-5554 and 127.0.0.1:16384; both captures see the same packets.
    private static void CheckSameEmulatorUnderTwoSerials(AccountRegistry registry)
    {
        using var first = new AccountPacketRouter(registry, "emulator-5560");
        using var second = new AccountPacketRouter(registry, "127.0.0.1:16480");
        Both(first, second, "CS", 10022, """{"account_id":339391,"serverid":3}""");
        Both(first, second, "SC", 10023, $$"""{"result":0,"user_id":{{AltUid}}}""");
        Both(first, second, "SC", 11003, Profile(AltUid, "부계정", 53));
        var altPath = registry.Find(AltUid)!.DatabasePath;
        for (var tick = 0; tick < AccountPacketRouter.DuplicateAfterPackets + 5; tick++)
        {
            Both(first, second, "SC", 11000, $$"""{"timestamp":{{1793600000 + tick}}}""");
        }

        Ensure(Scalar(altPath, "SELECT COUNT(*) FROM decoded_packets WHERE json LIKE '%17936000%'") ==
               AccountPacketRouter.DuplicateAfterPackets + 5,
            "the same packets seen by both captures are written once");
        Ensure(first is { LikelyDuplicate: true, LoggedInElsewhere: "127.0.0.1:16480" } && !second.IsWaitingForLogin,
            "the capture that keeps getting packets after the login elsewhere is flagged as the same emulator");
    }

    // Both serials started while the game was logged in: no login to order them, so the ships decide, and the
    // second capture to name the account finds the first already writing it.
    private static void CheckSameEmulatorStartedAfterLogin(AccountRegistry registry)
    {
        using var first = new AccountPacketRouter(registry, "emulator-5562");
        using var second = new AccountPacketRouter(registry, "127.0.0.1:16512");
        var altPath = registry.Find(AltUid)!.DatabasePath;
        Both(first, second, "SC", 11000, """{"timestamp":1793700000}""");
        Both(first, second, "CS", 40001, Sortie(2001, 2002, 2003));
        for (var tick = 1; tick <= 5; tick++)
        {
            Both(first, second, "SC", 11000, $$"""{"timestamp":{{1793700000 + tick}}}""");
        }

        Ensure(first is { Account.Uid: AltUid, IsWaitingForLogin: false, LikelyDuplicate: false },
            "the first capture to see the ships writes the account");
        Ensure(second is { LikelyDuplicate: true, IsWaitingForLogin: true, PendingCount: 0 },
            "the second finds it already written and keeps nothing");
        Ensure(Scalar(altPath, "SELECT COUNT(*) FROM decoded_packets WHERE json LIKE '%17937000%'") == 6,
            "each packet is written once");
    }

    private static void CheckRegistryFile(string folder)
    {
        var text = File.ReadAllText(Path.Combine(folder, AccountRegistry.FileName));
        Ensure(text.Contains(AltUid.ToString(), StringComparison.Ordinal) &&
               text.Contains(MainUid.ToString(), StringComparison.Ordinal) &&
               text.Contains("부계정", StringComparison.Ordinal) &&
               !text.Contains("DisplayName", StringComparison.Ordinal),
            "accounts.json lists the accounts with readable names");
    }

    private static void Login(AccountPacketRouter router, long uid)
    {
        Route(router, "CS", 10022, """{"account_id":339391,"serverid":3}""");
        Route(router, "SC", 10023, $$"""{"result":0,"user_id":{{uid}}}""");
        Route(router, "SC", 11003, Profile(uid, "", 0));
    }

    private static void Route(AccountPacketRouter router, string direction, int packetId, string json) =>
        router.Route(new GamePacket("check", "stream", direction, packetId, index++, [1, 2, 3]), true, json, null);

    // One packet seen by two captures of the same emulator: the same stream and frame index.
    private static void Both(AccountPacketRouter first, AccountPacketRouter second, string direction, int packetId,
        string json)
    {
        var packet = new GamePacket("check", "stream", direction, packetId, index++, [1, 2, 3]);
        first.Route(packet, true, json, null);
        second.Route(packet, true, json, null);
    }

    private static string Profile(long uid, string name, int level) => name.Length == 0
        ? $$"""{"resource_list":[],"id":{{uid}}}"""
        : $$"""{"resource_list":[],"id":{{uid}},"name":"{{name}}","level":{{level}}}""";

    private static string Ships(params long[] uids) =>
        "{\"shiplist\":[" + string.Join(",", uids.Select(uid => $"{{\"id\":{uid},\"template_id\":101011}}")) + "]}";

    private static string Sortie(params long[] uids) =>
        "{\"system\":51,\"ship_id_list\":[" + string.Join(",", uids) + "],\"data\":1,\"data2\":[],\"other_ship_id_list\":[]}";

    private static long Count(string path) => Scalar(path, "SELECT COUNT(*) FROM decoded_packets");

    private static long Scalar(string path, string sql) => Convert.ToInt64(Query(path, sql) ?? -1L);

    private static string Text(string path, string sql) => Convert.ToString(Query(path, sql)) ?? "";

    private static object? Query(string path, string sql)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false
        }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar();
    }

    private static void Ensure(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
