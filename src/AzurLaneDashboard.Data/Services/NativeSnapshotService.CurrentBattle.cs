using System.Text.Json;
using AzurLaneDashboard.Core.Models;
using Microsoft.Data.Sqlite;

namespace AzurLaneDashboard.Data.Services;

public sealed partial class NativeSnapshotService
{
    private static BattleSnapshot BuildCurrentBattleSnapshot(SqliteConnection connection, List<ShipState> ships,
        JsonElement levelMap, JsonElement stageMap)
    {
        var byUid = ships.ToDictionary(ship => ship.Uid, StringComparer.Ordinal);
        var runs = new List<BattleRunRow>();
        var rates = new List<BattleShipRateRow>();

        if (TableExists(connection, "current_battle_runs"))
        {
            using var command = connection.CreateCommand();
            // Newest first: the home card shows the top rows and the summary reads the first one as the latest battle.
            command.CommandText = "SELECT * FROM current_battle_runs ORDER BY row_index DESC";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                // The writer stores raw IDs (stage_label = stage ID, mvp_name = ship UID); names are resolved here.
                var stageInfo = GetStageInfo(Int(reader, "stage_id"), stageMap);
                runs.Add(new BattleRunRow
                {
                    StageId = Text(reader, "stage_id"),
                    StageLabel = stageInfo.Label,
                    StageDetail = stageInfo.Detail,
                    SystemLabel = Text(reader, "system_label"),
                    TotalTime = Int(reader, "total_time"),
                    ShipExpTotal = Int(reader, "ship_exp_total"),
                    ShipExpAvg = Int(reader, "ship_exp_avg"),
                    MvpName = BattleShipName(byUid, Text(reader, "mvp_uid")),
                    TopExpSummary = NamedExpSummary(Text(reader, "top_exp_summary"), byUid)
                });
            }
        }

        if (TableExists(connection, "current_battle_ship_rates"))
        {
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT * FROM current_battle_ship_rates";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var uid = Text(reader, "ship_uid");
                byUid.TryGetValue(uid, out var ship);
                var level = FirstPositive(Int(reader, "level"), ship?.Level ?? 0);
                var exp = ship?.Exp ?? 0;
                var rarity = ship?.Rarity ?? "";
                var progress = LevelProgress(level, exp, rarity, levelMap);
                var sampleExp = Int(reader, "sample_exp");
                var remaining120 = ExpRemainingToLevel(level, exp, 120, rarity, levelMap);
                var remaining125 = ExpRemainingToLevel(level, exp, 125, rarity, levelMap);
                var runsTo120 = remaining120 is null || sampleExp <= 0
                    ? null
                    : (int?)CeilingDiv(remaining120.Value, sampleExp);
                var runsTo125 = remaining125 is null || sampleExp <= 0
                    ? null
                    : (int?)CeilingDiv(remaining125.Value, sampleExp);
                if (ship is not null)
                {
                    ship.RecentBattleExpAvg = sampleExp;
                    ship.RunsTo120Avg = runsTo120;
                    ship.RunsTo125Avg = runsTo125;
                }

                rates.Add(new BattleShipRateRow
                {
                    ShipUid = uid,
                    Name = FirstNonEmpty(ship?.DisplayName ?? "", Text(reader, "name"), uid),
                    IconUrl = ship?.IconUrl ?? "",
                    Rarity = rarity,
                    RarityTier = RarityFilterTier(rarity),
                    RarityClass = ship?.RarityClass ?? "",
                    IsProposed = ship?.IsProposed ?? false,
                    Level = level,
                    SampleExp = sampleExp,
                    AvgExp = Int(reader, "avg_exp"),
                    LastExp = Int(reader, "last_exp"),
                    MvpCount = Int(reader, "mvp_count"),
                    RunsTo120Avg = runsTo120,
                    RunsTo125Avg = runsTo125,
                    SampleKind = "current",
                    ExpCurrent = progress.Current,
                    ExpRequired = progress.Required,
                    ExpPercent = progress.Percent,
                    // mood_value holds the morale the last battle spent, not the ship's morale.
                    MoodValue = ship?.Energy ?? 0,
                    MoodLabel = MoodLabel(ship?.Energy ?? 0)
                });
            }
        }

        rates = rates.OrderBy(row => row.RunsTo125Avg ?? row.RunsTo120Avg ?? int.MaxValue)
            .ThenBy(row => row.Name, StringComparer.CurrentCulture)
            .ToList();
        var latest = runs.FirstOrDefault();
        return new BattleSnapshot
        {
            Runs = runs,
            ShipRates = rates,
            SampleRunCount = runs.Count,
            DisplayRunCount = runs.Count,
            CapturedRunCount = runs.Count,
            SampleLabel = "현재 상태",
            SummaryText = latest is null
                ? "전투 결과 패킷 대기"
                : $"{latest.StageLabel} · 현재 상태 · 평균 {(rates.Count == 0 ? 0 : (int)Math.Round(rates.Average(row => row.SampleExp))):N0} EXP"
        };
    }

    private static string BattleShipName(IReadOnlyDictionary<string, ShipState> ships, string uid) =>
        ships.TryGetValue(uid, out var ship) ? ship.DisplayName
        : string.IsNullOrWhiteSpace(uid) ? "-"
        : "미확인 함선";

    // "53259051:1826, 53325959:1369" -> "타이거 1,826 · 라파엘로 1,369"
    private static string NamedExpSummary(string summary, IReadOnlyDictionary<string, ShipState> ships) =>
        string.Join(" · ", summary
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(part => part.Split(':', 2))
            .Select(pair => pair.Length == 2 && int.TryParse(pair[1], out var exp)
                ? $"{BattleShipName(ships, pair[0])} {exp:N0}"
                : string.Join(":", pair)));
}
