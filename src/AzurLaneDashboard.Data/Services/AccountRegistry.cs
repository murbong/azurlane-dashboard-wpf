using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Data.Sqlite;

namespace AzurLaneDashboard.Data.Services;

/// <summary>A game account the dashboard has seen log in, and the database that holds its state.</summary>
public sealed record GameAccount
{
    /// <summary>The game's user id: SC 10023 user_id, the same number as SC 11003 id.</summary>
    public long Uid { get; init; }

    public string Name { get; init; } = "";
    public int Level { get; init; }

    /// <summary>CS 10022 serverid of the last login; 0 when no login was seen.</summary>
    public int ServerId { get; init; }

    /// <summary>That server's name from the gateway's list (SC 10021); empty until a login carried a readable one.</summary>
    public string ServerName { get; init; } = "";

    public string DatabasePath { get; init; } = "";

    /// <summary>The capture that saw the last login: an ADB serial, or the host capture's key.</summary>
    public string LastCaptureTarget { get; init; } = "";

    public DateTimeOffset? LastSeenAt { get; init; }

    [JsonIgnore]
    public string DisplayName => Name.Length > 0 ? Name : $"UID {Uid.ToString(CultureInfo.InvariantCulture)}";

    /// <summary>The server's name, its number while the name is unknown, or empty when neither is.</summary>
    [JsonIgnore]
    public string ServerLabel => ServerName.Length > 0
        ? ServerName
        : ServerId > 0 ? $"서버 {ServerId.ToString(CultureInfo.InvariantCulture)}" : "";
}

/// <summary>A login one capture saw; <see cref="Sequence"/> orders logins across every capture.</summary>
public sealed record AccountLogin(long Uid, string CaptureTarget, long Sequence);

/// <summary>
/// The accounts seen in one data directory, saved as accounts.json beside their databases. Each account keeps its
/// own SQLite file: a database registered from before multi-account support stays where it is, and a new account
/// gets accounts/&lt;uid&gt;/inventory.sqlite. Capture threads and the UI share one instance per directory.
/// </summary>
public sealed class AccountRegistry
{
    public const string FileName = "accounts.json";
    public const string AccountsDirectoryName = "accounts";

    private static readonly Dictionary<string, AccountRegistry> Instances = new(StringComparer.OrdinalIgnoreCase);
    // Account names stay readable Korean in the file instead of escaped code points.
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private readonly object gate = new();
    private readonly List<GameAccount> accounts;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<long, string> writers = new();
    private long loginSequence;

    private AccountRegistry(string dataDirectory)
    {
        DataDirectory = dataDirectory;
        RegistryPath = Path.Combine(dataDirectory, FileName);
        accounts = Read(RegistryPath);
    }

    /// <summary>Raised after the list changed, on the thread that changed it.</summary>
    public event Action? Changed;

    /// <summary>
    /// A capture saw a login, raised on its thread. The game keeps one session per account, so the same account on
    /// any other capture was logged out by it.
    /// </summary>
    public event Action<AccountLogin>? LoggedIn;

    public string DataDirectory { get; }
    public string RegistryPath { get; }

    public static AccountRegistry ForDataDirectory(string dataDirectory)
    {
        var full = Path.GetFullPath(string.IsNullOrWhiteSpace(dataDirectory)
            ? ProjectLocator.DefaultDataDirectory()
            : dataDirectory);
        lock (Instances)
        {
            if (!Instances.TryGetValue(full, out var registry))
            {
                registry = new AccountRegistry(full);
                Instances[full] = registry;
            }

            return registry;
        }
    }

    /// <summary>In the order they were first seen, so a list of them does not reshuffle at every login.</summary>
    public IReadOnlyList<GameAccount> Accounts
    {
        get
        {
            lock (gate)
            {
                return accounts.ToList();
            }
        }
    }

    public GameAccount? MostRecent()
    {
        lock (gate)
        {
            return accounts.MaxBy(account => account.LastSeenAt ?? DateTimeOffset.MinValue);
        }
    }

    public GameAccount? Find(long uid)
    {
        lock (gate)
        {
            return accounts.FirstOrDefault(account => account.Uid == uid);
        }
    }

    public GameAccount? FindByDatabasePath(string databasePath)
    {
        if (string.IsNullOrWhiteSpace(databasePath)) return null;
        var full = Path.GetFullPath(databasePath);
        lock (gate)
        {
            return accounts.FirstOrDefault(account => SamePath(account.DatabasePath, full));
        }
    }

