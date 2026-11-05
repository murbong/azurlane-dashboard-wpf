using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using AzurLaneDashboard.Core.Models;
using AzurLaneDashboard.Core.Services;
using Microsoft.Data.Sqlite;

namespace AzurLaneDashboard.Data.Services;

public sealed partial class NativeSnapshotService
{
    private static SystemsSnapshot BuildSystems(List<PacketRecord> packets, JsonElement summary, List<ShipState> ships,
        JsonElement skillMap)
    {
        var byUid = ships.ToDictionary(ship => ship.Uid, StringComparer.Ordinal);
        var academy = BuildAcademy(packets, byUid, skillMap);
        var research = BuildResearch(packets);
        var commissions = BuildCommissions(packets, summary, byUid);
        var builds = BuildBuilds(packets);
        return new SystemsSnapshot
        {
            Statuses = new List<SystemStatusRow>
            {
                new()
                {
                    Name = "스킬 교실", StatusLabel = academy.StatusLabel, Detail = academy.Detail, Kind = academy.Kind
                },
                new()
                {
                    Name = "연구", StatusLabel = research.StatusLabel, Detail = research.Detail, Kind = research.Kind
                },
                new()
                {
                    Name = "의뢰", StatusLabel = commissions.StatusLabel, Detail = commissions.Detail,
                    Kind = commissions.Kind
                },
                new() { Name = "건조", StatusLabel = builds.StatusLabel, Detail = builds.Detail, Kind = builds.Kind }
            },
            AcademyRooms = academy.Rooms,
            ResearchQueue = research.Queue,
            Commissions = commissions.Items,
            Builds = builds.Items
        };
    }

    private static AcademySystem BuildAcademy(List<PacketRecord> packets, Dictionary<string, ShipState> ships,
        JsonElement skillMap)
    {
        var latest = packets.LastOrDefault(packet => packet.PacketId == 22001);
        if (latest is null)
        {
            return new AcademySystem("미포착", "로그인 캡처 필요", "bad", new List<AcademyRoomRow>());
        }

        var classes = Array(latest.Json, "skill_class_list").ToList();
        var finished = classes.Count(room => IsFinished(JsonInt(room, "finish_time")));
        var rooms = classes
            .Select(room =>
            {
                var uid = JsonString(room, "ship_id");
                var ship = ships.GetValueOrDefault(uid);
                return new AcademyRoomRow
                {
                    RoomId = JsonInt(room, "room_id"),
                    ShipUid = uid,
                    ShipName = ship?.DisplayName ?? uid,
                    ShipIconUrl = ship?.IconUrl ?? "",
                    SkillPosition = JsonString(room, "skill_pos"),
                    SkillName = AcademySkillName(skillMap, JsonString(room, "skill_pos")),
                    FinishAt = FormatEpoch(JsonInt(room, "finish_time")),
                    Remaining = RemainingText(JsonInt(room, "finish_time")),
                    Exp = JsonInt(room, "exp")
                };
            })
            .OrderBy(row => row.FinishAt, StringComparer.Ordinal)
            .ToList();
        var max = JsonInt(latest.Json, "skill_class_num");
        return new AcademySystem(finished > 0 ? FinishedStatusLabel : "decoded",
            AcademyStatusDetail(rooms.Count, finished, max > 0 ? max : rooms.Count),
            finished > 0 ? FinishedStatusKind : "good", rooms);
    }

    private static ResearchSystem BuildResearch(List<PacketRecord> packets)
    {
        var latestFull = packets.LastOrDefault(packet => packet.PacketId == 63000);
        var latestRelated = packets.LastOrDefault(packet => packet.PacketId is 63000 or 63002 or 63004 or 63014);
        if (latestRelated is null)
        {
            return new ResearchSystem("미포착", "연구 화면/로그인 캡처 필요", "bad", new List<ResearchQueueRow>());
        }

        var queue = new Dictionary<int, JsonElement>();
        if (latestFull is not null)
        {
            foreach (var item in Array(latestFull.Json, "queue"))
            {
                var id = FirstPositive(JsonInt(item, "id"), JsonInt(item, "tech_id"));
                if (id > 0)
                {
                    queue[id] = item;
                }
            }
        }

        var requests = packets.Where(packet => packet.PacketId is 63001 or 63003).ToList();
        foreach (var packet in packets
                     .Where(packet => packet.PacketId is 63002 or 63004 &&
                                      (latestFull is null || packet.RowId > latestFull.RowId))
                     .OrderBy(packet => packet.RowId))
        {
            if (JsonInt(packet.Json, "result") != 0)
            {
                continue;
            }

            var request = MatchingBattleRequest(packet, requests);
            var techId = JsonInt(request, "tech_id");
            if (techId <= 0)
            {
                continue;
            }

            if (packet.PacketId == 63002)
            {
                queue[techId] = MergeObject(request, new Dictionary<string, object?>
                {
                    ["id"] = techId,
                    ["time"] = JsonInt(packet.Json, "time")
                });
            }
            else if (packet.PacketId == 63004)
            {
                queue.Remove(techId);
            }
        }

        var rows = queue.Values
            .OrderBy(item => FirstPositive(JsonInt(item, "time"), JsonInt(item, "finish_time")))
            .Select((item, index) => new ResearchQueueRow
            {
                Order = index + 1,
                Id = FirstNonEmpty(JsonString(item, "id"), JsonString(item, "tech_id")),
                FinishAt = FormatEpoch(FirstPositive(JsonInt(item, "time"), JsonInt(item, "finish_time"))),
                Remaining = RemainingText(FirstPositive(JsonInt(item, "time"), JsonInt(item, "finish_time")))
            })
            .ToList();
        var finished = queue.Values.Count(item =>
            IsFinished(FirstPositive(JsonInt(item, "time"), JsonInt(item, "finish_time"))));
        return new ResearchSystem(finished > 0 ? FinishedStatusLabel : "decoded",
            ResearchStatusDetail(rows.Count, finished), finished > 0 ? FinishedStatusKind : "good", rows);
    }

