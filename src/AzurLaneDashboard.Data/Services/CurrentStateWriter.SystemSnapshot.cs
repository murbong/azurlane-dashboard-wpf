using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace AzurLaneDashboard.Data.Services;

public sealed partial class CurrentStateWriter
{
    private StateApplyResult ApplyAcademy(SqliteTransaction tx, DecodedPacketEnvelope packet, JsonElement json)
    {
        var rooms = Items(json, "skill_class_list").ToList();
        Execute(tx, "DELETE FROM current_academy_rooms");
        foreach (var room in rooms)
        {
            UpsertAcademyRoom(tx, room);
        }

        UpsertSystemStatus(tx, "academy", $"{rooms.Count.ToString(CultureInfo.InvariantCulture)}개 사용 중",
            new Dictionary<string, object?>
            {
                ["class_lv"] = Int(json, "class_lv"),
                ["proficiency"] = json.TryGetProperty("class", out var classInfo)
                    ? Int(classInfo, "proficiency")
                    : Int(json, "proficiency")
            });
        return StateApplyResult.Applied("academy", $"{rooms.Count.ToString(CultureInfo.InvariantCulture)} rooms",
            packet.DecodedRowId);
    }

    private StateApplyResult ApplyAcademyStatus(SqliteTransaction tx, DecodedPacketEnvelope packet, JsonElement json)
    {
        UpsertSystemStatus(tx, "academy", "숙련도 갱신", new Dictionary<string, object?>
        {
            ["proficiency"] = Int(json, "proficiency"),
            ["exp_in_well"] = Int(json, "exp_in_well")
        });
        return StateApplyResult.Applied("academy", "academy status", packet.DecodedRowId);
    }

    private void UpsertAcademyRoom(SqliteTransaction tx, JsonElement room)
    {
        var id = Int(room, "room_id");
        if (id <= 0)
        {
            return;
        }

        Execute(
            tx,
            """
            INSERT INTO current_academy_rooms (room_id, ship_uid, skill_pos, start_time, finish_time, exp, raw_json)
            VALUES ($room, $ship, $skill, $start, $finish, $exp, $raw)
            ON CONFLICT(room_id) DO UPDATE SET
                ship_uid = excluded.ship_uid,
                skill_pos = excluded.skill_pos,
                start_time = excluded.start_time,
                finish_time = excluded.finish_time,
                exp = excluded.exp,
                raw_json = excluded.raw_json
            """,
            ("$room", id),
            ("$ship", Text(room, "ship_id")),
            ("$skill", Int(room, "skill_pos")),
            ("$start", Int(room, "start_time")),
            ("$finish", Int(room, "finish_time")),
            ("$exp", Int(room, "exp")),
            ("$raw", Json(room)));
    }

    private StateApplyResult ApplyResearch(SqliteTransaction tx, DecodedPacketEnvelope packet, JsonElement json)
    {
        var queue = Items(json, "queue").ToList();
        Execute(tx, "DELETE FROM current_research_queue");
        var order = 1;
        foreach (var item in queue)
        {
            InsertResearch(tx, order++, FirstPositive(Int(item, "id"), Int(item, "tech_id")), Int(item, "refresh_id"),
                FirstPositive(Int(item, "time"), Int(item, "finish_time")), queued: true);
        }

        // The project being researched sits in its refresh group with a finish time, not in the queue.
        foreach (var group in Items(json, "refresh_list"))
        {
            foreach (var technology in Items(group, "technologys").Where(item => Int(item, "time") > 0))
            {
                InsertResearch(tx, order++, Int(technology, "id"), Int(group, "id"), Int(technology, "time"), queued: false);
            }
        }

        UpsertSystemStatus(tx, "research",
            $"큐 {queue.Count.ToString(CultureInfo.InvariantCulture)}개, refresh {Array(json, "refresh_list").Count().ToString(CultureInfo.InvariantCulture)}묶음",
            new Dictionary<string, object?>
            {
                ["queue"] = queue.Select(JsonDictionary).ToList(),
                ["refresh_list"] = Array(json, "refresh_list").Select(JsonDictionary).ToList()
            });
        return StateApplyResult.Applied("research", $"{queue.Count.ToString(CultureInfo.InvariantCulture)} queue",
            packet.DecodedRowId);
    }

