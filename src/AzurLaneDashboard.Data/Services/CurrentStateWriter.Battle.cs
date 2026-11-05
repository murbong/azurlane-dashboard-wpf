using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace AzurLaneDashboard.Data.Services;

public sealed partial class CurrentStateWriter
{
    private void ApplyBattleResult(SqliteTransaction tx, DecodedPacketEnvelope packet, JsonElement json,
        JsonElement request)
    {
        var stageId = Int(request, "data");
        var system = Int(request, "system");
        var mvp = Text(json, "mvp");
        var expRows = Array(json, "ship_exp_list").ToList();
        var total = expRows.Sum(item => Int(item, "exp"));
        var avg = expRows.Count == 0 ? 0 : total / expRows.Count;
        var top = string.Join(", ", expRows
            .Select(item => (Uid: Text(item, "ship_id"), Exp: Int(item, "exp")))
            .OrderByDescending(item => item.Exp)
            .Take(3)
            .Select(item => $"{item.Uid}:{item.Exp.ToString(CultureInfo.InvariantCulture)}"));
        var raw = new Dictionary<string, object?>
        {
            ["rowid"] = packet.DecodedRowId,
            ["stage_id"] = stageId,
            ["system"] = system,
            ["system_label"] = system == 1 ? "일반 해역" : system.ToString(CultureInfo.InvariantCulture),
            ["stage_label"] = stageId.ToString(CultureInfo.InvariantCulture),
            ["stage_detail"] = stageId.ToString(CultureInfo.InvariantCulture),
            ["total_time"] = Int(request, "total_time"),
            ["ship_exp_total"] = total,
            ["ship_exp_avg"] = avg,
            ["mvp_uid"] = mvp,
            ["mvp_name"] = mvp,
            ["top_exp_summary"] = top,
            ["ship_exp_list"] = expRows.Select(JsonDictionary).ToList()
        };
        Execute(
            tx,
            """
            INSERT INTO current_battle_runs (
                row_index, stage_id, stage_label, stage_detail, system_label, total_time,
                ship_exp_total, ship_exp_avg, mvp_uid, mvp_name, top_exp_summary, raw_json)
            VALUES ($row, $stage, $label, $detail, $systemLabel, $time, $total, $avg, $mvp, $mvpName, $top, $raw)
            ON CONFLICT(row_index) DO UPDATE SET raw_json = excluded.raw_json
            """,
            ("$row", packet.DecodedRowId),
            ("$stage", stageId),
            ("$label", stageId.ToString(CultureInfo.InvariantCulture)),
            ("$detail", stageId.ToString(CultureInfo.InvariantCulture)),
            ("$systemLabel", system == 1 ? "일반 해역" : system.ToString(CultureInfo.InvariantCulture)),
            ("$time", Int(request, "total_time")),
            ("$total", total),
            ("$avg", avg),
            ("$mvp", mvp),
            ("$mvpName", mvp),
            ("$top", top),
            ("$raw", Json(raw)));
        Execute(
            tx,
            """
            DELETE FROM current_battle_runs
            WHERE row_index NOT IN (
                SELECT row_index FROM current_battle_runs ORDER BY row_index DESC LIMIT 120
            )
            """);

        foreach (var item in expRows)
        {
            var uid = Text(item, "ship_id");
            if (string.IsNullOrWhiteSpace(uid))
            {
                continue;
            }

            ApplyBattleAffinityAndMorale(tx, item);
            var exp = Int(item, "exp");
            var rawRate = new Dictionary<string, object?>
            {
                ["ship_uid"] = uid,
                ["name"] = uid,
                ["sample_exp"] = exp,
                ["avg_exp"] = exp,
                ["last_exp"] = exp,
                ["mvp_count"] = uid == mvp ? 1 : 0,
                ["mood_value"] = Int(item, "energy")
            };
            Execute(
                tx,
                """
                INSERT INTO current_battle_ship_rates (
                    ship_uid, name, level, sample_exp, avg_exp, last_exp, mvp_count,
                    runs_to_120_avg, mood_value, raw_json)
                VALUES ($uid, $name, 0, $sample, $avg, $last, $mvp, 0, $mood, $raw)
                ON CONFLICT(ship_uid) DO UPDATE SET
                    sample_exp = excluded.sample_exp,
                    avg_exp = excluded.avg_exp,
                    last_exp = excluded.last_exp,
                    mvp_count = current_battle_ship_rates.mvp_count + excluded.mvp_count,
                    mood_value = CASE WHEN excluded.mood_value > 0 THEN excluded.mood_value ELSE current_battle_ship_rates.mood_value END,
                    raw_json = excluded.raw_json
                """,
                ("$uid", uid),
                ("$name", uid),
                ("$sample", exp),
                ("$avg", exp),
                ("$last", exp),
                ("$mvp", uid == mvp ? 1 : 0),
                ("$mood", Int(item, "energy")),
                ("$raw", Json(rawRate)));
        }
    }
}
