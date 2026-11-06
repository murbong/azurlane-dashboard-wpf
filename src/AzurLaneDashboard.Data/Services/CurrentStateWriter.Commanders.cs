using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace AzurLaneDashboard.Data.Services;

/// <summary>
/// Meowfficers (지휘냥). SC 25001 lists every cat at login; the later packets change one cat at a time.
/// Without this table the cats would live only in decoded_packets, which keeps the latest 20,000 packets.
/// </summary>
public sealed partial class CurrentStateWriter
{
    /// <summary>
    /// What raised a cat's EXP since the last login list: feedings (SC 25009 carries only a result; the client
    /// works the level out from the eaten cats, kept here as they were) and battles (SC 40004 commander_exp, a
    /// gain). The snapshot replays them on the stored level; the cat house (SC 25027) and the next login list
    /// carry the true level and replace them.
    /// </summary>
    private bool commanderEventsCreated;

    private void EnsureCommanderEventTable()
    {
        using (var exists = connection.CreateCommand())
        {
            exists.CommandText = "SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = 'current_commander_events'";
            commanderEventsCreated = exists.ExecuteScalar() is null;
        }

        using var create = connection.CreateCommand();
        create.CommandText = """
                             CREATE TABLE IF NOT EXISTS current_commander_events (
                                 event_id INTEGER PRIMARY KEY AUTOINCREMENT,
                                 kind TEXT,
                                 target_uid TEXT,
                                 exp INTEGER,
                                 materials_json TEXT,
                                 updated_rowid INTEGER
                             );
                             """;
        create.ExecuteNonQuery();
    }

