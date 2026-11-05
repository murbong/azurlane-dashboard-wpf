using System.Text.Json;
using AzurLaneDashboard.Core.Models;
using AzurLaneDashboard.Core.Services;
using AzurLaneDashboard.Data.Services;
using AzurLaneDashboard.Protocol;
using Microsoft.Data.Sqlite;

/// <summary>META fight log: CS 40003 decoding, the fight table and its backfill, the snapshot read and the findings.</summary>
internal static class MetaFightChecks
{
    private const string Account = "100000001";

    public static void Run()
    {
        CheckDecoder();
        CheckWriterAndSnapshot();
        CheckBackfill();
        CheckAnalyzer();
        Console.WriteLine("META fight log checks passed.");
    }

    private static void CheckDecoder()
    {
        var finish = new GamePayloadDecoder().Decode(40003, Message(
            VarintField(1, 50), VarintField(2, 100000001), VarintField(4, 1),
            MessageField(5, Statistic(53293562, 117023, 4884, 0, 4496, 5290)),
            VarintField(7, 80), VarintField(11, 78),
            MessageField(12, Message(VarintField(1, 295360), VarintField(2, 1805716), VarintField(3, 1820000)))));
        var stat = finish["statistics"]?[0];
        Check(stat?["ship_id"]?.GetValue<long>() == 53293562 && stat["damage_cause"]?.GetValue<long>() == 117023 &&
              stat["damage_caused"]?.GetValue<long>() == 4884 && stat["hp_rest"]?.GetValue<long>() == 0,
            "CS 40003 statistics decode to named fields: dealt, taken, HP left");
        Check(finish["enemy_info"]?[0]?["damage_taken"]?.GetValue<long>() == 1805716 && finish["boss_hp"]?.GetValue<long>() == 78,
            "CS 40003 enemy info carries the boss damage");
    }

    private static void CheckWriterAndSnapshot()
    {
        using var db = new PacketDatabase();
        db.Execute($"INSERT INTO current_commander_profile (key, value_json) VALUES ('id', '{Account}')");
        db.Execute("INSERT INTO current_ship_equipment_slots (ship_uid, slot, empty, equipment_template_id) VALUES ('11', 1, 0, 44333), ('11', 2, 1, 0)");
        db.Apply("CS", 40001, $$"""{"system":50,"data":{{Account}},"ship_id_list":[21,22,23,11,12,13]}""");
        // Gear changed after the sortie must not leak into the fight's record.
        db.Execute("UPDATE current_ship_equipment_slots SET equipment_template_id = 44320 WHERE ship_uid = '11' AND slot = 1");
        db.Apply("CS", 40003, Finish(Account, 1805716, 78, 80, ("11", 505368, 14926, 9540), ("21", 117023, 4884, 0)));
        db.Apply("SC", 40004, """{"result":0}""");
        Check(db.Scalar("SELECT COUNT(*) FROM current_meta_boss_fights WHERE finish_rowid IS NOT NULL") == 1,
            "a META sortie and its finish make one closed fight");
        Check(db.Text("SELECT gear_json FROM current_meta_boss_fights").Contains("44333"),
            "the fight keeps the gear the ships carried at the sortie");

        db.Apply("CS", 40001, """{"system":1,"data":101,"ship_id_list":[1,2]}""");
        db.Apply("CS", 40003, """{"system":1,"data":101,"score":4,"statistics":[]}""");
        Check(db.Scalar("SELECT COUNT(*) FROM current_meta_boss_fights") == 1, "map fights are not META fights");

        db.Apply("CS", 40003, Finish("201990470", 1313519, 2782, 80, ("11", 321250, 18359, 9422)));
        Check(db.Scalar("SELECT COUNT(*) FROM current_meta_boss_fights WHERE ship_uids_json = '[]'") == 1,
            "a finish without a captured sortie still records the fight");

        var inputs = new NativeSnapshotService().Load(db.Path).FleetPlan.Inputs;
        var fights = inputs.MetaFights;
        Check(fights.Count == 2 && fights[0] is { IsOwnBoss: true, BossDamage: 1805716, BossEnemyId: 295360, BossHpLeft: 78 } &&
              fights[1] is { IsOwnBoss: false, BossDamage: 1313519 },
            "the snapshot reads both fights and tells the account's own boss from a support fight");
        Check(fights[0].Stats.Single(stat => stat.Uid == "21") is { Damage: 117023, DamageTaken: 4884, HpRest: 0 } &&
              fights[0].Gear?["11"].Single(gear => gear.Slot == 1).TemplateId == 44333,
            "per-ship damage and the sortie's gear survive the round trip");
        Check(fights[0].SortieUids.SequenceEqual(["21", "22", "23", "11", "12", "13"]) && fights[1].SortieUids.Count == 0,
            "a fight keeps its sortie order, which is the META fleet used on that boss");
    }

