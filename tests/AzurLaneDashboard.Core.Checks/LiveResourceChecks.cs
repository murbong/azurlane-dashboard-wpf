using System.Text.Json;
using AzurLaneDashboard.Core.Services;
using AzurLaneDashboard.Data.Services;
using Microsoft.Data.Sqlite;

/// <summary>Oil between syncs and courtyard food, replayed from packets with the repository's maps.</summary>
internal static class LiveResourceChecks
{
    private const long Tick = 1_800_000_100;

    public static void Run()
    {
        const string fleet = """
                             [{"ship_id":101},{"ship_id":102},{"ship_id":103},{"ship_id":104},{"ship_id":105},{"ship_id":106}]
                             """;
        // 101 is template 102074 (oil 1/9), the rest 399054 (oil 1/17), all Lv.125 (fight_oil_ratio 9950):
        // a battle costs 8 + 5 * 16 = 88, capped in loop chapter 1204 at use_oil_limit 42 (normal) or 55 (boss)
        // less the fleet's oil_at_start of 6.
        var packets = new (string Direction, int PacketId, string Json)[]
        {
            ("SC", 12001, $$"""
                            {"shiplist":[{{string.Join(",", Enumerable.Range(101, 6).Select(uid =>
                                $$"""{"id":{{uid}},"template_id":{{(uid == 101 ? 102074 : 399054)}},"level":125,"exp":0,"equip_info_list":[]}"""))}}]}
                            """),
            ("SC", 11003, """{"resource_list":[{"type":2,"num":1}]}"""),
            ("SC", 11004, """{"resource_list":[{"type":1,"num":100},{"type":2,"num":5000}]}"""),
            ("SC", 13102, """{"result":0,"current_chapter":{"id":1204,"loop_flag":1}}"""),
            ("CS", 40003, """{"system":1,"data":1204010}"""),
            ("SC", 40004, $$"""{"ship_exp_list":{{fleet}},"drop_info":[{"type":1,"id":2,"number":30}]}"""),
            ("CS", 40003, """{"system":1,"data":1204000}"""),
            ("SC", 40004, $$"""{"ship_exp_list":{{fleet}}}"""),
            ("SC", 13102, """{"result":0,"current_chapter":{"id":1204,"loop_flag":0}}"""),
            ("CS", 40003, """{"system":1,"data":1204010}"""),
            ("SC", 40004, $$"""{"ship_exp_list":{{fleet}}}"""),
            ("SC", 20006, """{"award_list":[{"type":1,"id":2,"number":100},{"type":2,"id":20001,"number":1}]}"""),
            ("SC", 11004, """{"resource_list":[{"type":4,"num":7}]}"""),
            ("SC", 19001, $$"""
                            {"lv":4,"food":20000,"food_max_increase":4000,"food_max_increase_count":2,
                             "ship_list":[{"id":101,"floor":1},{"id":102,"floor":1},{"id":103,"floor":2}],
                             "next_timestamp":{{Tick - 100}},"load_time":{{Tick - 108}}}
                            """),
            ("SC", 15001, """{"item_list":[{"id":50005,"count":3},{"id":50001,"count":10}]}"""),
            ("CS", 15002, """{"id":50005,"count":1}"""),
            ("SC", 15003, """{"result":0}"""),
            ("SC", 19027, $$"""{"exp":10,"food":360,"next_timestamp":{{Tick}}}"""),
            ("CS", 19002, """{"ship_id":104,"type":1}"""),
            ("SC", 19003, """{"result":0}"""),
            ("CS", 15002, """{"id":50001,"count":1}"""),
            ("SC", 15003, """{"result":1}""")
        };

        var databasePath = Path.Combine(Path.GetTempPath(), $"azurlane-live-resources-check-{Guid.NewGuid():N}.sqlite");
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
                    if (direction == "SC" && packetId is 19001 or 19027 or 15003 or 20006)
                    {
                        Check(result.Status == "Applied", $"SC_{packetId} triggers a live reload ({result.Status})");
                    }
                }
            }

            var snapshot = new NativeSnapshotService().Load(databasePath);
            var oil = snapshot.Oil;
            Check(oil is { SyncedOil: 5000, SyncedRowId: 3, Battles: 3 },
                $"the newest 11004 with oil is the sync, and an 11004 without oil does not reset it ({oil})");
            Check(oil!.Spent == 10 + 36 + 49 + 10 + 88 && oil.Gained == 30 + 100 && oil.Oil == 4937,
                $"chapter entries, capped loop battles, a plain battle and oil rewards add up ({oil})");

            var dorm = snapshot.DormFood;
            Check(dorm is { BaseFood: 29640, Capacity: 44000, TrainingShips: 3, PerTick: 12, NextTick: Tick },
                $"login food plus a 10,000 snack less the settlement, with a third ship training ({dorm})");
            Check(dorm!.Snacks[50005] == 2 && dorm.Snacks[50001] == 10, "snack counts drop only for successful uses");
            Check(dorm.FoodAt(DateTimeOffset.FromUnixTimeSeconds(Tick - 1)) == 29640 &&
                  dorm.FoodAt(DateTimeOffset.FromUnixTimeSeconds(Tick)) == 29628 &&
                  dorm.FoodAt(DateTimeOffset.FromUnixTimeSeconds(Tick + 150)) == 29640 - 12 * 11,
                "food drops by the ratio's share every 15 s from the next tick");

            var live = LiveStateBuilder.Dorm(DateTimeOffset.FromUnixTimeSeconds(Tick), dorm);
            Check(live is { Food: 29628, Capacity: 44000, TrainingShips: 3 } && live.Snacks["10000"] == 2 &&
                  live.Snacks["20000"] == 0 && Math.Abs(live.Fill!.Value - 0.6734) < 0.0001,
                $"the live state lists food, fill and snacks by food value ({live.Fill})");
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(databasePath))
            {
                File.Delete(databasePath);
            }
        }

        Console.WriteLine("Live resource checks passed.");
    }

    private static void Check(bool condition, string description)
    {
        if (!condition) throw new InvalidOperationException(description);
    }
}