    /// <summary>
    /// The battles and cat-house answers still in decoded_packets after the latest login list fill the new event
    /// table once, so a database upgraded mid-session does not lose them.
    /// </summary>
    private void BackfillCommanderEvents()
    {
        if (!commanderEventsCreated) return;
        commanderEventsCreated = false;
        using (var exists = connection.CreateCommand())
        {
            exists.CommandText = "SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = 'decoded_packets'";
            if (exists.ExecuteScalar() is null) return;
        }

        var packets = new List<(long RowId, int PacketId, string Json)>();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                                  SELECT rowid, packet_id, json FROM decoded_packets
                                  WHERE direction = 'SC' AND packet_id IN (40004, 25027) AND rowid >
                                      (SELECT COALESCE(MAX(rowid), 0) FROM decoded_packets WHERE packet_id = 25001 AND direction = 'SC')
                                  ORDER BY rowid
                                  """;
            using var reader = command.ExecuteReader();
            while (reader.Read()) packets.Add((reader.GetInt64(0), reader.GetInt32(1), reader.GetString(2)));
        }

        if (packets.Count == 0) return;
        using var tx = connection.BeginTransaction();
        foreach (var (rowId, packetId, text) in packets)
        {
            using var document = JsonDocument.Parse(text);
            if (packetId == 40004) RecordCommanderBattleExp(tx, rowId, document.RootElement);
            else ApplyCommanderHome(tx, rowId, document.RootElement);
        }

        tx.Commit();
    }

    private void EnsureCommanderTable()
    {
        bool existed;
        using (var exists = connection.CreateCommand())
        {
            exists.CommandText = "SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = 'current_commanders'";
            existed = exists.ExecuteScalar() is not null;
        }

        if (existed) return;
        using (var create = connection.CreateCommand())
        {
            create.CommandText = """
                                 CREATE TABLE current_commanders (
                                     commander_uid TEXT PRIMARY KEY,
                                     template_id INTEGER,
                                     level INTEGER,
                                     exp INTEGER,
                                     locked INTEGER,
                                     name TEXT,
                                     abilities_json TEXT,
                                     ability_origin_json TEXT,
                                     skills_json TEXT,
                                     used_pt INTEGER,
                                     snapshot_rowid INTEGER,
                                     raw_json TEXT
                                 );
                                 """;
            create.ExecuteNonQuery();
        }

        BackfillCommanders();
    }

    /// <summary>The latest login list still in decoded_packets fills the new table once.</summary>
    private void BackfillCommanders()
    {
        using (var exists = connection.CreateCommand())
        {
            exists.CommandText = "SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = 'decoded_packets'";
            if (exists.ExecuteScalar() is null) return;
        }

        long rowId;
        string json;
        using (var command = connection.CreateCommand())
        {
            command.CommandText =
                "SELECT rowid, json FROM decoded_packets WHERE packet_id = 25001 AND direction = 'SC' ORDER BY rowid DESC LIMIT 1";
            using var reader = command.ExecuteReader();
            if (!reader.Read()) return;
            rowId = reader.GetInt64(0);
            json = reader.GetString(1);
        }

        using var document = JsonDocument.Parse(json);
        using var tx = connection.BeginTransaction();
        ReplaceCommanders(tx, rowId, document.RootElement);
        tx.Commit();
    }

    private StateApplyResult ApplyCommanderSnapshot(SqliteTransaction tx, DecodedPacketEnvelope packet, JsonElement json)
    {
        if (packet.PacketId == 25001)
        {
            var count = ReplaceCommanders(tx, packet.DecodedRowId, json);
            return StateApplyResult.Applied("commanders",
                $"meowfficers {count.ToString(CultureInfo.InvariantCulture)}", packet.DecodedRowId);
        }

        var updated = 0;
        foreach (var commander in Items(json, "commander_list"))
        {
            UpsertCommander(tx, packet.DecodedRowId, commander);
            updated++;
        }

        return StateApplyResult.Applied("commanders",
            $"meowfficers updated {updated.ToString(CultureInfo.InvariantCulture)}", packet.DecodedRowId);
    }

    private int ReplaceCommanders(SqliteTransaction tx, long rowId, JsonElement json)
    {
        Execute(tx, "DELETE FROM current_commanders");
        // The login list carries the levels the feedings and battles reached.
        Execute(tx, "DELETE FROM current_commander_events");
        var count = 0;
        foreach (var commander in Items(json, "commanders"))
        {
            UpsertCommander(tx, rowId, commander);
            count++;
        }

        return count;
    }

    private void UpsertCommander(SqliteTransaction tx, long rowId, JsonElement commander)
    {
        var uid = Text(commander, "id");
        if (uid.Length == 0 || uid == "0") return;
        Execute(
            tx,
            """
            INSERT INTO current_commanders (
                commander_uid, template_id, level, exp, locked, name, abilities_json, ability_origin_json,
                skills_json, used_pt, snapshot_rowid, raw_json)
            VALUES ($uid, $template, $level, $exp, $locked, $name, $abilities, $origin, $skills, $pt, $rowid, $raw)
            ON CONFLICT(commander_uid) DO UPDATE SET
                template_id = excluded.template_id,
                level = excluded.level,
                exp = excluded.exp,
                locked = excluded.locked,
                name = excluded.name,
                abilities_json = excluded.abilities_json,
                ability_origin_json = excluded.ability_origin_json,
                skills_json = excluded.skills_json,
                used_pt = excluded.used_pt,
                snapshot_rowid = excluded.snapshot_rowid,
                raw_json = excluded.raw_json
            """,
            ("$uid", uid),
            ("$template", Int(commander, "template_id")),
            ("$level", Int(commander, "level")),
            ("$exp", Int(commander, "exp")),
            ("$locked", Int(commander, "is_locked") != 0 ? 1 : 0),
            ("$name", Text(commander, "name")),
            ("$abilities", Json(Items(commander, "ability").Select(item => Int32(item)).ToList())),
            ("$origin", Json(Items(commander, "ability_origin").Select(item => Int32(item)).ToList())),
            ("$skills", Json(Items(commander, "skill").Select(skill => new Dictionary<string, int>
            {
                ["id"] = Int(skill, "id"), ["exp"] = Int(skill, "exp")
            }).ToList())),
            ("$pt", Int(commander, "used_pt")),
            ("$rowid", rowId),
            ("$raw", Json(commander)));
    }

    // Responses to the cat's change requests (CommanderProxy and the commander commands in the client Lua).
    // SC 25009 only says the feeding succeeded: the client works the target's new exp out itself, so the eaten
    // cats are recorded in current_commander_feeds before they are removed and the snapshot replays the feeding.
    private StateApplyResult ApplyCommanderResponse(SqliteTransaction tx, DecodedPacketEnvelope packet, JsonElement json,
        JsonElement request, string area)
    {
        var uid = Text(request, "commanderid");
        switch (packet.PacketId)
        {
            case 25005:
                if (json.TryGetProperty("commander", out var commander)) UpsertCommander(tx, packet.DecodedRowId, commander);
                return StateApplyResult.Applied(area, "meowfficer hatched", packet.DecodedRowId);
            case 25007:
                ApplyFleetCommanderSlot(tx, Int(request, "groupid"), Int(request, "pos"), Text(request, "commanderid"));
                return StateApplyResult.Applied(area, "fleet meowfficer slot", packet.DecodedRowId);
            case 25009:
                var eaten = 0;
                var materialIds = Items(request, "materialid").Select(ValueText).Where(id => id.Length > 0).ToList();
                if (Int(json, "result") != 0) return StateApplyResult.Applied(area, "meowfficer feed refused", packet.DecodedRowId);
                RecordFeed(tx, packet.DecodedRowId, Text(request, "targetid"), materialIds);
                foreach (var material in materialIds)
                {
                    Execute(tx, "DELETE FROM current_commanders WHERE commander_uid = $uid", ("$uid", material));
                    Execute(tx, "DELETE FROM current_fleet_commanders WHERE CAST(commander_id AS TEXT) = $uid", ("$uid", material));
                    eaten++;
                }

                return StateApplyResult.Applied(area,
                    $"meowfficer fed ({eaten.ToString(CultureInfo.InvariantCulture)} eaten)", packet.DecodedRowId);
            case 25027:
                var housed = ApplyCommanderHome(tx, packet.DecodedRowId, json);
                return StateApplyResult.Applied(area, $"cat house levels {housed.ToString(CultureInfo.InvariantCulture)}",
                    packet.DecodedRowId);
            case 25013:
                LearnTalent(tx, uid, Int(request, "targetid"), Int(request, "replaceid"));
                return StateApplyResult.Applied(area, "meowfficer talent learned", packet.DecodedRowId);
            case 25015:
                Execute(tx,
                    "UPDATE current_commanders SET abilities_json = ability_origin_json, used_pt = 0 WHERE commander_uid = $uid",
                    ("$uid", uid));
                return StateApplyResult.Applied(area, "meowfficer talents reset", packet.DecodedRowId);
            case 25017:
                Execute(tx, "UPDATE current_commanders SET locked = $flag WHERE commander_uid = $uid",
                    ("$flag", Int(request, "flag") != 0 ? 1 : 0), ("$uid", uid));
                return StateApplyResult.Applied(area, "meowfficer lock", packet.DecodedRowId);
            case 25021:
                Execute(tx, "UPDATE current_commanders SET name = $name WHERE commander_uid = $uid",
                    ("$name", Text(request, "name")), ("$uid", uid));
                return StateApplyResult.Applied(area, "meowfficer renamed", packet.DecodedRowId);
            default:
                return StateApplyResult.Deferred(area, "response handler not implemented", packet.DecodedRowId);
        }
    }

    private void RecordFeed(SqliteTransaction tx, long rowId, string targetUid, IReadOnlyList<string> materialIds)
    {
        if (targetUid.Length == 0 || materialIds.Count == 0) return;
        var materials = new List<Dictionary<string, object?>>();
        foreach (var uid in materialIds)
        {
            using var command = connection.CreateCommand();
            command.Transaction = tx;
            command.CommandText = "SELECT template_id, level, exp, abilities_json FROM current_commanders WHERE commander_uid = $uid";
            command.Parameters.AddWithValue("$uid", uid);
            using var reader = command.ExecuteReader();
            if (!reader.Read()) continue;
            materials.Add(new Dictionary<string, object?>
            {
                ["template_id"] = reader.IsDBNull(0) ? 0 : reader.GetInt32(0),
                ["level"] = reader.IsDBNull(1) ? 0 : reader.GetInt32(1),
                ["exp"] = reader.IsDBNull(2) ? 0 : reader.GetInt32(2),
                ["abilities"] = reader.IsDBNull(3) ? "[]" : reader.GetString(3)
            });
        }

        Execute(tx,
            "INSERT INTO current_commander_events (kind, target_uid, exp, materials_json, updated_rowid) " +
            "VALUES ('feed', $target, 0, $materials, $rowid)",
            ("$target", targetUid), ("$materials", Json(materials)), ("$rowid", rowId));
    }

    /// <summary>
    /// SC 40004 commander_exp: {commander_id, exp} per cat of the sortie fleets, exp being the gain. Captures decoded
    /// before p40.CommanderExp was read keep each entry as raw protobuf (field 1 id, field 2 exp).
    /// </summary>
    private void RecordCommanderBattleExp(SqliteTransaction tx, long rowId, JsonElement json)
    {
        foreach (var entry in Items(json, "commander_exp"))
        {
            var (uid, exp) = Text(entry, "raw") is { Length: > 0 } raw
                ? RawCommanderExp(raw)
                : (Text(entry, "commander_id"), Int(entry, "exp"));
            if (uid.Length == 0 || uid == "0" || exp <= 0) continue;
            Execute(tx,
                "INSERT INTO current_commander_events (kind, target_uid, exp, materials_json, updated_rowid) " +
                "VALUES ('exp', $target, $exp, '[]', $rowid)",
                ("$target", uid), ("$exp", exp), ("$rowid", rowId));
        }
    }

    private static (string Uid, int Exp) RawCommanderExp(string hex)
    {
        byte[] bytes;
        try
        {
            bytes = Convert.FromHexString(hex);
        }
        catch (FormatException)
        {
            return ("", 0);
        }

        long id = 0, exp = 0;
        var index = 0;
        while (index < bytes.Length)
        {
            var tag = ReadVarint(bytes, ref index);
            if ((tag & 7) != 0) break;
            var value = ReadVarint(bytes, ref index);
            if (tag >> 3 == 1) id = value;
            else if (tag >> 3 == 2) exp = value;
        }

        return (id > 0 ? id.ToString(CultureInfo.InvariantCulture) : "", (int)Math.Min(exp, int.MaxValue));
    }

    private static long ReadVarint(byte[] bytes, ref int index)
    {
        long value = 0;
        for (var shift = 0; index < bytes.Length && shift < 64; shift += 7)
        {
            var current = bytes[index++];
            value |= (long)(current & 0x7F) << shift;
            if ((current & 0x80) == 0) break;
        }

        return value;
    }

    /// <summary>SC 25027, the cat house: each housed cat's true level and EXP, which settle its earlier events.</summary>
    private int ApplyCommanderHome(SqliteTransaction tx, long rowId, JsonElement json)
    {
        var count = 0;
        foreach (var slot in Items(json, "slots"))
        {
            var uid = Text(slot, "commander_id");
            var level = Int(slot, "commander_level");
            if (uid.Length == 0 || uid == "0" || level <= 0) continue;
            Execute(tx, "UPDATE current_commanders SET level = $level, exp = $exp, snapshot_rowid = $rowid WHERE commander_uid = $uid",
                ("$level", level), ("$exp", Int(slot, "commander_exp")), ("$rowid", rowId), ("$uid", uid));
            Execute(tx, "DELETE FROM current_commander_events WHERE target_uid = $uid", ("$uid", uid));
            count++;
        }

        return count;
    }

    // CommanderLearnTalentCommand: a talent of the same line (a lower or higher rank) leaves first, then the
    // replaced talent, then the new one is appended and one talent point is spent. In the KR data every talent's
    // group_id is its id divided by ten, so the line is read from the id.
    private void LearnTalent(SqliteTransaction tx, string uid, int target, int replace)
    {
        using var command = connection.CreateCommand();
        command.Transaction = tx;
        command.CommandText = "SELECT abilities_json FROM current_commanders WHERE commander_uid = $uid";
        command.Parameters.AddWithValue("$uid", uid);
        if (command.ExecuteScalar() is not string json || target <= 0) return;
        var talents = JsonSerializer.Deserialize<List<int>>(json) ?? [];
        talents.RemoveAll(talent => talent / 10 == target / 10 || (replace != 0 && talent == replace));
        talents.Add(target);
        Execute(tx,
            "UPDATE current_commanders SET abilities_json = $talents, used_pt = used_pt + 1 WHERE commander_uid = $uid",
            ("$talents", Json(talents)), ("$uid", uid));
    }

    // EquipCommanderToFleetCommand: only the named slot changes; commander 0 clears it.
    private void ApplyFleetCommanderSlot(SqliteTransaction tx, int fleetId, int pos, string commanderUid)
    {
        if (fleetId <= 0 || pos <= 0) return;
        Execute(tx, "DELETE FROM current_fleet_commanders WHERE fleet_id = $fleet AND pos = $pos",
            ("$fleet", fleetId), ("$pos", pos));
        if (commanderUid.Length == 0 || commanderUid == "0") return;
        Execute(tx,
            "INSERT INTO current_fleet_commanders (fleet_id, pos, commander_id, raw_json) VALUES ($fleet, $pos, $commander, $raw)",
            ("$fleet", fleetId), ("$pos", pos), ("$commander", long.Parse(commanderUid, CultureInfo.InvariantCulture)),
            ("$raw", Json(new Dictionary<string, object?> { ["pos"] = pos, ["id"] = long.Parse(commanderUid, CultureInfo.InvariantCulture) })));
    }

    private static int Int32(JsonElement value) =>
        value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number) ? number
        : int.TryParse(ValueText(value), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0;
}
