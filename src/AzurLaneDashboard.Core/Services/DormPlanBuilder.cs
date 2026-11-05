using System.Globalization;
using AzurLaneDashboard.Core.Models;

namespace AzurLaneDashboard.Core.Services;

/// <summary>
/// The 숙소 card on the 쫄작 tab: whether the farming fleet rests on floor 2 (morale recovers faster there), and
/// which waiting ships should train on floor 1 (the only floor that gives EXP).
/// </summary>
public static class DormPlanBuilder
{
    private const int NamesShown = 3;

    /// <param name="ships">The leveling planner's ships, with growth goals already marked.</param>
    /// <param name="farmingNow">Ships the planned farming fleet already levels; they earn EXP in battle.</param>
    public static DormPlan Build(
        DormState dorm, IReadOnlyList<LevelingShip> ships, IReadOnlySet<string> farmingNow, ShipLevelCaps caps)
    {
        if (!dorm.IsKnown || dorm.SlotsPerFloor <= 0) return DormPlan.Unknown;

        var slots = dorm.SlotsPerFloor;
        var byUid = ships.GroupBy(ship => ship.Uid, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var farm = dorm.FarmFleet;
        var farmUids = farm.Select(ship => ship.Uid).ToHashSet(StringComparer.Ordinal);
        var onTraining = dorm.CountOn(DormState.TrainingFloor);
        var onRest = dorm.CountOn(DormState.RestFloor);
        var restOpen = dorm.FloorCount >= DormState.RestFloor;

        bool Training(LevelingShip ship) => dorm.Ships.GetValueOrDefault(ship.Uid)?.Floor == DormState.TrainingFloor;
        // Growth goals and content picks first; a ship the player already trains keeps its seat against a pick
        // that only rates higher, so the card never asks to reshuffle the floor for the rating alone.
        int Tier(LevelingShip ship) => ship.IsGrowthGoal ? 0
            : ship.ContentReach > 0 || ship.IsMetaGuideShip ? 1
            : Training(ship) ? 2
            : 3;
        var picks = ships
            .Where(ship => WantsTraining(ship, caps) && !farmUids.Contains(ship.Uid) && !farmingNow.Contains(ship.Uid))
            .OrderBy(Tier)
            .ThenByDescending(ship => ship.BestRatingScore)
            .ThenByDescending(ship => ship.Level)
            .ThenBy(ship => ship.Uid, StringComparer.Ordinal)
            .Take(slots)
            .ToList();
        var pickUids = picks.Select(ship => ship.Uid).ToHashSet(StringComparer.Ordinal);
        var others = dorm.Ships.Values
            .Where(placed => placed.Floor == DormState.TrainingFloor && !pickUids.Contains(placed.Uid))
            .Select(placed => OtherRow(placed, byUid.GetValueOrDefault(placed.Uid), farmUids.Contains(placed.Uid), caps))
            .OrderBy(row => row.StatusKind == "out" ? 0 : 1)
            .ThenBy(row => row.Name, StringComparer.CurrentCulture)
            .ToList();

        return new DormPlan
        {
            IsKnown = true,
            HeaderText = restOpen
                ? $"1층 {onTraining}/{slots} · 2층 {onRest}/{slots}"
                : $"1층 {onTraining}/{slots} · 2층 잠김",
            RestTitle = farm.Count == 0
                ? "2층 휴식 · 쫄작 함대"
                : $"2층 휴식 · {FleetName(dorm.FarmFleetId)} {farm.Count(ship => ship.Floor == DormState.RestFloor)}/{farm.Count}척",
            RestRows = farm.Select(RestRow).ToList(),
            RestAdvice = RestAdvice(dorm, slots, onRest, restOpen),
            TrainTitle = $"1층 훈련 추천 · {slots}칸",
            TrainRows = picks.Select(ship => TrainRow(ship, dorm.Ships.GetValueOrDefault(ship.Uid))).ToList(),
            TrainOthers = others,
            TrainAdvice = TrainAdvice(picks, dorm, others, slots - onTraining)
        };
    }

    /// <summary>A locked ship below Lv.120 that can still gain EXP: not stuck at a limit-break or awakening cap.</summary>
    public static bool WantsTraining(LevelingShip ship, ShipLevelCaps caps) =>
        ship.IsLocked && !ship.IsBulin && ship.Level is > 0 and < LevelingPlanBuilder.ReadyLevel && CanGainExp(ship, caps);

    private static bool CanGainExp(LevelingShip ship, ShipLevelCaps caps) =>
        ship.Level < ShipExperienceCalculator.MaximumLevel &&
        !(ship.TemplateMaxLevel is > 0 and < 100 && ship.Level >= ship.TemplateMaxLevel) &&
        !caps.IsWaitingForAwakening(ship.Level, ship.Experience, ship.Rarity);

    private static DormRow RestRow(DormShip ship)
    {
        var (status, kind, note) = ship.Floor switch
        {
            DormState.RestFloor => ("2층", "in", ""),
            DormState.TrainingFloor => ("1층", "next", " · 2층이 전의 회복이 빠름"),
            _ => ("숙소 밖", "out", "")
        };
        return Row(ship.Uid, ship.Name, ship.IconUrl, ship.Rarity, $"Lv.{ship.Level}{note}", status, kind);
    }

    private static string RestAdvice(DormState dorm, int slots, int onRest, bool restOpen)
    {
        var farm = dorm.FarmFleet;
        if (farm.Count == 0) return "쫄작 함대 기록이 없어 2층을 점검하지 못했습니다.";
        if (!restOpen) return "숙소 2층이 아직 열리지 않았습니다.";

        var missing = farm.Where(ship => ship.Floor == 0).ToList();
        var onTraining = farm.Where(ship => ship.Floor == DormState.TrainingFloor).ToList();
        if (missing.Count == 0 && onTraining.Count == 0) return $"{FleetName(dorm.FarmFleetId)}가 모두 2층에서 쉬고 있습니다.";

        var parts = new List<string>();
        if (missing.Count > 0)
        {
            var free = Math.Max(0, slots - onRest);
            var farmUids = farm.Select(ship => ship.Uid).ToHashSet(StringComparer.Ordinal);
            var outsiders = dorm.Ships.Values
                .Where(ship => ship.Floor == DormState.RestFloor && !farmUids.Contains(ship.Uid))
                .OrderByDescending(ship => ship.Level)
                .ToList();
            var need = missing.Count - free;
            if (need <= 0)
                parts.Add($"2층 빈자리 {free}칸에 {Names(missing)}을(를) 넣으세요.");
            else if (outsiders.Count > 0)
                parts.Add($"2층이 가득 찼습니다. 쫄작 함대가 아닌 {Names(outsiders.Take(need).ToList())}을(를) 빼고 " +
                          $"{Names(missing)}을(를) 넣으세요.");
            else
                parts.Add($"2층이 가득 찼습니다. {Names(missing)}은(는) 숙소 밖에 있습니다.");
        }

        if (onTraining.Count > 0)
            parts.Add($"{Names(onTraining)}은(는) 1층에 있습니다. 2층으로 옮기면 전의가 더 빨리 회복됩니다.");
        return string.Join(" ", parts);
    }

    private static DormRow TrainRow(LevelingShip ship, DormShip? placed)
    {
        var (status, kind) = placed?.Floor switch
        {
            DormState.TrainingFloor => ("1층", "in"),
            DormState.RestFloor => ("2층 → 1층", "next"),
            _ => ("넣기", "next")
        };
        var reason = ship.IsGrowthGoal ? "성장 목표"
            : ship.ContentLabels.Count > 0 ? string.Join("·", ship.ContentLabels.Take(2))
            : ship.IsMetaGuideShip ? "META 공략"
            : placed?.Floor == DormState.TrainingFloor ? "훈련 중"
            : "평가 상위";
        if (ship.Position == "Submarine") reason += " · 잠수함";
        return Row(ship.Uid, ship.Name, ship.IconUrl, ship.Rarity, $"Lv.{ship.Level} · {reason}", status, kind);
    }

    private static DormRow OtherRow(DormShip placed, LevelingShip? ship, bool inFarmFleet, ShipLevelCaps caps)
    {
        var level = ship?.Level ?? placed.Level;
        string status, kind, why;
        if (level >= ShipExperienceCalculator.MaximumLevel)
            (status, kind, why) = ("EXP 못 받음", "out", "최대 레벨");
        else if (ship is not null && ship.TemplateMaxLevel is > 0 and < 100 && ship.Level >= ship.TemplateMaxLevel)
            (status, kind, why) = ("EXP 못 받음", "out", "한계돌파 필요");
        else if (ship is not null && caps.IsWaitingForAwakening(ship.Level, ship.Experience, ship.Rarity))
            (status, kind, why) = ("EXP 못 받음", "out", "각성 필요");
        else if (inFarmFleet)
            (status, kind, why) = ("쫄작 함대", "wait", "2층이 전의 회복이 빠름");
        else
            (status, kind, why) = ("추천 밖", "wait", ship is null ? "평가 데이터 없음"
                : !ship.IsLocked ? "잠금 안 됨"
                : ship.IsBulin ? "불린"
                : level >= LevelingPlanBuilder.ReadyLevel ? "Lv.120 이상"
                : "추천 순위 밖");
        return Row(placed.Uid, placed.Name, placed.IconUrl, placed.Rarity, $"Lv.{level} · {why}", status, kind);
    }

    private static string TrainAdvice(IReadOnlyList<LevelingShip> picks, DormState dorm, IReadOnlyList<DormRow> others, int free)
    {
        if (picks.Count == 0) return "1층에서 EXP를 받을 대기 함선이 없습니다.";
        var toAdd = picks.Where(ship => dorm.Ships.GetValueOrDefault(ship.Uid)?.Floor != DormState.TrainingFloor)
            .Select(ship => ship.Name).ToList();
        if (toAdd.Count == 0) return "추천 함선이 모두 1층에서 훈련 중입니다.";
        free = Math.Max(0, free);
        if (free >= toAdd.Count) return $"1층 빈자리 {free}칸에 {Names(toAdd)}을(를) 넣으세요.";

        // Swap out ships that gain nothing first, then the ones the recommendation ranks lower.
        var swap = others.Where(row => row.StatusKind == "out")
            .Concat(others.Where(row => row.StatusKind != "out"))
            .Take(toAdd.Count - free)
            .Select(row => row.Name)
            .ToList();
        if (swap.Count == 0) return $"1층이 가득 찼습니다. {Names(toAdd)}은(는) 자리가 나면 넣으세요.";
        return $"1층이 가득 찼습니다. {Names(swap)} 대신 {Names(toAdd.Take(free + swap.Count).ToList())}을(를) 넣으면 " +
               "대기 중인 함선이 숙소 EXP를 받습니다.";
    }

    private static DormRow Row(string uid, string name, string icon, string rarity, string sub, string status, string kind) =>
        new() { Uid = uid, Name = name, IconUrl = icon, Rarity = rarity, SubText = sub, StatusText = status, StatusKind = kind };

    private static string FleetName(int fleetId) =>
        fleetId > 0 ? string.Create(CultureInfo.InvariantCulture, $"{fleetId}함대") : "쫄작 함대";

    private static string Names(IReadOnlyList<DormShip> ships) => Names(ships.Select(ship => ship.Name).ToList());

    private static string Names(IReadOnlyList<string> names) => names.Count <= NamesShown
        ? string.Join(", ", names)
        : $"{string.Join(", ", names.Take(NamesShown))} 외 {names.Count - NamesShown}척";
}
