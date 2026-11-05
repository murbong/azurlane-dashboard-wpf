using System.Globalization;
using System.Text.Json;
using AzurLaneDashboard.Core.Models;

namespace AzurLaneDashboard.Data.Services;

public sealed partial class NativeSnapshotService
{
    private static BattleSnapshot BuildBattleTraining(
        List<PacketRecord> packets,
        List<ShipState> ships,
        JsonElement levelMap,
        JsonElement stageMap)
    {
        var shipsByUid = ships.ToDictionary(ship => ship.Uid, StringComparer.Ordinal);
        var requests = packets.Where(packet => packet.PacketId == 40003 && packet.Direction == "CS").ToList();
        var runs = new List<BattleRunInternal>();

        foreach (var packet in packets.Where(packet => packet.PacketId == 40004 && packet.Direction == "SC"))
        {
            var request = MatchingBattleRequest(packet, requests);
            var stageId = JsonInt(request, "data");
            var stageInfo = GetStageInfo(stageId, stageMap);
            var mvpUid = JsonString(packet.Json, "mvp");
            var expEntries = new List<(string Uid, string Name, int Exp, bool IsMvp)>();
            foreach (var exp in Array(packet.Json, "ship_exp_list"))
            {
                var uid = JsonString(exp, "ship_id");
                var gained = JsonInt(exp, "exp");
                var ship = shipsByUid.GetValueOrDefault(uid);
                expEntries.Add((uid, ship?.DisplayName ?? uid, gained, uid == mvpUid));
            }

            var total = expEntries.Sum(entry => entry.Exp);
            runs.Add(new BattleRunInternal
            {
                RowId = packet.RowId,
                Source = packet.Source,
                Index = packet.Index,
                StageId = stageId,
                StageLabel = stageInfo.Label,
                StageName = stageInfo.Name,
                StageDetail = stageInfo.Detail,
                StageType = stageInfo.Type,
                SystemLabel = BattleSystemLabel(JsonInt(request, "system")),
                TotalTime = JsonInt(request, "total_time"),
                MvpUid = mvpUid,
                MvpName = shipsByUid.GetValueOrDefault(mvpUid)?.DisplayName ?? mvpUid,
                ShipExpTotal = total,
                ShipExpAvg = expEntries.Count == 0 ? 0 : (int)Math.Round(total / (double)expEntries.Count),
                ShipExpList = expEntries
            });
        }

        var displayRuns = LatestStageSegmentRuns(runs).OrderByDescending(run => run.RowId).ToList();
        var sampleRuns = LatestCompletedStageRuns(runs).ToList();
        var sampleIsStage = sampleRuns.Any(run => run.IsBoss);
        var shipTotals = new Dictionary<string, BattleShipAccumulator>(StringComparer.Ordinal);
        foreach (var run in sampleRuns)
        {
            foreach (var entry in run.ShipExpList)
            {
                if (!shipTotals.TryGetValue(entry.Uid, out var total))
                {
                    var ship = shipsByUid.GetValueOrDefault(entry.Uid);
                    total = new BattleShipAccumulator
                    {
                        Uid = entry.Uid,
                        Name = ship?.DisplayName ?? entry.Name,
                        IconUrl = ship?.IconUrl ?? "",
                        Rarity = ship?.Rarity ?? "",
                        RarityClass = ship?.RarityClass ?? "",
                        IsProposed = ship?.IsProposed ?? false,
                        Level = ship?.Level ?? 0,
                        CurrentExp = ship?.Exp ?? 0,
                        Energy = ship?.Energy ?? 0
                    };
                    shipTotals[entry.Uid] = total;
                }

                total.Runs++;
                total.TotalExp += entry.Exp;
                total.LastExp = entry.Exp;
                total.MaxExp = Math.Max(total.MaxExp, entry.Exp);
                if (entry.IsMvp)
                {
                    total.MvpCount++;
                }
            }
        }

        var rateRows = new List<BattleShipRateRow>();
        foreach (var total in shipTotals.Values)
        {
            var avg = total.Runs == 0 ? 0 : total.TotalExp / total.Runs;
            var sampleExp = sampleIsStage ? total.TotalExp : total.LastExp;
            var remaining120 = ExpRemainingToLevel(total.Level, total.CurrentExp, 120, total.Rarity, levelMap);
            var remaining125 = ExpRemainingToLevel(total.Level, total.CurrentExp, 125, total.Rarity, levelMap);
            var runsTo120 = remaining120 is null || sampleExp <= 0
                ? null
                : (int?)CeilingDiv(remaining120.Value, sampleExp);
            var runsTo125 = remaining125 is null || sampleExp <= 0
                ? null
                : (int?)CeilingDiv(remaining125.Value, sampleExp);
            var progress = LevelProgress(total.Level, total.CurrentExp, total.Rarity, levelMap);
            if (shipsByUid.TryGetValue(total.Uid, out var ship))
            {
                ship.RecentBattleExpAvg = sampleExp;
                ship.RunsTo120Avg = runsTo120;
                ship.RunsTo125Avg = runsTo125;
            }

            rateRows.Add(new BattleShipRateRow
            {
                ShipUid = total.Uid,
                Name = total.Name,
                IconUrl = total.IconUrl,
                Rarity = total.Rarity,
                RarityTier = total.Rarity,
                RarityClass = total.RarityClass,
                IsProposed = total.IsProposed,
                Level = total.Level,
                SampleExp = sampleExp,
                AvgExp = avg,
                LastExp = total.LastExp,
                MvpCount = total.MvpCount,
                RunsTo120Avg = runsTo120,
                RunsTo125Avg = runsTo125,
                SampleKind = sampleIsStage ? "stage_clear" : "battle",
                ExpCurrent = progress.Current,
                ExpRequired = progress.Required,
                ExpPercent = progress.Percent,
                MoodValue = total.Energy,
                MoodLabel = MoodLabel(total.Energy)
            });
        }

        rateRows = rateRows
            .OrderBy(row => row.RunsTo125Avg ?? row.RunsTo120Avg ?? int.MaxValue)
            .ThenBy(row => row.Name, StringComparer.CurrentCulture)
            .ToList();

        var latest = displayRuns.FirstOrDefault() ?? runs.OrderByDescending(run => run.RowId).FirstOrDefault();
        var boss = sampleRuns.LastOrDefault(run => run.IsBoss) ?? sampleRuns.LastOrDefault();
        return new BattleSnapshot
        {
            Runs = displayRuns.Select(run => new BattleRunRow
            {
                StageId = run.StageId.ToString(CultureInfo.InvariantCulture),
                StageLabel = run.StageLabel,
                StageDetail = run.StageDetail,
                SystemLabel = run.SystemLabel,
                TotalTime = run.TotalTime,
                ShipExpAvg = run.ShipExpAvg,
                ShipExpTotal = run.ShipExpTotal,
                MvpName = run.MvpName,
                TopExpSummary = string.Join(", ",
                    run.ShipExpList.OrderByDescending(entry => entry.Exp).Take(4).Select(entry =>
                        $"{entry.Name} +{entry.Exp:N0}{(entry.IsMvp ? " MVP" : "")}")),
                IsBoss = run.IsBoss
            }).ToList(),
            ShipRates = rateRows,
            SampleRunCount = sampleRuns.Count,
            DisplayRunCount = displayRuns.Count,
            CapturedRunCount = runs.Count,
            SampleLabel = sampleIsStage ? "최근 완료 스테이지" : "최근 전투",
            SummaryText = latest is null
                ? "전투 결과 패킷 대기"
                : $"{(boss?.StageLabel ?? latest.StageLabel)} · {(sampleIsStage ? "최근 완료 스테이지" : "최근 전투")} · 평균 {(rateRows.Count == 0 ? 0 : (int)Math.Round(rateRows.Average(row => row.SampleExp))):N0} EXP"
        };
    }
}