    /// <summary>The account whose last login this capture target saw, for a capture started mid-session.</summary>
    public GameAccount? LastOn(string captureTarget)
    {
        if (string.IsNullOrWhiteSpace(captureTarget)) return null;
        lock (gate)
        {
            return accounts
                .Where(account => string.Equals(account.LastCaptureTarget, captureTarget, StringComparison.OrdinalIgnoreCase))
                .MaxBy(account => account.LastSeenAt ?? DateTimeOffset.MinValue);
        }
    }

    /// <summary>
    /// A login seen by a capture: registers the account (with its own database) the first time, and records the
    /// capture target, time and whatever the login told about it.
    /// </summary>
    public GameAccount Observe(long uid, string captureTarget, int? serverId = null, string? name = null, int? level = null,
        string? serverName = null)
    {
        if (uid <= 0) throw new ArgumentOutOfRangeException(nameof(uid));
        GameAccount account;
        lock (gate)
        {
            var index = accounts.FindIndex(item => item.Uid == uid);
            var existing = index >= 0 ? accounts[index] : null;
            var server = serverId is > 0 ? serverId.Value : existing?.ServerId ?? 0;
            account = (existing ?? new GameAccount { Uid = uid, DatabasePath = NewDatabasePath(uid) }) with
            {
                Name = string.IsNullOrWhiteSpace(name) ? existing?.Name ?? "" : name.Trim(),
                Level = level is > 0 ? level.Value : existing?.Level ?? 0,
                ServerId = server,
                // A known name stays while the server does; another server without a name shows its number.
                ServerName = !string.IsNullOrWhiteSpace(serverName)
                    ? serverName.Trim()
                    : server == existing?.ServerId ? existing.ServerName : "",
                LastCaptureTarget = string.IsNullOrWhiteSpace(captureTarget)
                    ? existing?.LastCaptureTarget ?? ""
                    : captureTarget.Trim(),
                LastSeenAt = DateTimeOffset.Now
            };
            if (index >= 0) accounts[index] = account;
            else accounts.Add(account);
            Save();
        }

        Changed?.Invoke();
        return account;
    }

    /// <summary>
    /// Registers a database written before accounts were told apart, under the account its own profile names, so
    /// it keeps its path (settings and growth goals point at it). Null when it holds no commander profile.
    /// </summary>
    public GameAccount? RegisterDatabase(string databasePath, string captureTarget = "")
    {
        if (string.IsNullOrWhiteSpace(databasePath) || !File.Exists(databasePath)) return null;
        var full = Path.GetFullPath(databasePath);
        var known = FindByDatabasePath(full);
        if (known is not null) return known;

        var profile = ReadProfile(full);
        if (profile is null) return null;
        var server = ReadLastServer(full);

        GameAccount account;
        lock (gate)
        {
            if (accounts.Any(item => item.Uid == profile.Value.Uid))
            {
                // The account already has a database of its own; leave the old file out rather than fork its data.
                return accounts.First(item => item.Uid == profile.Value.Uid);
            }

            account = new GameAccount
            {
                Uid = profile.Value.Uid,
                Name = profile.Value.Name,
                Level = profile.Value.Level,
                ServerId = server?.Id ?? 0,
                ServerName = server?.Name ?? "",
                DatabasePath = full,
                LastCaptureTarget = captureTarget.Trim(),
                LastSeenAt = File.GetLastWriteTime(full)
            };
            accounts.Add(account);
            Save();
        }

        Changed?.Invoke();
        return account;
    }

    /// <summary>
    /// Fills in the server of accounts registered without one (a database from before servers were kept) from the
    /// last login each database recorded.
    /// </summary>
    public void FillMissingServers()
    {
        var missing = Accounts.Where(account => account.ServerId == 0 || account.ServerName.Length == 0).ToList();
        var changed = false;
        foreach (var account in missing)
        {
            if (ReadLastServer(account.DatabasePath) is not { } server) continue;
            lock (gate)
            {
                var index = accounts.FindIndex(item => item.Uid == account.Uid);
                if (index < 0) continue;
                var current = accounts[index];
                // A database's old login says nothing newer than what a capture already recorded.
                if (current.ServerId != 0 && current.ServerId != server.Id) continue;
                var name = server.Name.Length > 0 ? server.Name : current.ServerName;
                if (current.ServerId == server.Id && current.ServerName == name) continue;
                accounts[index] = current with { ServerId = server.Id, ServerName = name };
                changed = true;
            }
        }

        if (!changed) return;
        lock (gate)
        {
            Save();
        }

        Changed?.Invoke();
    }

