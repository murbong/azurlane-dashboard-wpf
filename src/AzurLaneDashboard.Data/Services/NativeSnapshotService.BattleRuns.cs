using System.Globalization;
using System.Text.Json;

namespace AzurLaneDashboard.Data.Services;

public sealed partial class NativeSnapshotService
{
    private static JsonElement MatchingBattleRequest(PacketRecord resultPacket, List<PacketRecord> requests)
    {
        return requests
            .Where(packet => packet.Source == resultPacket.Source && packet.RowId <= resultPacket.RowId)
            .OrderByDescending(packet => packet.RowId)
            .Select(packet => packet.Json)
            .FirstOrDefault();
    }

    private static List<BattleRunInternal> LatestCompletedStageRuns(List<BattleRunInternal> runs)
    {
        var bestSegment = new List<BattleRunInternal>();
        BattleRunInternal? bestBoss = null;
        foreach (var group in runs.GroupBy(run => run.Source))
        {
            var sourceRuns = group.OrderBy(run => run.RowId).ToList();
            var previousBossIndex = -1;
            for (var i = 0; i < sourceRuns.Count; i++)
            {
                if (!sourceRuns[i].IsBoss)
                {
                    continue;
                }

                if (bestBoss is null || sourceRuns[i].RowId > bestBoss.RowId)
                {
                    var segment = sourceRuns.Skip(previousBossIndex + 1).Take(i - previousBossIndex).ToList();
                    if (segment.Sum(run => run.ShipExpTotal) > 0)
                    {
                        bestBoss = sourceRuns[i];
                        bestSegment = segment;
                    }
                }

                previousBossIndex = i;
            }
        }

        if (bestSegment.Count > 0)
        {
            return bestSegment;
        }

        var expRun = runs.Where(run => run.ShipExpTotal > 0).OrderByDescending(run => run.RowId).FirstOrDefault();
        return expRun is null
            ? runs.OrderByDescending(run => run.RowId).Take(1).ToList()
            : new List<BattleRunInternal> { expRun };
    }

    private static List<BattleRunInternal> LatestStageSegmentRuns(List<BattleRunInternal> runs)
    {
        var latest = runs.OrderByDescending(run => run.RowId).FirstOrDefault();
        if (latest is null)
        {
            return new List<BattleRunInternal>();
        }

        var sourceRuns = runs.Where(run => run.Source == latest.Source).OrderBy(run => run.RowId).ToList();
        var latestIndex = sourceRuns.FindIndex(run => ReferenceEquals(run, latest));
        if (latestIndex < 0)
        {
            latestIndex = sourceRuns.Count - 1;
        }

        var previousBossIndex = -1;
        for (var i = 0; i < latestIndex; i++)
        {
            if (sourceRuns[i].IsBoss)
            {
                previousBossIndex = i;
            }
        }

        return sourceRuns.Skip(previousBossIndex + 1).Take(latestIndex - previousBossIndex).ToList();
    }

    private static StageInfo GetStageInfo(int stageId, JsonElement stageMap)
    {
        var info = MapObject(stageMap, stageId);
        var name = JsonString(info, "name");
        var chapter = JsonString(info, "chapter_label");
        var level = JsonInt(info, "level");
        // "D3 껍질을 깨부술 때 · 보스 함대": the chapter says where, the stage name tells boss from elite fleets.
        var label = !string.IsNullOrWhiteSpace(chapter) && !string.IsNullOrWhiteSpace(name) && name != chapter
            ? $"{chapter} · {name}"
            : FirstNonEmpty(chapter, name, stageId > 0 ? $"미확인 해역 {stageId}" : "-");
        return new StageInfo(
            label,
            name,
            level > 0 ? $"Lv.{level}" : "",
            JsonInt(info, "type"));
    }
}
