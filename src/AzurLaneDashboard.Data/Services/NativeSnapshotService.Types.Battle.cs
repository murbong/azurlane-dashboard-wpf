using System.Text.Json;
using AzurLaneDashboard.Core.Models;

namespace AzurLaneDashboard.Data.Services;

public sealed partial class NativeSnapshotService
{
    private sealed class BattleRunInternal
    {
        public int RowId { get; init; }
        public string Source { get; init; } = "";
        public int Index { get; init; }
        public int StageId { get; init; }
        public string StageLabel { get; init; } = "";
        public string StageName { get; init; } = "";
        public string StageDetail { get; init; } = "";
        public int StageType { get; init; }
        public bool IsBoss => StageType == 99 || StageName.Contains("보스", StringComparison.OrdinalIgnoreCase);
        public string SystemLabel { get; init; } = "";
        public int TotalTime { get; init; }
        public string MvpUid { get; init; } = "";
        public string MvpName { get; init; } = "";
        public int ShipExpTotal { get; init; }
        public int ShipExpAvg { get; init; }
        public List<(string Uid, string Name, int Exp, bool IsMvp)> ShipExpList { get; init; } = new();
    }

    private sealed class BattleShipAccumulator
    {
        public string Uid { get; init; } = "";
        public string Name { get; init; } = "";
        public string IconUrl { get; init; } = "";
        public string Rarity { get; init; } = "";
        public string RarityClass { get; init; } = "";
        public bool IsProposed { get; init; }
        public int Level { get; init; }
        public int CurrentExp { get; init; }
        public int Energy { get; init; }
        public int Runs { get; set; }
        public int TotalExp { get; set; }
        public int LastExp { get; set; }
        public int MaxExp { get; set; }
        public int MvpCount { get; set; }
    }

    private sealed class BattleSnapshot
    {
        public List<BattleRunRow> Runs { get; init; } = new();
        public List<BattleShipRateRow> ShipRates { get; init; } = new();
        public int SampleRunCount { get; init; }
        public int DisplayRunCount { get; init; }
        public int CapturedRunCount { get; init; }
        public string SampleLabel { get; init; } = "";
        public string SummaryText { get; init; } = "";
    }

}
