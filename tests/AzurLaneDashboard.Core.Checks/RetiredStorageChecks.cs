using AzurLaneDashboard.Data.Services;
using Microsoft.Data.Sqlite;

/// <summary>
/// The diagnostics cleanup must back the database up before it drops anything, keep every row the app
/// still reads, and leave a database the snapshot loads exactly as before.
/// </summary>
internal static class RetiredStorageChecks
{
    public static void Run()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"azurlane-retired-check-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var databasePath = Path.Combine(directory, "inventory.sqlite");
        try
        {
            using (var store = new LivePacketStore(databasePath))
            {
                store.AppendDecoded("check", "1", "SC", 12001, 0, """
                    {"shiplist":[
                      {"id":101,"template_id":102074,"level":125,"exp":0,
                       "equip_info_list":[{"id":11200,"skin_id":0},{"id":0,"skin_id":0},{"id":16200,"skin_id":0},{"id":0,"skin_id":0},{"id":0,"skin_id":0}]},
                      {"id":102,"template_id":399054,"level":118,"exp":0,
                       "equip_info_list":[{"id":17350,"skin_id":0},{"id":19150,"skin_id":0},{"id":16200,"skin_id":0},{"id":1450,"skin_id":0},{"id":1450,"skin_id":0}]}
                    ]}
                    """);
                store.AppendDecoded("check", "1", "SC", 14001, 1, """
                    {"equip_list":[{"id":11200,"count":2},{"id":16200,"count":1}],"spweapon_list":[]}
                    """);
                store.AppendDecoded("check", "1", "SC", 12101, 2,
                    """{"group_list":[{"id":1,"name":"","ship_list":[101,102],"commanders":[]}]}""");
            }

            var plantedTables = PlantRetiredStorage(databasePath);

            // Capture keeps writing into a database that still carries the retired storage.
            using (var store = new LivePacketStore(databasePath))
            {
                store.AppendDecoded("check", "1", "SC", 14001, 3, """
                    {"equip_list":[{"id":11200,"count":2},{"id":16200,"count":1},{"id":1450,"count":4}],"spweapon_list":[]}
                    """);
            }

            Check(Count(databasePath, "current_equipment_items") == 3,
                "the writer applies new packets before the cleanup has run");
            Check(!TableExists(databasePath, "current_battle_sessions") || Count(databasePath, "current_battle_sessions") == 0,
                "the writer leaves the retired tables alone");
            var before = new NativeSnapshotService().Load(databasePath);
            var service = new DatabaseMaintenanceService();

            var plan = service.InspectRetiredStorage(databasePath);
            Check(plan.Tables.Count == RetiredStorage.Tables.Count,
                $"every retired table is found ({plan.Tables.Count}/{RetiredStorage.Tables.Count})");
            Check(plan.Columns.Count == RetiredStorage.Columns.Values.Sum(columns => columns.Count),
                $"every retired column is found ({plan.Columns.Count})");
            Check(plan.Indexes.Count == RetiredStorage.Indexes.Count, "every redundant index is found");

            var decodedBefore = Count(databasePath, "decoded_packets");
            var result = service.CleanupRetiredStorage(databasePath);
            Check(File.Exists(result.BackupPath) && Path.GetDirectoryName(result.BackupPath) == directory,
                "the backup is written next to the database");
            Check(result.Compacted && result.SizeAfterBytes < result.SizeBeforeBytes,
                $"dropping the map copies shrinks the file ({result.SizeBeforeBytes} -> {result.SizeAfterBytes})");

            foreach (var table in plantedTables)
            {
                Check(TableExists(result.BackupPath, table), $"the backup still holds {table}");
                Check(!TableExists(databasePath, table), $"{table} is dropped from the database");
            }

            Check(ColumnExists(result.BackupPath, "current_ships", "empty_slots"),
                "the backup keeps the retired columns");
            foreach (var (table, columns) in RetiredStorage.Columns)
            {
                Check(columns.All(column => !ColumnExists(databasePath, table, column)),
                    $"{table} loses its retired columns");
            }

            Check(Count(databasePath, "decoded_packets") == decodedBefore, "decoded packets are kept");
            Check(Count(databasePath, "current_ships") == 2, "ship state is kept");
            Check(Count(databasePath, "current_equipment_items") == 3, "bag equipment is kept");

            var after = new NativeSnapshotService().Load(databasePath);
            Check(after.Ships.Count == before.Ships.Count &&
                  after.Ships.Select(ship => ship.DisplayName).SequenceEqual(before.Ships.Select(ship => ship.DisplayName)),
                "the snapshot lists the same ships after the cleanup");
            Check(after.Equipment.Count == before.Equipment.Count &&
                  after.Equipment.Sum(row => row.Count) == before.Equipment.Sum(row => row.Count),
                "the snapshot counts the same equipment after the cleanup");
            Check(after.Equipment.Any(row => row.BagCount == 2 && row.EquippedCount == 1),
                "bag and equipped counts are grouped from the item and slot tables");

            Check(service.InspectRetiredStorage(databasePath).IsEmpty, "a cleaned database has nothing left to remove");
            var again = service.CleanupRetiredStorage(databasePath);
            Check(again.BackupPath.Length == 0 && Directory.GetFiles(directory, "*.backup-*").Length == 1,
                "running the cleanup on a clean database makes no backup and changes nothing");
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(directory, recursive: true);
        }

