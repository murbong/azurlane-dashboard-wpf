using System.Text.Json;
using AzurLaneDashboard.Core.Models;
using Microsoft.Data.Sqlite;

namespace AzurLaneDashboard.Data.Services;

public sealed partial class NativeSnapshotService
{
    private static SystemsSnapshot BuildCurrentSystems(SqliteConnection connection,
        IReadOnlyDictionary<string, ShipState> ships, JsonElement skillMap, JsonElement commissionMap)
    {
        var academyRooms = ReadCurrentAcademyRooms(connection, ships, skillMap, out var finishedAcademyRooms);
        var researchQueue = ReadCurrentResearchQueue(connection, out var finishedResearch);
        var builds = ReadCurrentBuilds(connection);
        var statuses = new List<SystemStatusRow>();
        if (TableExists(connection, "current_system_statuses"))
        {
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT * FROM current_system_statuses ORDER BY system_key";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var key = Text(reader, "system_key");
                // Event packets (22013 proficiency, 63014 refresh, build pickups) overwrite the stored
                // text, so these cards summarise the same rows the systems page lists.
                var (detail, finished) = key switch
                {
                    "academy" => (AcademyStatusDetail(academyRooms.Count, finishedAcademyRooms), finishedAcademyRooms),
                    "research" => (ResearchStatusDetail(researchQueue.Count, finishedResearch), finishedResearch),
                    "builds" => (BuildQueueStatusDetail(builds), FinishedBuildCount(builds)),
                    _ => (CurrentSystemDetail(Text(reader, "detail")), 0)
                };
                statuses.Add(new SystemStatusRow
                {
                    Name = CurrentSystemName(key),
                    StatusLabel = finished > 0
                        ? FinishedStatusLabel
                        : CurrentSystemStatusLabel(Text(reader, "status_label")),
                    Detail = detail,
                    Kind = finished > 0 ? FinishedStatusKind : Text(reader, "status_kind")
                });
            }
        }

