using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace AzurLaneDashboard.Data.Services;

public sealed partial class CurrentStateWriter
{
    private StateApplyResult ApplyFleetSnapshot(SqliteTransaction tx, DecodedPacketEnvelope packet, JsonElement json)
    {
        Execute(tx, "DELETE FROM current_fleet_commanders");
        Execute(tx, "DELETE FROM current_fleet_ships");
        Execute(tx, "DELETE FROM current_fleets");

        var count = 0;
        foreach (var group in Array(json, "group_list"))
        {
            if (UpsertFleetGroup(tx, group, packet.DecodedRowId, replaceShips: true, replaceCommanders: true))
            {
                count++;
            }
        }

        return StateApplyResult.Applied("fleets", $"{count.ToString(CultureInfo.InvariantCulture)} fleets", packet.DecodedRowId);
    }

    private StateApplyResult ApplyFleetUpdate(SqliteTransaction tx, DecodedPacketEnvelope packet, JsonElement json)
    {
        if (!json.TryGetProperty("group", out var group) || group.ValueKind != JsonValueKind.Object)
        {
            return StateApplyResult.Deferred("fleets", "group missing", packet.DecodedRowId);
        }

        return UpsertFleetGroup(tx, group, packet.DecodedRowId, replaceShips: true, replaceCommanders: true)
            ? StateApplyResult.Applied("fleets", $"fleet {Int(group, "id").ToString(CultureInfo.InvariantCulture)} updated", packet.DecodedRowId)
            : StateApplyResult.Deferred("fleets", "fleet id missing", packet.DecodedRowId);
    }

    private void ApplyFleetCommit(SqliteTransaction tx, DecodedPacketEnvelope packet, JsonElement request)
    {
        var fleetId = Int(request, "id");
        if (fleetId <= 0)
        {
            return;
        }

        UpsertFleetHeader(tx, fleetId, "", "", Json(request), packet.DecodedRowId);
        ReplaceFleetShips(tx, fleetId, Array(request, "ship_list"));
    }

    private void ApplyFleetRename(SqliteTransaction tx, DecodedPacketEnvelope packet, JsonElement request)
    {
        var fleetId = Int(request, "id");
        if (fleetId <= 0)
        {
            return;
        }

        UpsertFleetHeader(tx, fleetId, Text(request, "name"), "", Json(request), packet.DecodedRowId);
    }

    private bool UpsertFleetGroup(
        SqliteTransaction tx,
        JsonElement group,
        long rowId,
        bool replaceShips,
        bool replaceCommanders)
    {
        var fleetId = Int(group, "id");
        if (fleetId <= 0)
        {
            return false;
        }

        var commanders = Array(group, "commanders").ToArray();
        UpsertFleetHeader(tx, fleetId, Text(group, "name"), FleetCommanderSummary(commanders), Json(group), rowId);

        if (replaceShips)
        {
            ReplaceFleetShips(tx, fleetId, Array(group, "ship_list"));
        }

        if (replaceCommanders)
        {
            ReplaceFleetCommanders(tx, fleetId, commanders);
        }

        return true;
    }

    private void UpsertFleetHeader(
        SqliteTransaction tx,
        int fleetId,
        string name,
        string commanderSummary,
        string rawJson,
        long rowId)
    {
        Execute(
            tx,
            """
            INSERT INTO current_fleets (fleet_id, name, commander_summary, raw_json, updated_rowid, updated_at)
            VALUES ($fleet, $name, $commanders, $raw, $rowid, $now)
            ON CONFLICT(fleet_id) DO UPDATE SET
                name = CASE WHEN excluded.name <> '' THEN excluded.name ELSE current_fleets.name END,
                commander_summary = CASE
                    WHEN excluded.commander_summary <> '' THEN excluded.commander_summary
                    ELSE current_fleets.commander_summary
                END,
                raw_json = excluded.raw_json,
                updated_rowid = excluded.updated_rowid,
                updated_at = excluded.updated_at
            """,
            ("$fleet", fleetId),
            ("$name", name),
            ("$commanders", commanderSummary),
            ("$raw", rawJson),
            ("$rowid", rowId),
            ("$now", NowText()));
    }

    private void ReplaceFleetShips(SqliteTransaction tx, int fleetId, IEnumerable<JsonElement> shipIds)
    {
        Execute(tx, "DELETE FROM current_fleet_ships WHERE fleet_id = $fleet", ("$fleet", fleetId));

        var slot = 1;
        foreach (var shipId in shipIds)
        {
            var uid = ValueText(shipId);
            if (string.IsNullOrWhiteSpace(uid) || uid == "0")
            {
                continue;
            }

            Execute(
                tx,
                """
                INSERT INTO current_fleet_ships (fleet_id, ship_uid, slot_index, raw_json)
                VALUES ($fleet, $ship, $slot, $raw)
                """,
                ("$fleet", fleetId),
                ("$ship", uid),
                ("$slot", slot),
                ("$raw", Json(new Dictionary<string, object?>
                {
                    ["fleet_id"] = fleetId,
                    ["ship_uid"] = uid,
                    ["slot_index"] = slot
                })));
            slot++;
        }
    }

    private void ReplaceFleetCommanders(SqliteTransaction tx, int fleetId, IEnumerable<JsonElement> commanders)
    {
        Execute(tx, "DELETE FROM current_fleet_commanders WHERE fleet_id = $fleet", ("$fleet", fleetId));

        foreach (var commander in commanders)
        {
            var pos = Int(commander, "pos");
            var commanderId = Int(commander, "id");
            if (pos <= 0 || commanderId <= 0)
            {
                continue;
            }

            Execute(
                tx,
                """
                INSERT INTO current_fleet_commanders (fleet_id, pos, commander_id, raw_json)
                VALUES ($fleet, $pos, $commander, $raw)
                ON CONFLICT(fleet_id, pos) DO UPDATE SET
                    commander_id = excluded.commander_id,
                    raw_json = excluded.raw_json
                """,
                ("$fleet", fleetId),
                ("$pos", pos),
                ("$commander", commanderId),
                ("$raw", Json(commander)));
        }
    }

    private static string FleetCommanderSummary(IEnumerable<JsonElement> commanders)
    {
        return string.Join(", ", commanders
            .Select(commander =>
            {
                var pos = Int(commander, "pos");
                var id = Int(commander, "id");
                return pos > 0 && id > 0
                    ? $"{pos.ToString(CultureInfo.InvariantCulture)}:{id.ToString(CultureInfo.InvariantCulture)}"
                    : "";
            })
            .Where(value => !string.IsNullOrWhiteSpace(value)));
    }
}
