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
    // SC 25009 only says the feeding succeeded: the client works the target's new exp out itself, so the target
    // keeps its old level here until the next login list; only the eaten cats are removed.
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
                foreach (var material in Items(request, "materialid").Select(ValueText).Where(id => id.Length > 0))
                {
                    Execute(tx, "DELETE FROM current_commanders WHERE commander_uid = $uid", ("$uid", material));
                    Execute(tx, "DELETE FROM current_fleet_commanders WHERE CAST(commander_id AS TEXT) = $uid", ("$uid", material));
                    eaten++;
                }

                return StateApplyResult.Applied(area,
                    $"meowfficer fed ({eaten.ToString(CultureInfo.InvariantCulture)} eaten)", packet.DecodedRowId);
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
