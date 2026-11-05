using AzurLaneDashboard.Core.Services;

namespace AzurLaneDashboard.Core.Models;

/// <summary>
/// What the 편성·육성 tabs compute from interactively: the snapshot carries the data, and the view model
/// rebuilds the leveling, META boss and gear plans when the player changes a stage, boss or option.
/// </summary>
public sealed class FleetPlannerInputs
{
    public IReadOnlyList<FleetPlannerShip> Ships { get; init; } = [];
    public IReadOnlyList<LevelingShip> LevelingShips { get; init; } = [];
    /// <summary>The recent farming chapter first (when there is one), then the standard chapters.</summary>
    public IReadOnlyList<LevelingStageOption> Stages { get; init; } = [];
    public int FarmFleetId { get; init; }
    public IReadOnlyList<string> FarmFleetUids { get; init; } = [];

    public IReadOnlyList<MetaBoss> MetaBosses { get; init; } = [];
    public IReadOnlyDictionary<int, MetaBossGuide> MetaBossGuides { get; init; } = new Dictionary<int, MetaBossGuide>();
    public IReadOnlyList<RegalStarRule> StarRules { get; init; } = [];
    /// <summary>The archive boss last chosen in the game (SC 34502 default_boss_id), 0 when unknown.</summary>
    public int GameArchiveBossId { get; init; }
    public IReadOnlyList<IReadOnlySet<string>> GameFleets { get; init; } = [];
    /// <summary>
    /// Recorded META fights, oldest first, against every boss. The game keeps the META fleet on the device only
    /// (one slot for the rotation boss, one shared by the archive), and players rebuild it per boss, so the
    /// latest fight against a boss is the only record of the fleet used on it.
    /// </summary>
    public IReadOnlyList<MetaBossFightRecord> MetaFights { get; init; } = [];
    public IReadOnlyDictionary<string, MetaFightShipInfo> MetaFightShips { get; init; } =
        new Dictionary<string, MetaFightShipInfo>();

    public RegalGearMap? GearMap { get; init; }
    public GearInventory GearInventory { get; init; } = new();
    public IReadOnlyDictionary<string, GearFleetShip> GearShips { get; init; } = new Dictionary<string, GearFleetShip>();
    public IReadOnlyDictionary<int, GearItemInfo> GearItems { get; init; } = new Dictionary<int, GearItemInfo>();
}

public sealed record LevelingStageOption(LevelingStage Stage, IReadOnlyList<LevelingBattleRecord> Records);

public sealed record GearItemInfo(int BaseId, string Name, string IconUrl, string Rarity, string TypeGroup)
{
    public string TypeLabel => EquipmentRow.TypeLabel(TypeGroup);
}
