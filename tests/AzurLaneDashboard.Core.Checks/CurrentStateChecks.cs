using System.Text.Json;
using AzurLaneDashboard.Data.Services;
using Microsoft.Data.Sqlite;

internal static class CurrentStateChecks
{
    public static void Run()
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var finishedAt = now - 86_400;
        var runningUntil = now + 3_600;
        var packets = new (string Direction, int PacketId, string Json)[]
        {
            ("SC", 12001, """
                          {"shiplist":[
                            {"id":101,"template_id":102074,"level":125,"exp":0,
                             "equip_info_list":[{"id":11200,"skin_id":0},{"id":0,"skin_id":0},{"id":16200,"skin_id":0},{"id":0,"skin_id":0},{"id":0,"skin_id":0}]},
                            {"id":102,"template_id":399054,"level":125,"exp":0,
                             "equip_info_list":[{"id":17350,"skin_id":0},{"id":19150,"skin_id":0},{"id":16200,"skin_id":0},{"id":1450,"skin_id":0},{"id":1450,"skin_id":0}]}
                          ]}
                          """),
            ("CS", 12006, """{"ship_id":102,"equip_id":0,"pos":2,"type":0}"""),
            ("SC", 12007, """{"result":0}"""),
            ("SC", 12101, """{"group_list":[{"id":1,"name":"","ship_list":[102],"commanders":[]},{"id":101,"name":"","ship_list":[101],"commanders":[]}]}"""),
            ("SC", 63000, $$"""{"queue":[{"id":854,"time":{{finishedAt}}},{"id":882,"time":{{runningUntil}}}],"refresh_list":[]}"""),
            ("CS", 63013, """{"type":0}"""),
            ("SC", 63014, """{"result":0,"refresh_list":[{"id":2,"technologys":[]}]}"""),
            ("SC", 22001, $$"""
                            {"skill_class_list":[
                              {"room_id":2,"ship_id":101,"start_time":{{finishedAt - 43_200}},"finish_time":{{finishedAt}},"skill_pos":152730,"exp":3000},
                              {"room_id":3,"ship_id":102,"start_time":{{runningUntil - 43_200}},"finish_time":{{runningUntil}},"skill_pos":152830,"exp":3000}
                            ]}
                            """),
            ("SC", 22013, """{"proficiency":100296,"exp_in_well":26400}"""),
            ("CS", 40003, """{"system":1,"data":2063213,"total_time":45}"""),
            ("SC", 40004, """{"mvp":101,"ship_exp_list":[{"ship_id":101,"exp":1826},{"ship_id":102,"exp":913}]}""")
        };

        var databasePath = Path.Combine(Path.GetTempPath(), $"azurlane-current-state-check-{Guid.NewGuid():N}.sqlite");
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
                    Check(result.Status == "Applied", $"{direction}_{packetId} must apply to current state ({result.Detail})");
                }

                Check(ScalarInt(connection,
                        "SELECT COUNT(*) FROM current_ship_equipment_slots WHERE ship_uid = '101' AND empty = 1") == 3,
                    "the slot table records the empty slots from equip_info_list");
                foreach (var (table, columns) in RetiredStorage.Columns)
                {
                    Check(ScalarInt(connection,
                            $"SELECT COUNT(*) FROM pragma_table_info('{table}') WHERE name IN ({string.Join(", ", columns.Select(column => $"'{column}'"))})") == 0,
                        $"a new database creates {table} without its retired columns");
                }

                foreach (var table in RetiredStorage.Tables)
                {
                    Check(ScalarInt(connection, $"SELECT COUNT(*) FROM sqlite_master WHERE name = '{table}'") == 0,
                        $"a new database does not create the retired table {table}");
                }
            }

            var snapshot = new NativeSnapshotService().Load(databasePath);
            Check(snapshot.SnapshotSource.Contains("current", StringComparison.OrdinalIgnoreCase),
                $"the check database loads through the current-state tables ({snapshot.SnapshotSource})");

            var stripped = snapshot.Ships.Single(ship => ship.ShipUid == "101");
            var unequipped = snapshot.Ships.Single(ship => ship.ShipUid == "102");
            Check(stripped.EmptySlots == 3, "empty slots come from the slot table");
            Check(unequipped.EmptySlots == 1, "an unequip request (12006) is counted from the slot table");
            Check(unequipped.StatusBadges.Any(badge => badge.Label == "편성 빈칸 1" && badge.Kind == "warn"),
                "a sortie-fleet ship with missing equipment is flagged");
            Check(stripped.StatusBadges.All(badge => !badge.Label.Contains("빈칸", StringComparison.Ordinal)),
                "a stripped ship outside the sortie fleets is not flagged (fleet 101 is not a sortie fleet)");

            var firstGap = snapshot.GearGaps.First();
            Check(firstGap.ShipUid == "102" && firstGap.ShipDisplaySummary.StartsWith("1함대", StringComparison.Ordinal),
                "fleet ships lead the gear-gap list and name their fleet");
            Check(snapshot.GearGaps.Any(row => row.ShipUid == "101"), "stored ships with gaps stay in the gear-gap list");

            Check(snapshot.AcademyRooms.Count == 2, "finished lessons keep their room until they are collected");
            var finishedRoom = snapshot.AcademyRooms.Single(room => room.RoomId == 2);
            Check(finishedRoom.Remaining == "완료", "a finished lesson is labelled as finished");
            var academy = snapshot.SystemStatuses.Single(status => status.Name == "스킬 교실");
            Check(academy.Detail == "2개 사용 중 · 완료 1개",
                $"a 22013 proficiency update does not replace the room summary ({academy.Detail})");
            Check(academy.IsNotable && academy.StatusLabel == "완료 대기",
                "an activity with something to collect earns a badge instead of the routine dot");
            Check(finishedRoom.SkillName != "152730" && finishedRoom.SkillName.Length > 0,
                $"academy rows name the skill instead of its ID ({finishedRoom.SkillName})");

            // The writer stores stage and MVP as raw IDs; the snapshot must show names.
            var run = snapshot.BattleRuns.Single();
            var mvp = stripped.DisplayName;
            Check(run.StageLabel == "D3 껍질을 깨부술 때 · 보스 함대", $"battle stage shows chapter and fleet ({run.StageLabel})");
            Check(run.MvpName == mvp, $"battle MVP shows the ship name ({run.MvpName})");
            Check(run.TopExpSummary == $"{mvp} 1,826 · {unequipped.DisplayName} 913",
                $"battle EXP summary names ships ({run.TopExpSummary})");

            Check(snapshot.ResearchQueue.Count == 2 && snapshot.ResearchQueue[0].Remaining == "완료",
                "finished research stays queued until it is collected");
            var research = snapshot.SystemStatuses.Single(status => status.Name == "연구");
            Check(research.Detail == "큐 2개 · 완료 1개",
                $"a 63014 refresh does not replace the research summary ({research.Detail})");
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(databasePath))
            {
                File.Delete(databasePath);
            }
        }

        Console.WriteLine("Current state checks passed.");
    }

    private static int ScalarInt(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt32(command.ExecuteScalar());
    }

    private static void Check(bool condition, string description)
    {
        if (!condition) throw new InvalidOperationException(description);
    }
}