    private static void CheckBackfill()
    {
        var path = Path.Combine(Path.GetTempPath(), $"azurlane-meta-backfill-{Guid.NewGuid():N}.sqlite");
        try
        {
            using (var connection = new SqliteConnection($"Data Source={path}"))
            {
                connection.Open();
                var legacyStat = Convert.ToHexString(Statistic(53293562, 117023, 4884, 0, 4496, 5290)).ToLowerInvariant();
                var legacyEnemy = Convert.ToHexString(Message(VarintField(1, 295360), VarintField(2, 1805716), VarintField(3, 1820000)))
                    .ToLowerInvariant();
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = $$"""
                        CREATE TABLE decoded_packets (source TEXT, stream_id TEXT, direction TEXT, packet_id INTEGER,
                            packet_name TEXT, "index" INTEGER, json TEXT);
                        INSERT INTO decoded_packets VALUES
                            ('cap', '1', 'CS', 40001, 'CS_40001', 0, '{"ship_id_list":[53293562],"system":50,"data":{{Account}}}'),
                            ('cap', '1', 'CS', 40003, 'CS_40003', 0,
                             '{"statistics":[{"raw":"{{legacyStat}}"}],"enemy_info":[{"raw":"{{legacyEnemy}}"}],"system":50,"data":{{Account}},"score":1,"total_time":80,"boss_hp":78}'),
                            ('cap', '1', 'CS', 40003, 'CS_40003', 0, '{"statistics":[],"system":51,"data":293154,"score":4}');
                        """;
                    command.ExecuteNonQuery();
                }

                _ = new CurrentStateWriter(connection);
                using var count = connection.CreateCommand();
                count.CommandText = "SELECT COUNT(*) FROM current_meta_boss_fights WHERE finish_rowid = 2 AND gear_json IS NULL";
                Check(Convert.ToInt64(count.ExecuteScalar()) == 1,
                    "fights already in decoded_packets are copied once, with their gear left unknown");
            }

            var fight = new NativeSnapshotService().Load(path).FleetPlan.Inputs.MetaFights.Single();
            Check(fight is { BossDamage: 1805716, Gear: null } && fight.Stats.Single() is { Damage: 117023, HpRest: 0 },
                "statistics stored as protobuf hex by older builds still read");
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(path)) File.Delete(path);
        }
    }

    private static void CheckAnalyzer()
    {
        var boss = MetaBossCatalog.Parse(Json("""
            { "24": { "id": 24, "name": "새러토가·META", "availability": "timed", "armor": "Medium", "hull": "CV", "battle_seconds": 80,
                      "windows": [ { "start": "2000-01-01T00:00:00", "end": "2099-12-31T12:00:00" } ],
                      "levels": [ { "level": 15, "hp": 1820000, "enemy_id": 295360, "expedition_id": 296360 } ] } }
            """)).Single();
        var (guides, rules) = MetaBossCatalog.ParseGuides(Json("""
            { "bosses": { "24": { "verified": true, "armorAlternates": ["Medium", "Heavy"],
                "mechanics": [ { "title": "록온은 선두만", "text": "전위 선두가 받는 피해가 늘어난다.", "response": "가장 단단한 함선을 선두에" } ],
                "fleet": { "main": [ ["r_g"], ["r_l"], ["r_a"] ], "vanguard": [ ["r_t"], ["r_ti"], ["r_e"] ], "flagship": 0 } } } }
            """));
        var info = new (string Uid, string Name, string Position)[]
        {
            ("g", "괴츠", "Main"), ("l", "라이온", "Main"), ("m", "메클렌부르크", "Main"), ("a", "아즈치", "Main"),
            ("t", "트라팔가", "Vanguard"), ("d", "드레이크", "Vanguard"), ("ti", "타이거", "Vanguard"), ("e", "엘드릿지", "Vanguard")
        };
        var ships = info.ToDictionary(ship => ship.Uid,
            ship => new MetaFightShipInfo(ship.Uid, ship.Name, "", "UR", ship.Position == "Main" ? "BB" : "DD", ship.Position, 125, $"r_{ship.Uid}"));
        var planner = info.Select(ship => new FleetPlannerShip
        {
            Uid = ship.Uid, RegalUid = $"r_{ship.Uid}", Name = ship.Name, Position = ship.Position,
            Hull = ship.Position == "Main" ? "BB" : "DD", Level = 125,
            Ratings = new Dictionary<string, string> { ["META"] = "S" }
        }).ToList();
        var plan = MetaBossPlanBuilder.Build(boss, guides[24], planner, rules, new DateTime(2026, 11, 6));

        var fights = new[]
        {
            Fight(10, 1469897, 1923, 80, ["t", "d", "e", "g", "l", "m"], [("g", 416151, 9422), ("l", 282507, 9329), ("m", 404301, 8528),
                ("t", 92847, 0), ("d", 198548, 4221), ("e", 124389, 1005)]),
            Fight(20, 193658, 0, 19, ["t", "d", "e", "g", "l", "m"], [("g", 78829, 9928), ("t", 27003, 9705)]),
            Fight(30, 1805716, 78, 80, ["t", "ti", "e", "g", "l", "a"], [("g", 505368, 9540), ("l", 416378, 9442), ("a", 385581, 9259),
                ("t", 117023, 0), ("ti", 257068, 2413), ("e", 128256, 2158)])
        };
        var gear = new[]
        {
            new GearShipPlan
            {
                Ship = new GearFleetShip("g", "괴츠", 1, true, new Dictionary<int, GearItemCopy>()),
                Slots = [new GearSlotPlan { Slot = 1, Action = GearSlotAction.Bag, BaseId = 1, Rank = 1 },
                    new GearSlotPlan { Slot = 2, Action = GearSlotAction.Keep, BaseId = 2, Rank = 1 }]
            }
        };
        var log = MetaBossFightAnalyzer.Build(boss, guides[24], fights, ships, plan, gear);
        Check(log.Fights.Select(fight => fight.Id).SequenceEqual([30, 20, 10]) && log.OwnCount == 3 &&
              log.BestFullDamage == 1805716, "fights list latest first; the best counts full fights only");

        var latest = log.Fights[0];
        Check(latest is { IsFullFight: true, Killed: false, ShortBy: 14284, ResultKind: "warn" },
            $"a full fight that leaves 0.78% is 14,284 short, not the rounded-down {Math.Round(0.0078 * 1820000)}");
        Check(latest.MainShips[0] is { Uid: "g", Role: "기함" } && latest.VanguardShips[0] is { Uid: "t", Role: "선두", IsSunk: true } &&
              Math.Abs(latest.Ships.Sum(ship => ship.Share) - 1) < 1e-9, "sortie order gives flagship and lead; shares add up");
        Check(latest.Findings[0].Title == "처치까지 14,284 부족", "the result leads the findings");
        Check(latest.Findings.Any(finding => finding.Kind == "warn" && finding.Title == "선두 트라팔가 격침" &&
                                             finding.Text.Contains("풀 전투 2번 중 2번") && finding.Text.Contains("가장 단단한 함선을 선두에")),
            "a sunk lead names how often it sank and the guide's lead mechanic");
        var compare = latest.Findings.Single(finding => finding.Title.StartsWith("직전 풀 전투보다", StringComparison.Ordinal));
        Check(compare.Kind == "good" && compare.Title.Contains("+335,819") && compare.Text.Contains("드레이크 → 타이거") &&
              compare.Text.Contains("메클렌부르크 → 아즈치"), "the mop-up is skipped; the swap against the last full fight is named");
        Check(latest.Findings.Any(finding => finding is { Kind: "good", Title: "추천 편성과 같음" }), "the guide fleet matches");
        Check(latest.Findings.Any(finding => finding.Title == "장비 교체 가능 1칸" && finding.Text.StartsWith("괴츠 1칸", StringComparison.Ordinal)),
            "the latest fight lists the gear Regal would change");

        var mopUp = log.Fights[1];
        Check(mopUp is { IsFullFight: false, Killed: true, ResultKind: "good" } && mopUp.Findings[0].Title == "마무리 처치" &&
              mopUp.Findings.All(finding => !finding.Title.StartsWith("직전", StringComparison.Ordinal)),
            "a fight that began at 10.6% is a mop-up and is not compared");
        var first = log.Fights[2];
        Check(first.Findings.Any(finding => finding.Title == "추천과 다른 함선 2척" &&
                                            finding.Text.Contains("메클렌부르크 → 추천 아즈치") && finding.Text.Contains("드레이크 → 추천 타이거")) &&
              first.Findings.All(finding => !finding.Title.StartsWith("장비", StringComparison.Ordinal)),
            "an older fleet is compared with the recommendation; gear is judged on the latest fight only");

        var kills = MetaBossFightAnalyzer.Build(boss, null, [
            Fight(40, 1820000, 0, 78, ["t", "ti", "e", "g", "l", "a"], [("g", 900000, 9000), ("t", 920000, 0)]),
            Fight(50, 1820000, 0, 75, ["t", "ti", "e", "g", "l", "a"], [("g", 910000, 9000), ("t", 910000, 3000)])
        ], ships).Fights[0];
        Check(kills.Findings.Any(finding => finding is { Kind: "good", Title: "직전 풀 전투보다 3초 빨리 처치" }) &&
              kills.Findings.All(finding => !finding.Title.Contains("+0", StringComparison.Ordinal)),
            "two kills compare by time, since both dealt the boss's whole HP");

        var archive = MetaBossFightAnalyzer.Build(boss with { Levels = [new MetaBossLevel(15, 1420000, 1, 2)] }, null, fights, ships);
        Check(!archive.HasFights && archive.LatestFleetUids.Count == 0 &&
              log.LatestFleetUids.SequenceEqual(["g", "l", "a", "t", "ti", "e"]),
            "fights against another boss are not listed; each boss keeps its own last fleet");
    }

    private static MetaBossFightRecord Fight(long id, long damage, int left, int seconds, string[] sortie,
        (string Uid, long Damage, int Hp)[] stats) => new()
    {
        Id = id, OwnerUid = Account, IsOwnBoss = true, BossEnemyId = 295360, BossDamage = damage, BossTotalHp = 1820000,
        BossHpLeft = left, TotalSeconds = seconds, AutoAtStart = true, SortieUids = sortie,
        Stats = stats.Select(stat => new MetaFightShipStat(stat.Uid, stat.Damage, 1000, stat.Hp, 5000, 6000)).ToList()
    };

    private static string Finish(string owner, long damage, int left, int seconds, params (string Uid, long Damage, long Taken, int Hp)[] stats) =>
        JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["system"] = 50, ["data"] = long.Parse(owner), ["score"] = left == 0 ? 4 : 1, ["total_time"] = seconds, ["boss_hp"] = left,
            ["auto_before"] = 1, ["auto_switch_time"] = 0,
            ["statistics"] = stats.Select(stat => new Dictionary<string, long>
            {
                ["ship_id"] = long.Parse(stat.Uid), ["damage_cause"] = stat.Damage, ["damage_caused"] = stat.Taken,
                ["hp_rest"] = stat.Hp, ["max_damage_once"] = 4000, ["ship_gear_score"] = 6000
            }).ToList(),
            ["enemy_info"] = new[] { new Dictionary<string, long> { ["enemy_id"] = 295360, ["damage_taken"] = damage, ["total_hp"] = 1820000 } }
        });

    private static byte[] Statistic(ulong uid, ulong dealt, ulong taken, ulong hp, ulong max, ulong score) => Message(
        VarintField(1, uid), VarintField(2, dealt), VarintField(3, taken), VarintField(4, hp), VarintField(5, max), VarintField(6, score));

    private static JsonElement Json(string text)
    {
        using var document = JsonDocument.Parse(text);
        return document.RootElement.Clone();
    }

    private static byte[] Message(params byte[][] fields) => fields.SelectMany(field => field).ToArray();

    private static byte[] VarintField(int number, ulong value) => Message(Varint((ulong)(number << 3)), Varint(value));

    private static byte[] MessageField(int number, byte[] value) =>
        Message(Varint((ulong)((number << 3) | 2)), Varint((ulong)value.Length), value);

    private static byte[] Varint(ulong value)
    {
        var bytes = new List<byte>();
        do
        {
            var next = (byte)(value & 0x7f);
            value >>= 7;
            bytes.Add(value == 0 ? next : (byte)(next | 0x80));
        } while (value != 0);

        return bytes.ToArray();
    }

    private static void Check(bool condition, string description)
    {
        if (!condition) throw new InvalidOperationException(description);
    }
}