    private static CommissionSystem BuildCommissions(List<PacketRecord> packets, JsonElement summary,
        Dictionary<string, ShipState> ships)
    {
        var latestFull = packets.LastOrDefault(packet => packet.PacketId == 13002);
        var latestRelated = packets.LastOrDefault(packet => packet.PacketId is 13002 or 13010 or 13011 or 13006);
        if (latestRelated is null)
        {
            return new CommissionSystem("미포착", "의뢰 화면/로그인 캡처 필요", "bad", new List<CommissionRow>());
        }

        var collection = new Dictionary<int, JsonElement>();
        if (latestFull is not null)
        {
            foreach (var item in Array(latestFull.Json, "collection_list"))
            {
                var id = JsonInt(item, "id");
                if (id > 0)
                {
                    collection[id] = item;
                }
            }
        }

        var requests = packets.Where(packet => packet.PacketId == 13005).ToList();
        foreach (var packet in packets
                     .Where(packet => packet.PacketId is 13010 or 13011 or 13006 &&
                                      (latestFull is null || packet.RowId > latestFull.RowId))
                     .OrderBy(packet => packet.RowId))
        {
            if (packet.PacketId == 13006)
            {
                if (JsonInt(packet.Json, "result") == 0)
                {
                    var request = MatchingBattleRequest(packet, requests);
                    var id = JsonInt(request, "id");
                    if (id > 0)
                    {
                        collection.Remove(id);
                    }
                }

                foreach (var item in Array(packet.Json, "new_collection"))
                {
                    var id = JsonInt(item, "id");
                    if (id > 0)
                    {
                        collection[id] = item;
                    }
                }

                continue;
            }

            foreach (var item in Array(packet.Json, "collection_list").Concat(Array(packet.Json, "collection")))
            {
                var id = JsonInt(item, "id");
                if (id > 0)
                {
                    collection[id] = item;
                }
            }
        }

        if (latestFull is null && collection.Count == 0)
        {
            foreach (var item in Array(latestRelated.Json, "collection_list")
                         .Concat(Array(latestRelated.Json, "collection"))
                         .Concat(Array(latestRelated.Json, "new_collection")))
            {
                var id = JsonInt(item, "id");
                if (id > 0)
                {
                    collection[id] = item;
                }
            }
        }

        var rows = collection.Values
            .OrderBy(item => JsonInt(item, "finish_time"))
            .Select(item =>
            {
                var names = Array(item, "ship_id_list")
                    .Select(ValueText)
                    .Select(uid => ships.GetValueOrDefault(uid)?.DisplayName ?? uid);
                return new CommissionRow
                {
                    Id = JsonString(item, "id"),
                    FinishAt = FormatEpoch(JsonInt(item, "finish_time")),
                    Remaining = RemainingText(JsonInt(item, "finish_time")),
                    ShipNames = string.Join(", ", names)
                };
            })
            .ToList();
        var maxTeamSource = latestFull is null ? latestRelated.Json : latestFull.Json;
        var maxTeam = JsonInt(maxTeamSource, "max_team");
        return new CommissionSystem("decoded", $"진행 {rows.Count}개 / 슬롯 {(maxTeam > 0 ? maxTeam : rows.Count)}", "good",
            rows);
    }