    /// <summary>
    /// The server of the last login a database recorded: CS 10022 serverid, named from the SC 10021 server list
    /// when that name was stored readable (older captures turned the CP949 names into replacement characters).
    /// </summary>
    public static (int Id, string Name)? ReadLastServer(string databasePath)
    {
        if (string.IsNullOrWhiteSpace(databasePath) || !File.Exists(databasePath)) return null;
        try
        {
            using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = databasePath,
                Mode = SqliteOpenMode.ReadOnly,
                Cache = SqliteCacheMode.Private,
                Pooling = false
            }.ToString());
            connection.Open();
            using (var exists = connection.CreateCommand())
            {
                exists.CommandText = "SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = 'decoded_packets'";
                if (exists.ExecuteScalar() is null) return null;
            }

            var login = LastJson(connection, "CS", 10022);
            if (login is null || ServerIdOf(login) is not { } id) return null;
            var list = LastJson(connection, "SC", 10021);
            var name = list is null ? "" : ServerNames(list).GetValueOrDefault(id, "");
            return (id, name.Contains('�') ? "" : name);
        }
        catch (SqliteException)
        {
            return null;
        }
    }

    /// <summary>The CS 10022 serverid, or null.</summary>
    public static int? ServerIdOf(string loginJson)
    {
        try
        {
            using var document = JsonDocument.Parse(loginJson);
            return document.RootElement.ValueKind == JsonValueKind.Object &&
                   document.RootElement.TryGetProperty("serverid", out var value) &&
                   value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var id) && id > 0
                ? id
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Server id → name from an SC 10021 server list; one entry can stand for several ids.</summary>
    public static Dictionary<int, string> ServerNames(string serverListJson)
    {
        var names = new Dictionary<int, string>();
        try
        {
            using var document = JsonDocument.Parse(serverListJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                !document.RootElement.TryGetProperty("serverlist", out var list))
            {
                return names;
            }

            foreach (var server in list.ValueKind == JsonValueKind.Array ? list.EnumerateArray().ToList() : [list])
            {
                if (server.ValueKind != JsonValueKind.Object ||
                    !server.TryGetProperty("name", out var nameValue) || nameValue.ValueKind != JsonValueKind.String ||
                    !server.TryGetProperty("ids", out var ids))
                {
                    continue;
                }

                var name = nameValue.GetString()?.Trim() ?? "";
                foreach (var id in ids.ValueKind == JsonValueKind.Array ? ids.EnumerateArray().ToList() : [ids])
                {
                    if (id.ValueKind == JsonValueKind.Number && id.TryGetInt32(out var number) && number > 0 &&
                        name.Length > 0)
                    {
                        names[number] = name;
                    }
                }
            }
        }
        catch (JsonException)
        {
        }

        return names;
    }

    private static string? LastJson(SqliteConnection connection, string direction, int packetId)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
                              SELECT json FROM decoded_packets
                              WHERE direction = $direction AND packet_id = $packet
                              ORDER BY rowid DESC LIMIT 1
                              """;
        command.Parameters.AddWithValue("$direction", direction);
        command.Parameters.AddWithValue("$packet", packetId);
        return command.ExecuteScalar() as string;
    }

    /// <summary>The capture writing an account's database right now; null when none is.</summary>
    public string? WriterOf(long uid) => writers.TryGetValue(uid, out var target) ? target : null;

    public void SetWriter(long uid, string captureTarget) => writers[uid] = captureTarget;

    /// <summary>Clears the entry only while it still names this capture; a newer writer keeps its own.</summary>
    public void ClearWriter(long uid, string captureTarget) =>
        writers.TryRemove(new KeyValuePair<long, string>(uid, captureTarget));

    /// <summary>Numbers a login in the order logins happened across every capture and tells the others.</summary>
    public AccountLogin AnnounceLogin(long uid, string captureTarget)
    {
        var login = new AccountLogin(uid, captureTarget, Interlocked.Increment(ref loginSequence));
        LoggedIn?.Invoke(login);
        return login;
    }

    /// <summary>
    /// How many of these ship uids a database's current_ships holds. Ship uids are unique per account, so the ships
    /// in a sortie or fleet packet tell whose session it is without a login.
    /// </summary>
    public static int CountOwnedShips(string databasePath, IReadOnlyCollection<long> shipUids)
    {
        if (shipUids.Count == 0 || string.IsNullOrWhiteSpace(databasePath) || !File.Exists(databasePath)) return 0;
        try
        {
            using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = databasePath,
                Mode = SqliteOpenMode.ReadOnly,
                Cache = SqliteCacheMode.Private,
                Pooling = false
            }.ToString());
            connection.Open();
            using (var exists = connection.CreateCommand())
            {
                exists.CommandText = "SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = 'current_ships'";
                if (exists.ExecuteScalar() is null) return 0;
            }

            using var command = connection.CreateCommand();
            var names = shipUids.Distinct().Select((uid, index) =>
            {
                var name = $"$s{index.ToString(CultureInfo.InvariantCulture)}";
                command.Parameters.AddWithValue(name, uid.ToString(CultureInfo.InvariantCulture));
                return name;
            }).ToList();
            command.CommandText = $"SELECT COUNT(*) FROM current_ships WHERE ship_uid IN ({string.Join(", ", names)})";
            return Convert.ToInt32(command.ExecuteScalar() ?? 0, CultureInfo.InvariantCulture);
        }
        catch (SqliteException)
        {
            return 0;
        }
    }

    /// <summary>The uid, name and level a database's current_commander_profile holds.</summary>
    public static (long Uid, string Name, int Level)? ReadProfile(string databasePath)
    {
        try
        {
            using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = databasePath,
                Mode = SqliteOpenMode.ReadOnly,
                Cache = SqliteCacheMode.Private,
                Pooling = false
            }.ToString());
            connection.Open();
            using (var exists = connection.CreateCommand())
            {
                exists.CommandText =
                    "SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = 'current_commander_profile'";
                if (exists.ExecuteScalar() is null) return null;
            }

            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            using (var command = connection.CreateCommand())
            {
                command.CommandText =
                    "SELECT key, value_json FROM current_commander_profile WHERE key IN ('id', 'name', 'level')";
                using var reader = command.ExecuteReader();
                while (reader.Read()) values[reader.GetString(0)] = reader.GetString(1);
            }

            if (!values.TryGetValue("id", out var idText) ||
                !long.TryParse(idText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var uid) || uid <= 0)
            {
                return null;
            }

            var name = "";
            if (values.TryGetValue("name", out var nameJson))
            {
                try
                {
                    name = JsonSerializer.Deserialize<string>(nameJson) ?? "";
                }
                catch (JsonException)
                {
                    name = nameJson.Trim('"');
                }
            }

            var level = values.TryGetValue("level", out var levelText) &&
                        int.TryParse(levelText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : 0;
            return (uid, name, level);
        }
        catch (SqliteException)
        {
            return null;
        }
    }

    private string NewDatabasePath(long uid) =>
        Path.Combine(DataDirectory, AccountsDirectoryName, uid.ToString(CultureInfo.InvariantCulture),
            AzurLaneDashboard.Core.Configuration.AppDefaults.DefaultDatabaseFileName);

    private static bool SamePath(string left, string right) =>
        !string.IsNullOrWhiteSpace(left) &&
        string.Equals(Path.GetFullPath(left), right, StringComparison.OrdinalIgnoreCase);

    private static List<GameAccount> Read(string path)
    {
        try
        {
            if (!File.Exists(path)) return [];
            var file = JsonSerializer.Deserialize<RegistryFile>(File.ReadAllText(path, Encoding.UTF8), JsonOptions);
            return file?.Accounts?.Where(account => account.Uid > 0 && !string.IsNullOrWhiteSpace(account.DatabasePath))
                       .GroupBy(account => account.Uid)
                       .Select(group => group.First())
                       .ToList() ?? [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return [];
        }
    }

    // Called under the gate. Written to a temporary file and moved over, so a crash never leaves half a list.
    private void Save()
    {
        try
        {
            Directory.CreateDirectory(DataDirectory);
            var temporary = RegistryPath + ".tmp";
            var json = JsonSerializer.Serialize(new RegistryFile { Accounts = accounts.ToList() }, JsonOptions);
            File.WriteAllText(temporary, json, new UTF8Encoding(false));
            File.Move(temporary, RegistryPath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The list stays in memory; the next change tries again.
        }
    }

    private sealed class RegistryFile
    {
        public int Version { get; init; } = 1;
        public List<GameAccount>? Accounts { get; init; }
    }
}
