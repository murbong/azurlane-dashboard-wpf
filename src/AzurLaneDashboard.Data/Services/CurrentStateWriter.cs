using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace AzurLaneDashboard.Data.Services;

public sealed record DecodedPacketEnvelope(
    long DecodedRowId,
    string Source,
    string StreamId,
    string Direction,
    int PacketId,
    int Index);

public sealed record StateApplyResult(string Status, string Area, string Detail, long LastDecodedRowId)
{
    public static StateApplyResult Applied(string area, string detail, long rowId) =>
        new("Applied", area, detail, rowId);

    public static StateApplyResult Ignored(string detail, long rowId) => new("Ignored", "none", detail, rowId);

    public static StateApplyResult Deferred(string area, string detail, long rowId) =>
        new("Deferred", area, detail, rowId);

    public static StateApplyResult Failed(string detail, long rowId) => new("Failed", "writer", detail, rowId);
}

public static class StatePacketRegistry
{
    public static readonly IReadOnlyDictionary<int, (int ResponsePacketId, string Area)> RequestResponses =
        new Dictionary<int, (int, string)>
        {
            [12004] = (12005, "ship_retire"),
            [12017] = (12018, "ship_strengthen"),
            [12022] = (12023, "ship_lock"),
            [12027] = (12028, "ship_breakout"),
            [63303] = (63304, "ship_meta_breakout"),
            [12032] = (12033, "ship_propose"),
            [12034] = (12035, "ship_rename"),
            [12038] = (12039, "ship_max_level"),
            [12040] = (12041, "ship_favorite"),
            [12102] = (12103, "fleets"),
            [12104] = (12105, "fleets"),
            [12202] = (12203, "ship_skin"),
            [12006] = (12007, "ship_equipment"),
            [12036] = (12037, "equipment_skin"),
            [14002] = (14003, "equipment_enhance_slot"),
            [14004] = (14005, "equipment_enhance_item"),
            [14006] = (14007, "equipment_compose"),
            [14008] = (14009, "equipment_destroy"),
            [14010] = (14011, "equipment_revert"),
            [14013] = (14014, "equipment_upgrade_slot"),
            [14015] = (14016, "equipment_upgrade_item"),
            [14201] = (14202, "spweapon_equip"),
            [14203] = (14204, "spweapon_upgrade"),
            [14205] = (14206, "spweapon_reforge"),
            [14207] = (14208, "spweapon_confirm_reforge"),
            [14209] = (14210, "spweapon_create"),
            [12002] = (12003, "build_start"),
            [22201] = (22202, "academy_start"),
            [22203] = (22204, "academy_finish"),
            [22014] = (22015, "academy_quick_finish"),
            [63001] = (63002, "research_start"),
            [63003] = (63004, "research_finish"),
            [63005] = (63006, "research_stop"),
            [63013] = (63014, "research_queue_join"),
            [63015] = (63016, "research_queue_finish"),
            [64001] = (64002, "fleet_tech"),
            [64003] = (64004, "fleet_tech"),
            [64005] = (64006, "fleet_tech"),
            [64007] = (64008, "fleet_tech"),
            [64009] = (64010, "fleet_tech"),
            [13003] = (13004, "commission_start"),
            [13005] = (13006, "commission_finish"),
            [13007] = (13008, "commission_giveup"),
            [12008] = (12009, "build_finish"),
            [12043] = (12044, "build_quick_finish"),
            [12045] = (12046, "build_cancel"),
            [25004] = (25005, "commanders"),
            [25006] = (25007, "fleet_commanders"),
            [25008] = (25009, "commanders"),
            [25012] = (25013, "commanders"),
            [25014] = (25015, "commanders"),
            [25016] = (25017, "commanders"),
            [25020] = (25021, "commanders"),
            [33602] = (33603, "world"),
            [20005] = (20006, "tasks"),
            [40001] = (40002, "battle_begin"),
            [40003] = (40004, "battle_result")
        };

    public static readonly IReadOnlyDictionary<int, string> SnapshotAreas = new Dictionary<int, string>
    {
        [11003] = "commander",
        [11004] = "resources",
        [12001] = "ships",
        [12010] = "ships",
        [12101] = "fleets",
        [12106] = "fleets",
        [12024] = "builds",
        [12026] = "ships",
        [12030] = "ships",
        [12042] = "ships",
        [13002] = "commissions",
        [13010] = "commissions",
        [13011] = "commissions",
        [14001] = "equipment",
        [14101] = "equipment_skins",
        [14200] = "spweapons",
        [15001] = "items",
        [22001] = "academy",
        [22013] = "academy",
        [25001] = "commanders",
        [25039] = "commanders",
        [20001] = "tasks",
        [20002] = "tasks",
        [20003] = "tasks",
        [20004] = "tasks",
        [20012] = "tasks",
        [33001] = "world",
        [33601] = "world",
        [63000] = "research",
        [64000] = "fleet_tech"
    };

    public static readonly IReadOnlyDictionary<int, (int RequestPacketId, string Area)> ResponseRequests =
        RequestResponses.ToDictionary(item => item.Value.ResponsePacketId, item => (item.Key, item.Value.Area));
}

public sealed partial class CurrentStateWriter
{
    private const string SchemaVersion = "7";
    private readonly SqliteConnection connection;

