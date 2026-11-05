using System.Collections.ObjectModel;

namespace AzurLaneDashboard.Core.Models;

public sealed class DashboardSnapshot
{
    public System.Text.Json.JsonElement ShipLevelTable { get; init; }
    public AuctionDashboard Auction { get; init; } = new();
    public FleetPlanDashboard FleetPlan { get; init; } = new();
    public MonthlyShopDashboard MonthlyShop { get; init; } = new();
    public MeowfficerDashboard Meowfficers { get; init; } = MeowfficerDashboard.Empty;
    public ObservableCollection<DashboardKpiRow> Kpis { get; init; } = [];
    public ObservableCollection<ShipRow> Ships { get; init; } = [];
    public ObservableCollection<EquipmentRow> Equipment { get; init; } = [];
    public ObservableCollection<EquipmentRow> EquipmentRecommendations { get; init; } = [];
    public ObservableCollection<SpecialEquipmentRow> SpecialEquipment { get; init; } = [];
    public ObservableCollection<EquipmentSkinRow> EquipmentSkins { get; init; } = [];
    public ObservableCollection<LevelPriorityRow> LevelPriorities { get; init; } = [];
    public ObservableCollection<GearGapRow> GearGaps { get; init; } = [];
    public ObservableCollection<BattleRunRow> BattleRuns { get; init; } = [];
    public ObservableCollection<BattleShipRateRow> BattleShipRates { get; init; } = [];
    public ObservableCollection<FleetDamageRow> FleetDamageRows { get; init; } = [];
    public ObservableCollection<FleetCompositionGroupRow> FleetCompositionGroups { get; init; } = [];
    public ObservableCollection<SkillPriorityRow> SkillPriorities { get; init; } = [];
    public ObservableCollection<SystemStatusRow> SystemStatuses { get; init; } = [];
    public ObservableCollection<AcademyRoomRow> AcademyRooms { get; init; } = [];
    public ObservableCollection<ResearchQueueRow> ResearchQueue { get; init; } = [];
    public ObservableCollection<CommissionRow> Commissions { get; init; } = [];
    public ObservableCollection<BuildQueueRow> Builds { get; init; } = [];
    public ObservableCollection<ResourceRow> Resources { get; init; } = [];
    public ObservableCollection<CommunityGearShortageRow> CommunityGearShortages { get; init; } = [];
    public ObservableCollection<CleanupCandidateRow> CleanupCandidates { get; init; } = [];
    public ObservableCollection<PacketStatRow> PacketStats { get; init; } = [];
    public ObservableCollection<PacketStatRow> PacketInterpretationGaps { get; init; } = [];
    public ObservableCollection<DecodeFailureRow> DecodeFailures { get; init; } = [];
    public string DatabasePath { get; init; } = "";
    public string Status { get; init; } = "";
    public string SnapshotSource { get; init; } = "";
    public string SnapshotWarning { get; init; } = "";
    public string CurrentStateMode { get; init; } = "";
    public int CurrentStateLastDecodedRowId { get; init; }
    public string CurrentStateLastStatus { get; init; } = "";
    public string CurrentStateLastArea { get; init; } = "";
    public string CurrentStateLastDetail { get; init; } = "";
    public string CurrentStateLastError { get; init; } = "";
    public int PacketsSeen { get; init; }
    public int KnownPacketsSeen { get; init; }
    public int KnownPacketsDecoded { get; init; }
    public int PacketPayloadsSaved { get; init; }
    public int DecodeFailuresSaved { get; init; }
    public string LiveUpdatedAt { get; init; } = "";
    public string CaptureStatusLabel { get; init; } = "";
    public string CaptureStatusDetail { get; init; } = "";
    public bool IsCaptureRunning { get; init; }
    public string GeneratedAt { get; init; } = "";
    /// <summary>The newest captured battle result, null before any.</summary>
    public LatestBattleInfo? LatestBattle { get; init; }
    /// <summary>The chapter last entered in the game, 0 when none was captured.</summary>
    public int CurrentChapterId { get; init; }
    /// <summary>Oil since the last server sync, null before any sync was captured.</summary>
    public OilEstimate? Oil { get; init; }
    /// <summary>The courtyard's food, null before SC 19001 was captured.</summary>
    public DormFoodState? DormFood { get; init; }
    public int DockUsed { get; init; }
    public int DockMax { get; init; }
    public int DockExemptCount { get; init; }
    public int EquipmentBagCount { get; init; }
    public int EquipmentBagMax { get; init; }
    public int EquipmentEquippedCount { get; init; }
    public string BattleSummaryText { get; init; } = "";
    public string BattleSubText { get; init; } = "";
    public string FleetTechSummaryText { get; init; } = "";
    public string CommunitySummaryText { get; init; } = "";
}
