using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace AzurLaneDashboard.Data.Services;

public sealed partial class CurrentStateWriter
{
    private StateApplyResult ApplyFleetTechSnapshot(SqliteTransaction tx, DecodedPacketEnvelope packet, JsonElement json)
    {
        Execute(tx, "DELETE FROM current_fleet_tech_attr_settings");
        Execute(tx, "DELETE FROM current_fleet_tech_groups");

        var groups = 0;
        foreach (var item in Array(json, "tech_list"))
        {
            if (UpsertFleetTechGroup(tx, item, packet.DecodedRowId))
            {
                groups++;
            }
        }

        var settings = ReplaceFleetTechAttrSettings(tx, Array(json, "techset_list"), packet.DecodedRowId);
        UpsertFleetTechStatus(tx);
        return StateApplyResult.Applied("fleet_tech",
            $"groups={groups.ToString(CultureInfo.InvariantCulture)}, settings={settings.ToString(CultureInfo.InvariantCulture)}",
            packet.DecodedRowId);
    }

    private void ApplyFleetTechStart(SqliteTransaction tx, DecodedPacketEnvelope packet, JsonElement request)
    {
        var groupId = Int(request, "tech_group_id");
        var techId = Int(request, "tech_id");
        if (groupId <= 0 || techId <= 0)
        {
            return;
        }

        Execute(
            tx,
            """
            INSERT INTO current_fleet_tech_groups (
                group_id, effect_tech_id, study_tech_id, study_finish_time,
                rewarded_tech, raw_json, updated_rowid, updated_at)
            VALUES ($group, 0, $study, 0, 0, $raw, $rowid, $now)
            ON CONFLICT(group_id) DO UPDATE SET
                study_tech_id = excluded.study_tech_id,
                study_finish_time = CASE
                    WHEN excluded.study_finish_time > 0 THEN excluded.study_finish_time
                    ELSE current_fleet_tech_groups.study_finish_time
                END,
                raw_json = json_patch(COALESCE(current_fleet_tech_groups.raw_json, '{}'), excluded.raw_json),
                updated_rowid = excluded.updated_rowid,
                updated_at = excluded.updated_at
            """,
            ("$group", groupId),
            ("$study", techId),
            ("$raw", Json(new Dictionary<string, object?>
            {
                ["group_id"] = groupId,
                ["study_tech_id"] = techId
            })),
            ("$rowid", packet.DecodedRowId),
            ("$now", NowText()));

        UpsertFleetTechStatus(tx);
    }

    private void ApplyFleetTechFinish(SqliteTransaction tx, DecodedPacketEnvelope packet, JsonElement request)
    {
        var groupId = Int(request, "tech_group_id");
        if (groupId <= 0)
        {
            return;
        }

        Execute(
            tx,
            """
            UPDATE current_fleet_tech_groups
            SET effect_tech_id = CASE
                    WHEN study_tech_id > 0 THEN study_tech_id
                    ELSE effect_tech_id
                END,
                study_tech_id = 0,
                study_finish_time = 0,
                updated_rowid = $rowid,
                updated_at = $now
            WHERE group_id = $group
            """,
            ("$group", groupId),
            ("$rowid", packet.DecodedRowId),
            ("$now", NowText()));

        UpsertFleetTechStatus(tx);
    }

    private void ApplyFleetTechAward(SqliteTransaction tx, DecodedPacketEnvelope packet, JsonElement request)
    {
        var groupId = Int(request, "group_id");
        var techId = Int(request, "tech_id");
        if (groupId <= 0 || techId <= 0)
        {
            return;
        }

        Execute(
            tx,
            """
            INSERT INTO current_fleet_tech_groups (
                group_id, effect_tech_id, study_tech_id, study_finish_time,
                rewarded_tech, raw_json, updated_rowid, updated_at)
            VALUES ($group, 0, 0, 0, $rewarded, $raw, $rowid, $now)
            ON CONFLICT(group_id) DO UPDATE SET
                rewarded_tech = excluded.rewarded_tech,
                raw_json = json_patch(COALESCE(current_fleet_tech_groups.raw_json, '{}'), excluded.raw_json),
                updated_rowid = excluded.updated_rowid,
                updated_at = excluded.updated_at
            """,
            ("$group", groupId),
            ("$rewarded", techId),
            ("$raw", Json(new Dictionary<string, object?>
            {
                ["group_id"] = groupId,
                ["rewarded_tech"] = techId
            })),
            ("$rowid", packet.DecodedRowId),
            ("$now", NowText()));

        UpsertFleetTechStatus(tx);
    }

    private void ApplyFleetTechOneStepAward(SqliteTransaction tx, DecodedPacketEnvelope packet)
    {
        Execute(
            tx,
            """
            UPDATE current_fleet_tech_groups
            SET rewarded_tech = effect_tech_id,
                updated_rowid = $rowid,
                updated_at = $now
            WHERE effect_tech_id > 0
            """,
            ("$rowid", packet.DecodedRowId),
            ("$now", NowText()));

        UpsertFleetTechStatus(tx);
    }