        return new SystemsSnapshot
        {
            Statuses = statuses,
            AcademyRooms = academyRooms,
            ResearchQueue = researchQueue,
            Commissions = ReadCurrentCommissions(connection, ships, commissionMap),
            Builds = builds
        };
    }

    private static List<AcademyRoomRow> ReadCurrentAcademyRooms(SqliteConnection connection,
        IReadOnlyDictionary<string, ShipState> ships, JsonElement skillMap, out int finishedRooms)
    {
        var rows = new List<AcademyRoomRow>();
        finishedRooms = 0;
        if (!TableExists(connection, "current_academy_rooms"))
        {
            return rows;
        }

        using var command = connection.CreateCommand();
        command.CommandText = """
                              SELECT room.room_id, room.ship_uid, room.skill_pos, room.finish_time, room.exp,
                                     COALESCE(NULLIF(ship.display_name, ''), NULLIF(ship.custom_name, ''), room.ship_uid) AS ship_name
                              FROM current_academy_rooms room
                              LEFT JOIN current_ships ship ON ship.ship_uid = room.ship_uid
                              ORDER BY room.finish_time, room.room_id
                              """;
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var finish = Int(reader, "finish_time");
            if (IsFinished(finish))
            {
                finishedRooms++;
            }

            var uid = Text(reader, "ship_uid");
            ships.TryGetValue(uid, out var ship);
            rows.Add(new AcademyRoomRow
            {
                RoomId = Int(reader, "room_id"),
                ShipUid = uid,
                ShipName = ship?.DisplayName ?? Text(reader, "ship_name"),
                ShipIconUrl = ship?.IconUrl ?? "",
                SkillPosition = Text(reader, "skill_pos"),
                SkillName = AcademySkillName(skillMap, Text(reader, "skill_pos")),
                FinishAt = FormatEpoch(finish),
                Remaining = RemainingText(finish),
                Exp = Int(reader, "exp")
            });
        }

        return rows;
    }

    // Finished lessons, research, commissions and builds stay listed as 완료 until the game reports them
    // collected: the slot is still occupied, and the player needs to know to claim it. The activity card
    // then earns a labelled badge instead of the quiet routine dot.
    private const string FinishedStatusLabel = "완료 대기";
    private const string FinishedStatusKind = "info";

    private static string WithFinishedCount(string detail, int finished) =>
        finished > 0 ? $"{detail} · 완료 {finished}개" : detail;

    private static string AcademySkillName(JsonElement skillMap, string skillId) =>
        int.TryParse(skillId, out var id) ? FirstNonEmpty(JsonString(MapObject(skillMap, id), "name"), skillId) : skillId;

    private static int FinishedBuildCount(IReadOnlyCollection<BuildQueueRow> builds) =>
        builds.Count(row => row.StatusLabel != "진행 중");

    private static string AcademyStatusDetail(int usedRooms, int finishedRooms, int capacity = 0) =>
        WithFinishedCount(capacity > 0 ? $"{usedRooms}/{capacity}개 사용 중" : $"{usedRooms}개 사용 중",
            finishedRooms);

    private static string ResearchStatusDetail(int queued, int finished) =>
        WithFinishedCount($"큐 {queued}개", finished);

    private static string BuildQueueStatusDetail(IReadOnlyCollection<BuildQueueRow> builds) =>
        WithFinishedCount($"{builds.Count - FinishedBuildCount(builds)}개 진행", FinishedBuildCount(builds));

    private static List<ResearchQueueRow> ReadCurrentResearchQueue(SqliteConnection connection, out int finishedItems)
    {
        var rows = new List<ResearchQueueRow>();
        finishedItems = 0;
        if (!TableExists(connection, "current_research_queue"))
        {
            return rows;
        }

        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT queue_order, tech_id, finish_time FROM current_research_queue ORDER BY queue_order";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var finish = Int(reader, "finish_time");
            if (IsFinished(finish))
            {
                finishedItems++;
            }

            rows.Add(new ResearchQueueRow
            {
                Order = Int(reader, "queue_order"),
                Id = Text(reader, "tech_id"),
                FinishAt = FormatEpoch(finish),
                Remaining = RemainingText(finish)
            });
        }

        return rows;
    }

    /// <summary>
    /// Commissions with their finish time; one started this session (SC 13004 has no time) finishes collect_time
    /// (commissions.json) after its start, as EventStartCommand counts it.
    /// </summary>
    private static List<CommissionRow> ReadCurrentCommissions(SqliteConnection connection,
        IReadOnlyDictionary<string, ShipState> ships, JsonElement commissionMap)
    {
        var rows = new List<CommissionRow>();
        if (!TableExists(connection, "current_commissions"))
        {
            return rows;
        }

        var shipNameMap = CurrentShipNameMap(connection, ships);
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT commission_id, finish_time, ship_id_list_json, raw_json FROM current_commissions ORDER BY finish_time, commission_id";
        using var reader = command.ExecuteReader();
        var timed = new List<(long Finish, int Id, CommissionRow Row)>();
        while (reader.Read())
        {
            long finish = Int(reader, "finish_time");
            var id = Int(reader, "commission_id");
            if (finish <= 0 && JsonLong(ParseJson(Text(reader, "raw_json")), "start_time") is > 0 and var started &&
                JsonInt(MapObject(commissionMap, id), "collect_time") is > 0 and var collectTime)
            {
                finish = started + collectTime;
            }

            timed.Add((finish, id, new CommissionRow
            {
                Id = Text(reader, "commission_id"),
                FinishAt = FormatEpoch((int)finish),
                Remaining = RemainingText((int)finish),
                ShipNames = CurrentShipNames(shipNameMap, Text(reader, "ship_id_list_json"))
            }));
        }

        rows.AddRange(timed.OrderBy(item => item.Finish).ThenBy(item => item.Id).Select(item => item.Row));
        return rows;
    }

    private static List<BuildQueueRow> ReadCurrentBuilds(SqliteConnection connection)
    {
        var rows = new List<BuildQueueRow>();
        if (!TableExists(connection, "current_build_queue"))
        {
            return rows;
        }

        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT slot, start_time, finish_time, status, raw_json FROM current_build_queue ORDER BY slot";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var finish = Int(reader, "finish_time");
            rows.Add(new BuildQueueRow
            {
                Slot = Text(reader, "slot"),
                StatusLabel = finish > 0 && finish <= NowEpoch() ? "완료 대기" : "진행 중",
                StartedAt = FormatEpoch(Int(reader, "start_time")),
                FinishAt = FormatEpoch(finish),
                Remaining = RemainingText(finish),
                ResultName = JsonString(ParseJson(Text(reader, "raw_json")), "result_name")
            });
        }

        return rows;
    }

    private static Dictionary<string, string> CurrentShipNameMap(SqliteConnection connection,
        IReadOnlyDictionary<string, ShipState> ships)
    {
        var names = ships
            .Where(ship => !string.IsNullOrWhiteSpace(ship.Key))
            .ToDictionary(ship => ship.Key, ship => ship.Value.DisplayName, StringComparer.Ordinal);
        if (!TableExists(connection, "current_ships"))
        {
            return names;
        }

        using var command = connection.CreateCommand();
        command.CommandText = """
                              SELECT ship_uid,
                                     COALESCE(NULLIF(display_name, ''), NULLIF(custom_name, ''), ship_uid) AS ship_name
                              FROM current_ships
                              """;
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var uid = Text(reader, "ship_uid");
            if (!names.ContainsKey(uid))
            {
                names[uid] = Text(reader, "ship_name");
            }
        }

        return names;
    }

    private static string CurrentShipNames(IReadOnlyDictionary<string, string> nameMap, string rawJson)
    {
        var ids = ParseJson(rawJson);
        if (ids.ValueKind != JsonValueKind.Array)
        {
            return "";
        }

        var names = new List<string>();
        foreach (var item in ids.EnumerateArray())
        {
            var uid = ValueText(item);
            if (string.IsNullOrWhiteSpace(uid))
            {
                continue;
            }

            names.Add(nameMap.GetValueOrDefault(uid, uid));
        }

        return string.Join(", ", names);
    }

    private static string CurrentSystemName(string key) => key switch
    {
        "academy" => "스킬 교실",
        "research" => "연구",
        "commissions" => "의뢰",
        "builds" => "건조",
        "resources" => "자원",
        "fleet_tech" => "함대 기술",
        "ships" => "함선",
        "equipment" => "장비",
        "spweapons" => "특수장비",
        _ => key
    };

    // Display-only wording; the stored status values stay unchanged.
    private static string CurrentSystemStatusLabel(string label) => label switch
    {
        "incremental" => "실시간 반영",
        _ => label
    };

    private static readonly (string Token, string Label)[] CurrentSystemDetailTokens =
    [
        ("groups=", "그룹 "),
        ("completed=", "완료 "),
        ("studying=", "연구 중 "),
        ("unclaimed=", "미수령 "),
        ("settings=", "설정 "),
        ("refresh ", "갱신 목록 ")
    ];

    private static string CurrentSystemDetail(string detail)
    {
        if (string.IsNullOrWhiteSpace(detail))
        {
            return detail;
        }

        var text = detail;
        foreach (var (token, label) in CurrentSystemDetailTokens)
        {
            text = text.Replace(token, label, StringComparison.Ordinal);
        }

        return text.Replace(", ", " · ", StringComparison.Ordinal);
    }
}
