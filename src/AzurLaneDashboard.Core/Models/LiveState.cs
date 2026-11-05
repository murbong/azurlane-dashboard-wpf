namespace AzurLaneDashboard.Core.Models;

/// <summary>The newest battle result (SC 40004) with its request (CS 40003); <see cref="Score"/> is -1 when unknown.</summary>
public sealed record LatestBattleInfo(
    long RowId,
    int StageId,
    string StageLabel,
    int Score,
    string MvpUid,
    string MvpName,
    int ShipDrops)
{
    /// <summary>BattleConst.BattleScore: D 0, C 1, B 2, A 3, S 4.</summary>
    public string Rank => Score switch { 4 => "S", 3 => "A", 2 => "B", 1 => "C", 0 => "D", _ => "" };
}

/// <summary>
/// live_state.json for macro tools such as ALAuto's "game" conditions. Values the captured packets state, or that
/// follow from them with the client's own rules (oil between syncs, courtyard food); morale is left out, and so is
/// the dock because its capacity includes a guild bonus the captured packets do not carry.
/// </summary>
public sealed record LiveStateDocument(
    int Schema,
    string WrittenAt,
    double WrittenUnix,
    LiveCaptureState Capture,
    LiveBattleState Battle,
    LiveChapterState Chapter,
    LiveLevelingState Leveling,
    LiveResourcesState Resources,
    LiveDormState Dorm)
{
    public const int CurrentSchema = 1;
}

public sealed record LiveCaptureState(bool Running);

/// <summary><see cref="LastId"/> is the decoded packet row of the result and only grows; 0 before any battle.</summary>
public sealed record LiveBattleState(
    long LastId,
    int StageId,
    string Stage,
    string Rank,
    int Score,
    string MvpUid,
    string MvpName,
    int ShipDrops);

/// <summary>The chapter last entered (SC 13102); 0 when none was captured.</summary>
public sealed record LiveChapterState(int Id);

/// <summary>
/// The game fleet that farms (detected from recent battles) and its ships under Lv.120. <see cref="Growing"/>
/// counts the ones that still level up; it drops when one reaches Lv.120, an awakening cap with full EXP or
/// its limit-break cap, which is when the fleet needs rotating.
/// </summary>
public sealed record LiveLevelingState(
    int FarmFleetId,
    int Growing,
    bool NextStopReached,
    IReadOnlyList<LiveLevelingShip> Ships);

public sealed record LiveLevelingShip(string Uid, string Name, int Level, bool Waiting);

/// <summary>
/// Oil as <see cref="OilEstimate"/> follows it: <see cref="Oil"/> is the estimate, <see cref="OilSynced"/> the
/// server's last number and <see cref="OilEstimated"/> whether anything was added or taken since. Null before
/// any sync was captured.
/// </summary>
public sealed record LiveResourcesState(int? Oil, int? OilSynced, bool OilEstimated, int OilBattlesSinceSync);

/// <summary>
/// The courtyard as <see cref="DormFoodState"/> counts it at the time of writing; null before SC 19001.
/// <see cref="Snacks"/> maps each snack's food value ("1000" … "20000") to how many are left.
/// </summary>
public sealed record LiveDormState(
    int? Food,
    int? Capacity,
    double? Fill,
    int TrainingShips,
    IReadOnlyDictionary<string, int> Snacks);