    private static BuildSystem BuildBuilds(List<PacketRecord> packets)
    {
        var latestFull = packets.LastOrDefault(packet => packet.PacketId == 12024);
        var latestRelated = packets.LastOrDefault(packet => packet.PacketId is 12024 or 12003 or 12009 or 12044 or 12046);
        if (latestRelated is null)
        {
            return new BuildSystem("미포착", "건조 화면/로그인 캡처 필요", "bad", new List<BuildQueueRow>());
        }

        var queue = new Dictionary<int, JsonElement>();
        if (latestFull is not null)
        {
            var slot = 1;
            foreach (var item in Array(latestFull.Json, "worklist_list").Concat(Array(latestFull.Json, "build_info")))
            {
                queue[slot++] = MergeObject(item, new Dictionary<string, object?> { ["pos"] = slot - 1 });
            }
        }

        var finishRequests = packets.Where(packet => packet.PacketId == 12008).ToList();
        foreach (var packet in packets
                     .Where(packet => packet.PacketId is 12003 or 12009 or 12044 &&
                                      (latestFull is null || packet.RowId > latestFull.RowId))
                     .OrderBy(packet => packet.RowId))
        {
            if (packet.PacketId == 12003 && JsonInt(packet.Json, "result") == 0)
            {
                foreach (var item in Array(packet.Json, "build_info"))
                {
                    var slot = FirstFreeSlot(queue);
                    queue[slot] = MergeObject(item, new Dictionary<string, object?> { ["pos"] = slot });
                }
            }
            else if (packet.PacketId == 12009 && JsonInt(packet.Json, "result") == 0)
            {
                var request = MatchingBattleRequest(packet, finishRequests);
                foreach (var pos in Array(packet.Json, "pos_list").Concat(Array(request, "pos_list")).Select(IntValue)
                             .Where(value => value > 0))
                {
                    queue.Remove(pos);
                }
            }
            else if (packet.PacketId == 12044)
            {
                foreach (var pos in Array(packet.Json, "info_list")
                             .Select(item => JsonInt(item, "pos"))
                             .Where(value => value > 0))
                {
                    queue.Remove(pos);
                }
            }
        }

        var rows = queue.OrderBy(pair => pair.Key)
            .Select(pair => pair.Value)
            .Select((item, index) =>
            {
                var finish = JsonInt(item, "finish_time");
                return new BuildQueueRow
                {
                    Slot = FirstPositive(JsonInt(item, "pos"), index + 1).ToString(CultureInfo.InvariantCulture),
                    StatusLabel = finish > 0 && finish <= NowEpoch() ? "완료 대기" : "진행 중",
                    StartedAt = FormatEpoch(FirstPositive(JsonInt(item, "start_time"), JsonInt(item, "time"))),
                    FinishAt = FormatEpoch(finish),
                    Remaining = RemainingText(finish),
                    ResultName = ""
                };
            })
            .ToList();
        var finished = FinishedBuildCount(rows);
        return new BuildSystem(finished > 0 ? FinishedStatusLabel : "decoded", BuildQueueStatusDetail(rows),
            finished > 0 ? FinishedStatusKind : "good", rows);
    }

    private static JsonElement MergeObject(JsonElement source, IReadOnlyDictionary<string, object?> values)
    {
        var merged = source.ValueKind == JsonValueKind.Object
            ? JsonSerializer.Deserialize<Dictionary<string, object?>>(source.GetRawText()) ??
              new Dictionary<string, object?>()
            : new Dictionary<string, object?>();
        foreach (var (key, value) in values)
        {
            merged[key] = value;
        }

        return JsonSerializer.SerializeToElement(merged).Clone();
    }

    private static int FirstFreeSlot(IReadOnlyDictionary<int, JsonElement> queue)
    {
        for (var slot = 1; slot <= 12; slot++)
        {
            if (!queue.ContainsKey(slot))
            {
                return slot;
            }
        }

        return queue.Count == 0 ? 1 : queue.Keys.Max() + 1;
    }

    private static List<ResourceRow> BuildResources(List<PacketRecord> packets, JsonElement summary,
        JsonElement resourceMap)
    {
        var resources = packets
            .Where(packet => packet.PacketId is 11003 or 11004)
            .SelectMany(packet => Items(packet.Json, "resource_list"))
            .Select(resource => (JsonInt(resource, "type"), JsonInt(resource, "num")));
        return ResourceRows(resources, resourceMap);
    }

    private static Dictionary<string, string> BuildCommanderProfile(List<PacketRecord> packets)
    {
        var latest = packets.LastOrDefault(packet => packet.PacketId == 11003);
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (latest is null)
        {
            return result;
        }

        foreach (var key in new[]
                     { "ship_bag_max", "equip_bag_max", "commander_bag_max", "ship_count", "level", "name" })
        {
            result[key] = JsonString(latest.Json, key);
        }

        return result;
    }
}