    /// <summary>
    /// A research row: <paramref name="queued"/> for the queue (TechnologyProxy.queue, finished in order by
    /// SC 63016), false for the project being researched (finished by SC 63004, stopped by SC 63006).
    /// </summary>
    private void InsertResearch(SqliteTransaction tx, int order, int techId, int refreshId, int finishTime, bool queued)
    {
        Execute(
            tx,
            """
            INSERT INTO current_research_queue (queue_order, tech_id, refresh_id, finish_time, raw_json)
            VALUES ($order, $tech, $refresh, $time, $raw)
            """,
            ("$order", order),
            ("$tech", techId),
            ("$refresh", refreshId),
            ("$time", finishTime),
            ("$raw", Json(new Dictionary<string, object?>
            {
                ["tech_id"] = techId, ["refresh_id"] = refreshId, ["time"] = finishTime, ["queued"] = queued
            })));
    }

    private int NextResearchOrder(SqliteTransaction tx)
    {
        using var command = connection.CreateCommand();
        command.Transaction = tx;
        command.CommandText = "SELECT COALESCE(MAX(queue_order), 0) + 1 FROM current_research_queue";
        return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    // The project being researched with this id; rows from before the queued flag count as active first.
    private const string ActiveResearchRow = """
                                             (SELECT queue_order FROM current_research_queue
                                              WHERE tech_id = $tech
                                              ORDER BY COALESCE(json_extract(raw_json, '$.queued'), 0), queue_order
                                              LIMIT 1)
                                             """;

    /// <summary>EventProxy.updateInfoList for SC 13011: new or changed commissions merge in, expired ones go.</summary>
    private StateApplyResult ApplyCommissionUpdate(SqliteTransaction tx, DecodedPacketEnvelope packet, JsonElement json)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var items = Items(json, "collection").Concat(Items(json, "collection_list")).ToList();
        foreach (var item in items)
        {
            if (Int(item, "finish_time") == 0 && Int(item, "over_time") is > 0 and var over && over <= now)
            {
                Execute(tx, "DELETE FROM current_commissions WHERE commission_id = $id", ("$id", Int(item, "id")));
            }
            else
            {
                UpsertCommission(tx, item);
            }
        }

        return StateApplyResult.Applied("commissions",
            $"{items.Count.ToString(CultureInfo.InvariantCulture)} commissions merged", packet.DecodedRowId);
    }

    /// <summary>
    /// EventStartCommand.OnStart: the ships go and the commission runs for its collect_time from now. The writer
    /// stores the start (it applies packets as they arrive); the snapshot adds collect_time from commissions.json.
    /// </summary>
    private void ApplyCommissionStart(SqliteTransaction tx, JsonElement request)
    {
        var id = Int(request, "id");
        if (id <= 0) return;
        var ships = Items(request, "ship_id_list").Select(ValueText).ToList();
        var startTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        Execute(
            tx,
            """
            INSERT INTO current_commissions (commission_id, finish_time, over_time, ship_id_list_json, raw_json)
            VALUES ($id, 0, 0, $ships, $raw)
            ON CONFLICT(commission_id) DO UPDATE SET
                finish_time = 0,
                ship_id_list_json = excluded.ship_id_list_json,
                raw_json = json_patch(COALESCE(current_commissions.raw_json, '{}'), excluded.raw_json)
            """,
            ("$id", id),
            ("$ships", Json(ships)),
            ("$raw", Json(new Dictionary<string, object?> { ["id"] = id, ["ship_id_list"] = ships, ["start_time"] = startTime })));
    }

    private StateApplyResult ApplyCommissions(SqliteTransaction tx, DecodedPacketEnvelope packet, JsonElement json)
    {
        var items = Items(json, "collection_list").Concat(Items(json, "collection")).ToList();
        Execute(tx, "DELETE FROM current_commissions");
        foreach (var item in items)
        {
            UpsertCommission(tx, item);
        }

        UpsertSystemStatus(tx, "commissions", $"진행 {items.Count.ToString(CultureInfo.InvariantCulture)}개",
            new Dictionary<string, object?>
            {
                ["items"] = items.Select(JsonDictionary).ToList()
            });
        return StateApplyResult.Applied("commissions",
            $"{items.Count.ToString(CultureInfo.InvariantCulture)} commissions", packet.DecodedRowId);
    }