        Console.WriteLine("Retired storage cleanup checks passed.");
    }

    /// <summary>Recreates what older builds left behind: map copies, text inventory copies, placeholder columns.</summary>
    private static IReadOnlyList<string> PlantRetiredStorage(string databasePath)
    {
        using var connection = new SqliteConnection($"Data Source={databasePath};Pooling=False");
        connection.Open();
        Execute(connection, """
            CREATE TABLE json_store (namespace TEXT NOT NULL, name TEXT NOT NULL, json TEXT NOT NULL,
                                     mtime_ns INTEGER NOT NULL DEFAULT 0, size INTEGER NOT NULL DEFAULT 0,
                                     updated_at TEXT NOT NULL DEFAULT '', PRIMARY KEY(namespace, name));
            CREATE TABLE ships (source TEXT, ship_uid TEXT, ship_template_id TEXT, name TEXT);
            CREATE TABLE equipment (equipment_template_id TEXT, name TEXT, count TEXT);
            CREATE TABLE equipment_items (equipment_uid TEXT, equipment_template_id TEXT, name TEXT);
            CREATE TABLE current_equipment_inventory (equipment_template_id INTEGER PRIMARY KEY, name TEXT, count INTEGER);
            CREATE TABLE current_battle_sessions (session_key TEXT PRIMARY KEY, updated_at TEXT NOT NULL);
            CREATE INDEX idx_ships_uid ON ships(ship_uid);
            CREATE INDEX idx_decoded_packets_id ON decoded_packets(packet_id);
            CREATE INDEX idx_decoded_packets_lookup ON decoded_packets(packet_id, direction);
            CREATE INDEX idx_packet_decode_failures_id ON packet_decode_failures(packet_id);
            INSERT INTO ships VALUES ('check', '999', '102074', 'retired ship');
            INSERT INTO current_equipment_inventory VALUES (11200, '', 99);
            """);
        using (var insert = connection.CreateCommand())
        {
            insert.CommandText = "INSERT INTO json_store (namespace, name, json) VALUES ('maps', 'equipment.json', $json)";
            insert.Parameters.AddWithValue("$json", $"{{\"pad\":\"{new string('x', 2_000_000)}\"}}");
            insert.ExecuteNonQuery();
        }

        foreach (var (table, columns) in RetiredStorage.Columns)
        {
            foreach (var column in columns)
            {
                Execute(connection, $"ALTER TABLE \"{table}\" ADD COLUMN \"{column}\" TEXT DEFAULT ''");
            }
        }

        Execute(connection, "UPDATE current_ships SET empty_slots = '4', name = 'stale name'");
        return RetiredStorage.Tables;
    }

    private static void Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static SqliteConnection OpenReadOnly(string path)
    {
        var connection = new SqliteConnection($"Data Source={path};Mode=ReadOnly;Pooling=False");
        connection.Open();
        return connection;
    }

    private static bool TableExists(string path, string table)
    {
        using var connection = OpenReadOnly(path);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = $name";
        command.Parameters.AddWithValue("$name", table);
        return Convert.ToInt32(command.ExecuteScalar()) > 0;
    }

    private static bool ColumnExists(string path, string table, string column)
    {
        using var connection = OpenReadOnly(path);
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM pragma_table_info('{table}') WHERE name = $column";
        command.Parameters.AddWithValue("$column", column);
        return Convert.ToInt32(command.ExecuteScalar()) > 0;
    }

    private static long Count(string path, string table)
    {
        if (!TableExists(path, table))
        {
            return -1;
        }

        using var connection = OpenReadOnly(path);
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM \"{table}\"";
        return Convert.ToInt64(command.ExecuteScalar());
    }

    private static void Check(bool condition, string description)
    {
        if (!condition)
        {
            throw new InvalidOperationException($"Retired storage check failed: {description}");
        }
    }
}