    public CurrentStateWriter(SqliteConnection connection)
    {
        this.connection = connection;
        EnsureSchema();
        EnsureMetaBossFightTable();
        EnsureCommanderTable();
        EnsureWorldTables();
        EnsureTaskTable();
        DropShipsMissingFromLogin();
    }

    /// <summary>
    /// Ships stored before the newest login list (SC 12001) that it did not carry: retired or used up while the
    /// writer still kept every ship it had ever seen. Each login now clears them; this tidies older databases.
    /// </summary>
    private void DropShipsMissingFromLogin()
    {
        using var tx = connection.BeginTransaction();
        var stale = new List<string>();
        using (var exists = connection.CreateCommand())
        {
            exists.Transaction = tx;
            exists.CommandText = "SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = 'decoded_packets'";
            if (exists.ExecuteScalar() is null) return;
        }

        using (var command = connection.CreateCommand())
        {
            command.Transaction = tx;
            command.CommandText = """
                                  SELECT ship_uid FROM current_ships
                                  WHERE snapshot_rowid > 0 AND snapshot_rowid <
                                      (SELECT MAX(rowid) FROM decoded_packets WHERE packet_id = 12001)
                                  """;
            using var reader = command.ExecuteReader();
            while (reader.Read()) stale.Add(reader.GetString(0));
        }

        foreach (var shipUid in stale) DeleteCurrentShip(tx, shipUid);
        tx.Commit();
    }

    public StateApplyResult ApplyDecodedPacket(
        SqliteTransaction transaction,
        DecodedPacketEnvelope packet,
        JsonElement json)
    {
        try
        {
            StateApplyResult result;
            if (packet.Direction == "CS" &&
                StatePacketRegistry.RequestResponses.TryGetValue(packet.PacketId, out var response))
            {
                StorePendingRequest(transaction, packet, response.ResponsePacketId, response.Area, json);
                RecordMetaBossFight(transaction, packet, json);
                result = StateApplyResult.Applied(response.Area, $"pending SC_{response.ResponsePacketId}",
                    packet.DecodedRowId);
            }
            else if (packet.Direction == "SC" &&
                     StatePacketRegistry.ResponseRequests.TryGetValue(packet.PacketId, out var requestInfo))
            {
                var request = TakePendingRequest(transaction, packet, packet.PacketId);
                result = request.HasValue
                    ? ApplyResponsePacket(transaction, packet, json, request.Value, requestInfo.Area)
                    // A task reward still feeds oil and courtyard food, so it reloads without its request.
                    : packet.PacketId == 20006
                        ? StateApplyResult.Applied("live_resources", "oil/food input", packet.DecodedRowId)
                        : StateApplyResult.Deferred(requestInfo.Area, "matching CS request missing", packet.DecodedRowId);
            }
            else if (packet.Direction == "SC" &&
                     StatePacketRegistry.SnapshotAreas.ContainsKey(packet.PacketId))
            {
                result = ApplySnapshotPacket(transaction, packet, json);
            }
            else if (packet.PacketId is 13000 or 13001 or 13102 or 13104 or 13105 or 33104 or 33114)
            {
                Execute(
                    transaction,
                    """
                    INSERT INTO current_chapter_state (key, value_json, updated_rowid, updated_at)
                    VALUES ($key, $value, $rowid, $now)
                    ON CONFLICT(key) DO UPDATE SET
                        value_json = excluded.value_json,
                        updated_rowid = excluded.updated_rowid,
                        updated_at = excluded.updated_at
                    """,
                    ("$key", $"{packet.Direction}_{packet.PacketId}"),
                    ("$value", Json(json)),
                    ("$rowid", packet.DecodedRowId),
                    ("$now", NowText()));
                result = StateApplyResult.Applied("chapter", "chapter/world state saved", packet.DecodedRowId);
            }
            else if (packet.Direction == "SC" && packet.PacketId == 63316)
            {
                result = StateApplyResult.Applied("ships",
                    $"{ApplyMetaSkills(transaction, json).ToString(System.Globalization.CultureInfo.InvariantCulture)} META skills",
                    packet.DecodedRowId);
            }
            else if (packet.Direction == "SC" && packet.PacketId is 11014 or 15003 or 19001 or 19003 or 19005 or 19027 or 20006 or 22012 or 40008)
            {
                // No table of their own: the snapshot replays them for oil, courtyard food and ship EXP, so a
                // reload must follow.
                result = StateApplyResult.Applied("live_resources", "oil/food input", packet.DecodedRowId);
            }
            else
            {
                result = StateApplyResult.Ignored("no state effect", packet.DecodedRowId);
            }

            if (packet.Direction == "SC" && EquipmentDropPackets.Contains(packet.PacketId) && Int(json, "result") == 0)
            {
                ApplyEquipmentDrops(transaction, json);
                if (result.Status == "Ignored")
                {
                    result = StateApplyResult.Applied("equipment", "rewards checked for equipment", packet.DecodedRowId);
                }
            }

            UpdateMeta(transaction, result);
            RecordApplyLog(transaction, packet, result);
            return result;
        }
        catch (Exception ex) when (ex is SqliteException or JsonException or InvalidOperationException)
        {
            var result = StateApplyResult.Failed(ex.Message.Length > 1000 ? ex.Message[..1000] : ex.Message,
                packet.DecodedRowId);
            UpdateMeta(transaction, result);
            RecordApplyLog(transaction, packet, result);
            return result;
        }
    }

}