    private void UpsertCommission(SqliteTransaction tx, JsonElement item)
    {
        var id = Int(item, "id");
        if (id <= 0)
        {
            return;
        }

        Execute(
            tx,
            """
            INSERT INTO current_commissions (commission_id, finish_time, over_time, ship_id_list_json, raw_json)
            VALUES ($id, $finish, $over, $ships, $raw)
            ON CONFLICT(commission_id) DO UPDATE SET
                finish_time = excluded.finish_time,
                over_time = excluded.over_time,
                ship_id_list_json = excluded.ship_id_list_json,
                raw_json = excluded.raw_json
            """,
            ("$id", id),
            ("$finish", Int(item, "finish_time")),
            ("$over", Int(item, "over_time")),
            ("$ships", Json(Items(item, "ship_id_list").Select(ValueText).ToList())),
            ("$raw", Json(item)));
    }

    private StateApplyResult ApplyBuilds(SqliteTransaction tx, DecodedPacketEnvelope packet, JsonElement json,
        bool replaceExisting = true)
    {
        var items = Array(json, "worklist_list").Concat(Array(json, "build_info")).Concat(Array(json, "info_list"))
            .ToList();
        if (replaceExisting)
        {
            Execute(tx, "DELETE FROM current_build_queue");
        }

        var slot = 1;
        foreach (var item in items)
        {
            var rowSlot = FirstPositive(Int(item, "pos"), Int(item, "slot"),
                replaceExisting ? slot : NextBuildSlot(tx));
            Execute(
                tx,
                """
                INSERT INTO current_build_queue (slot, template_id, build_id, start_time, finish_time, status, raw_json)
                VALUES ($slot, $template, $build, $start, $finish, $status, $raw)
                ON CONFLICT(slot) DO UPDATE SET
                    template_id = excluded.template_id,
                    build_id = excluded.build_id,
                    start_time = excluded.start_time,
                    finish_time = excluded.finish_time,
                    status = excluded.status,
                    raw_json = excluded.raw_json
                """,
                ("$slot", rowSlot),
                ("$template", Int(item, "template_id")),
                ("$build", Int(item, "build_id")),
                ("$start", FirstPositive(Int(item, "start_time"), Int(item, "time"))),
                ("$finish", Int(item, "finish_time")),
                ("$status", "active"),
                ("$raw", Json(item)));
            slot++;
        }

        UpsertSystemStatus(tx, "builds", $"{items.Count.ToString(CultureInfo.InvariantCulture)}개 진행",
            new Dictionary<string, object?>
            {
                ["items"] = items.Select(JsonDictionary).ToList(),
                ["worklist_count"] = Int(json, "worklist_count"),
                ["draw_count_1"] = Int(json, "draw_count_1"),
                ["draw_count_10"] = Int(json, "draw_count_10"),
                ["exchange_count"] = Int(json, "exchange_count")
            });
        return StateApplyResult.Applied("builds", $"{items.Count.ToString(CultureInfo.InvariantCulture)} builds",
            packet.DecodedRowId);
    }

    private int NextBuildSlot(SqliteTransaction tx)
    {
        var occupied = new HashSet<int>();
        using var command = connection.CreateCommand();
        command.Transaction = tx;
        command.CommandText = "SELECT slot FROM current_build_queue ORDER BY slot";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            occupied.Add(ToInt(reader.GetValue(0)));
        }

        for (var slot = 1; slot <= 12; slot++)
        {
            if (!occupied.Contains(slot))
            {
                return slot;
            }
        }

        return occupied.Count == 0 ? 1 : occupied.Max() + 1;
    }

    private void DeleteBuildPositions(SqliteTransaction tx, IEnumerable<string> positions)
    {
        foreach (var position in positions
                     .Select(value => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
                         ? parsed
                         : 0)
                     .Where(value => value > 0)
                     .Distinct())
        {
            Execute(tx, "DELETE FROM current_build_queue WHERE slot = $slot", ("$slot", position));
        }
    }
}
