using System.Text.Json;
using AzurLaneDashboard.Data.Services;
using Microsoft.Data.Sqlite;

/// <summary>Ships leaving the port: retirement, a new login list, and databases that kept ships forever.</summary>
internal static class ShipLifecycleChecks
{
    public static void Run()
    {
        // 301 carries equipment 11200 with skin 5001 and special equipment 900; 302 has special equipment 901.
        const string firstLogin = """
            {"shiplist":[
              {"id":301,"template_id":102074,"level":50,"exp":0,"max_level":100,
               "equip_info_list":[{"id":11200,"skin_id":5001},{"id":0,"skin_id":0}],
               "spweapon":{"id":900,"template_id":2001,"attr_1":3,"attr_2":4,"effect":0,"pt":10}},
              {"id":302,"template_id":102074,"level":60,"exp":0,"max_level":100,"equip_info_list":[],
               "spweapon":{"id":901,"template_id":2002}},
              {"id":303,"template_id":102074,"level":70,"exp":0,"max_level":100,"equip_info_list":[]}
            ]}
            """;
        var packets = new (string Direction, int PacketId, string Json)[]
        {
            ("SC", 12001, firstLogin),
            ("SC", 14001, """{"equip_list":[{"id":11200,"count":2}],"spweapon_list":[]}"""),
            // Captures decoded before the schema fix kept a one-ship list as a bare number.
            ("CS", 12004, """{"ship_id_list":301}"""),
            ("SC", 12005, """{"result":0,"ship_id_list":301}"""),
            // The next login no longer lists 303, and 302 has taken its special equipment off.
            ("SC", 12001, """{"shiplist":[{"id":302,"template_id":102074,"level":60,"exp":0,"max_level":100,"equip_info_list":[]}]}""")
        };

        var databasePath = Path.Combine(Path.GetTempPath(), $"azurlane-ship-lifecycle-check-{Guid.NewGuid():N}.sqlite");
        try
        {
            using (var connection = new SqliteConnection($"Data Source={databasePath}"))
            {
                connection.Open();
                Execute(connection, """
                                    CREATE TABLE decoded_packets (
                                        source TEXT, stream_id TEXT, direction TEXT, packet_id INTEGER,
                                        packet_name TEXT, "index" INTEGER, json TEXT
                                    );
                                    """);
                var writer = new CurrentStateWriter(connection);
                for (var index = 0; index < packets.Length; index++)
                {
                    var (direction, packetId, json) = packets[index];
                    using (var insert = connection.CreateCommand())
                    {
                        insert.CommandText = """
                                             INSERT INTO decoded_packets
                                                 (source, stream_id, direction, packet_id, packet_name, "index", json)
                                             VALUES ('check', '1', $direction, $packet, $name, 0, $json)
                                             """;
                        insert.Parameters.AddWithValue("$direction", direction);
                        insert.Parameters.AddWithValue("$packet", packetId);
                        insert.Parameters.AddWithValue("$name", $"{direction}_{packetId}");
                        insert.Parameters.AddWithValue("$json", json);
                        insert.ExecuteNonQuery();
                    }

                    using var document = JsonDocument.Parse(json);
                    using var transaction = connection.BeginTransaction();
                    var result = writer.ApplyDecodedPacket(transaction,
                        new DecodedPacketEnvelope(index + 1, "check", "1", direction, packetId, 0),
                        document.RootElement);
                    transaction.Commit();
                    Check(result.Status == "Applied", $"{direction}_{packetId} applies ({result.Status} {result.Detail})");

                    if (index == 3)
                    {
                        Check(Scalar(connection, "SELECT COUNT(*) FROM current_ships WHERE ship_uid = '301'") == 0,
                            "a retired ship leaves the port");
                        Check(Scalar(connection, "SELECT count FROM current_equipment_items WHERE equipment_uid = 11200") == 3,
                            "its equipment goes back to the bag");
                        Check(Scalar(connection, "SELECT count FROM current_equipment_skins WHERE skin_id = 5001") == 1,
                            "its equipment skin goes back too");
                        Check(Scalar(connection, "SELECT template_id FROM current_spweapon_inventory WHERE spweapon_uid = 900") == 2001,
                            "its special equipment returns to the spweapon inventory");
                        Check(Scalar(connection, "SELECT COUNT(*) FROM current_ship_special_equipment WHERE ship_uid = '302'") == 1,
                            "special equipment from the ship list is stored for the ship that wears it");
                    }
                }

                Check(Scalar(connection, "SELECT COUNT(*) FROM current_ships") == 1,
                    "a login list replaces the port: ships it does not carry are gone");
                Check(Scalar(connection, "SELECT COUNT(*) FROM current_ship_special_equipment") == 0,
                    "a ship listed without special equipment wears none");

                // A database from before: a ship older than the newest login list and one obtained after it.
                Execute(connection, "INSERT INTO current_ships (ship_uid, ship_template_id, level, snapshot_rowid) VALUES ('399', 102074, 1, 1)");
                Execute(connection, "INSERT INTO current_ships (ship_uid, ship_template_id, level, snapshot_rowid) VALUES ('398', 102074, 1, 99)");
                _ = new CurrentStateWriter(connection);
                Check(Scalar(connection, "SELECT COUNT(*) FROM current_ships WHERE ship_uid = '399'") == 0 &&
                      Scalar(connection, "SELECT COUNT(*) FROM current_ships WHERE ship_uid = '398'") == 1,
                    "opening an old database drops ships the newest login list left out and keeps newer ones");
            }
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(databasePath))
            {
                File.Delete(databasePath);
            }
        }

        Console.WriteLine("Ship lifecycle checks passed.");
    }

    private static void Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static long Scalar(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(command.ExecuteScalar() ?? -1L);
    }

    private static void Check(bool condition, string description)
    {
        if (!condition) throw new InvalidOperationException(description);
    }
}