    private bool UpsertFleetTechGroup(SqliteTransaction tx, JsonElement group, long rowId)
    {
        var groupId = Int(group, "group_id");
        if (groupId <= 0)
        {
            return false;
        }

        Execute(
            tx,
            """
            INSERT INTO current_fleet_tech_groups (
                group_id, effect_tech_id, study_tech_id, study_finish_time,
                rewarded_tech, raw_json, updated_rowid, updated_at)
            VALUES ($group, $effect, $study, $finish, $rewarded, $raw, $rowid, $now)
            ON CONFLICT(group_id) DO UPDATE SET
                effect_tech_id = excluded.effect_tech_id,
                study_tech_id = excluded.study_tech_id,
                study_finish_time = excluded.study_finish_time,
                rewarded_tech = excluded.rewarded_tech,
                raw_json = excluded.raw_json,
                updated_rowid = excluded.updated_rowid,
                updated_at = excluded.updated_at
            """,
            ("$group", groupId),
            ("$effect", Int(group, "effect_tech_id")),
            ("$study", Int(group, "study_tech_id")),
            ("$finish", Int(group, "study_finish_time")),
            ("$rewarded", Int(group, "rewarded_tech")),
            ("$raw", Json(group)),
            ("$rowid", rowId),
            ("$now", NowText()));

        return true;
    }

    private int ReplaceFleetTechAttrSettings(SqliteTransaction tx, IEnumerable<JsonElement> settings, long rowId)
    {
        Execute(tx, "DELETE FROM current_fleet_tech_attr_settings");

        var count = 0;
        foreach (var setting in settings)
        {
            var shipType = Int(setting, "ship_type");
            var attrType = Int(setting, "attr_type");
            if (shipType <= 0 || attrType <= 0)
            {
                continue;
            }

            Execute(
                tx,
                """
                INSERT INTO current_fleet_tech_attr_settings (
                    ship_type, attr_type, set_value, raw_json, updated_rowid, updated_at)
                VALUES ($shipType, $attrType, $value, $raw, $rowid, $now)
                ON CONFLICT(ship_type, attr_type) DO UPDATE SET
                    set_value = excluded.set_value,
                    raw_json = excluded.raw_json,
                    updated_rowid = excluded.updated_rowid,
                    updated_at = excluded.updated_at
                """,
                ("$shipType", shipType),
                ("$attrType", attrType),
                ("$value", Int(setting, "set_value")),
                ("$raw", Json(setting)),
                ("$rowid", rowId),
                ("$now", NowText()));
            count++;
        }

        return count;
    }

    private void UpsertFleetTechStatus(SqliteTransaction tx)
    {
        using var command = connection.CreateCommand();
        command.Transaction = tx;
        command.CommandText = """
                              SELECT
                                  COUNT(*) AS group_count,
                                  COALESCE(SUM(CASE WHEN effect_tech_id > 0 THEN 1 ELSE 0 END), 0) AS completed_count,
                                  COALESCE(SUM(CASE WHEN study_tech_id > 0 THEN 1 ELSE 0 END), 0) AS studying_count,
                                  COALESCE(SUM(CASE
                                      WHEN effect_tech_id > 0 AND rewarded_tech <> effect_tech_id THEN 1
                                      ELSE 0
                                  END), 0) AS unclaimed_count,
                                  (SELECT COUNT(*) FROM current_fleet_tech_attr_settings) AS setting_count
                              FROM current_fleet_tech_groups
                              """;
        using var reader = command.ExecuteReader();
        var groupCount = 0;
        var completedCount = 0;
        var studyingCount = 0;
        var unclaimedCount = 0;
        var settingCount = 0;
        if (reader.Read())
        {
            groupCount = Convert.ToInt32((object?)reader.GetValue(0), CultureInfo.InvariantCulture);
            completedCount = Convert.ToInt32((object?)reader.GetValue(1), CultureInfo.InvariantCulture);
            studyingCount = Convert.ToInt32((object?)reader.GetValue(2), CultureInfo.InvariantCulture);
            unclaimedCount = Convert.ToInt32((object?)reader.GetValue(3), CultureInfo.InvariantCulture);
            settingCount = Convert.ToInt32((object?)reader.GetValue(4), CultureInfo.InvariantCulture);
        }

        reader.Close();
        UpsertSystemStatus(tx, "fleet_tech",
            $"groups={groupCount.ToString(CultureInfo.InvariantCulture)}, completed={completedCount.ToString(CultureInfo.InvariantCulture)}, studying={studyingCount.ToString(CultureInfo.InvariantCulture)}, unclaimed={unclaimedCount.ToString(CultureInfo.InvariantCulture)}, settings={settingCount.ToString(CultureInfo.InvariantCulture)}",
            new Dictionary<string, object?>
            {
                ["group_count"] = groupCount,
                ["completed_count"] = completedCount,
                ["studying_count"] = studyingCount,
                ["unclaimed_count"] = unclaimedCount,
                ["setting_count"] = settingCount
            });
    }
}
