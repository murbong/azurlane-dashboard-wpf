using System.Text.Json;
using AzurLaneDashboard.Core.Services;
using AzurLaneDashboard.Data.Services;
using Microsoft.Data.Sqlite;

/// <summary>EXP the client adds without a ship list: books, level-cap breaks, the courtyard, commissions, battles.</summary>
internal static class ExpReplayChecks
{
    public static void Run()
    {
        // 201: Lv.115 at its max level; the cap break makes 120 the max, then the books fill it up to 120.
        // 202: Lv.115 at its max level, no break: books give nothing.
        // 203 trains in the courtyard, 204 is on commission 30101, 205 banks battle EXP at its cap.
        string Ship(int uid, int level, int exp, int maxLevel) =>
            $$"""{"id":{{uid}},"template_id":102074,"level":{{level}},"exp":{{exp}},"max_level":{{maxLevel}},"equip_info_list":[]}""";
        var packets = new (string Direction, int PacketId, string Json)[]
        {
            ("SC", 12001, $$"""{"shiplist":[{{Ship(201, 115, 140000, 115)}},{{Ship(202, 115, 100, 115)}},{{Ship(203, 50, 0, 100)}},{{Ship(204, 60, 0, 100)}},{{Ship(205, 100, 0, 100)}},{{Ship(206, 40, 0, 100)}}]}"""),
            ("SC", 13002, """{"collection_list":{"id":30101,"ship_id_list":204,"finish_time":1}}"""),
            ("SC", 19001, """{"lv":4,"food":1000,"ship_list":[{"id":203,"floor":1},{"id":204,"floor":2}],"next_timestamp":1}"""),
            ("CS", 12038, """{"ship_id":201}"""),
            ("SC", 12039, """{"result":0}"""),
            ("CS", 22011, """{"ship_id":201,"books":[{"id":16502,"num":100}]}"""),
            ("SC", 22012, """{"result":0}"""),
            ("CS", 22011, """{"ship_id":202,"books":{"id":16501,"num":1}}"""),
            ("SC", 22012, """{"result":0}"""),
            ("SC", 19027, """{"exp":500,"food":10,"next_timestamp":2}"""),
            ("CS", 13005, """{"id":30101}"""),
            ("SC", 13006, """{"result":0,"exp":300,"drop_list":[]}"""),
            ("CS", 22011, """{"ship_id":206,"books":{"id":16501,"num":1}}"""),
            ("SC", 22012, """{"result":0}"""),
            ("CS", 40003, """{"system":1,"data":1204010}"""),
            ("SC", 40004, """{"ship_exp_list":{"ship_id":205,"exp":1000}}""")
        };

        var databasePath = Path.Combine(Path.GetTempPath(), $"azurlane-exp-replay-check-{Guid.NewGuid():N}.sqlite");
        try
        {
            using (var connection = new SqliteConnection($"Data Source={databasePath}"))
            {
                connection.Open();
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = """
                                          CREATE TABLE decoded_packets (
                                              source TEXT, stream_id TEXT, direction TEXT, packet_id INTEGER,
                                              packet_name TEXT, "index" INTEGER, json TEXT
                                          );
                                          """;
                    command.ExecuteNonQuery();
                }

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
                    if (direction == "SC" && packetId is 22012 or 12039 or 19027 or 13006)
                    {
                        Check(result.Status == "Applied", $"SC_{packetId} triggers a live reload ({result.Status})");
                    }
                }
            }

            var ships = new NativeSnapshotService().Load(databasePath).Ships.ToDictionary(ship => ship.ShipUid);
            string Show(string uid) => $"Lv.{ships[uid].Level} {ships[uid].ExpCurrent}";
            Check(ships["201"].Level == 120 && ships["201"].ExpCurrent == 0,
                $"a cap break raises the max level and books stop at it, dropping the rest ({Show("201")})");
            Check(ships["202"].Level == 115 && ships["202"].ExpCurrent == 100,
                $"books give nothing at the ship's max level ({Show("202")})");
            Check(ships["203"].Level == 50 && ships["203"].ExpCurrent == 500, $"courtyard EXP goes to floor 1 ({Show("203")})");
            Check(ships["204"].Level == 60 && ships["204"].ExpCurrent == 300,
                $"commission EXP goes to the ships sent, not floor 2 of the courtyard ({Show("204")})");
            Check(ships["205"].Level == 100 && ships["205"].ExpCurrent == 1000, $"battle EXP is banked at the cap ({Show("205")})");
            Check(ships["206"].Level == 40 && ships["206"].ExpCurrent == 3000,
                $"a single book kind (an object, not a list) still counts ({Show("206")})");

            var caps = new ShipLevelCaps(JsonDocument.Parse(File.ReadAllText(MapPath("ship_level.json"))).RootElement);
            Check(caps.NextCap(115) == 120 && caps.NextCap(70) == 100, "a cap break goes to the next level_limit row");
            Check(caps.AddExperience(100, 200, 5000, "SSR", 100, banksOverflow: false) == (100, 200),
                "non-battle EXP is not banked at a cap");
            Check(caps.AddExperience(99, 0, 1_000_000, "SSR", 100, banksOverflow: false) == (100, 0),
                "non-battle EXP that reaches the cap drops the rest");
            Check(caps.AddExperience(115, 0, 1_000_000, "SSR", 100, maxLevel: 125).Level > 115,
                "the packet's max level overrides the cap guess");
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(databasePath))
            {
                File.Delete(databasePath);
            }
        }

        Console.WriteLine("EXP replay checks passed.");
    }

    private static string MapPath(string name)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var path = Path.Combine(directory.FullName, "maps", name);
            if (File.Exists(path)) return path;
        }

        throw new FileNotFoundException(name);
    }

    private static void Check(bool condition, string description)
    {
        if (!condition) throw new InvalidOperationException(description);
    }
}
