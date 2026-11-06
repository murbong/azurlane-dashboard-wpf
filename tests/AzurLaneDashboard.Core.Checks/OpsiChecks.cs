using System.Text.Json;
using AzurLaneDashboard.Core.Models;
using AzurLaneDashboard.Core.Services;

internal static class OpsiChecks
{
    public static void Run()
    {
        using var document = JsonDocument.Parse("""
            {
              "areas": {
                "14": { "id": 14, "serial": "14", "region": "NA해역 사우스웨스트", "normal": [1401], "cryptic": [1404, 1405],
                        "awards": [ { "star": 1, "type": 12, "id": 101, "count": 50 }, { "star": 2, "type": 2, "id": 20013, "count": 2 } ] },
                "15": { "id": 15, "serial": "15", "region": "NA해역 센터", "normal": [1501], "cryptic": [1504], "awards": [] }
              },
              "targets": {
                "1401": { "id": 1401, "name": "1차 확보", "desc": "해역 1개 지점 확보 완료", "conditions": [ { "trigger": 1401, "count": 1 } ] },
                "1404": { "id": 1404, "name": "1차 탐색", "desc": "세이렌 탐지 장치 2회 사용", "conditions": [ { "trigger": 1404, "count": 2 } ] },
                "1405": { "id": 1405, "name": "2차 탐색", "desc": "세이렌 정보 수집 장치 3회 사용", "conditions": [ { "trigger": 1405, "count": 3 } ] },
                "1501": { "id": 1501, "name": "완전 확보", "desc": "비정상 구역 5개 지점 정찰", "conditions": [ { "trigger": 1501, "count": 5 } ] },
                "1504": { "id": 1504, "name": "1차 탐색", "desc": "세이렌 탐지 장치 1회 사용", "conditions": [ { "trigger": 1504, "count": 1 } ] }
              },
              "items": { "12:101": "특별 교환 토큰", "2:20013": "냥박스 SSR형" }
            }
            """);
        var catalog = OpsiCatalog.Parse(document.RootElement);
        var state = new OpsiState
        {
            IsKnown = true,
            Progress = new Dictionary<int, IReadOnlyDictionary<int, int>>
            {
                [1401] = new Dictionary<int, int> { [1401] = 1 },
                [1404] = new Dictionary<int, int> { [1404] = 2 },
                [1405] = new Dictionary<int, int> { [1405] = 2 },
                [1501] = new Dictionary<int, int> { [1501] = 4 }
            },
            Claimed = new Dictionary<int, IReadOnlySet<int>> { [14] = new HashSet<int> { 1 } },
            Items = new Dictionary<int, int> { [OpsiStarBuilder.EnergyContainerItem] = 1, [OpsiStarBuilder.SpecialTokenItem] = 4 },
            Adaptability = (10, 20, 30)
        };

        var plan = OpsiStarBuilder.Build(catalog, state);
        Check(plan is { HasData: true, Stars: 2, TotalStars: 5, CompleteAreas: 0, AreaCount: 2, HiddenTotal: 3, HiddenDone: 1 },
            "stars count achieved normal and hidden targets");
        Check(plan.Claimable.Count == 1 && plan.Claimable[0] is { Star: 2, RewardText: "냥박스 SSR형 ×2" },
            "a reached star whose reward was not claimed is listed");
        Check(plan.HiddenStillMasked == 1, "a hidden target in an area with an open normal target is still ??? in the game");
        Check(plan.Groups.Select(group => group.Action).SequenceEqual(["비정상 구역 정찰", "세이렌 정보 수집 장치 사용", "세이렌 탐지 장치 사용"]) &&
              plan.Groups.First(group => group.Action == OpsiStarBuilder.DetectionDevice).Rows[0] is { AreaLabel: "센터 15", Left: 1 },
            "unfinished targets group by action without the counts");
        Check(plan.QuickWins.Count == 3 && plan is { InfoDeviceUses: 1, DetectionDeviceUses: 1, DetectionTokenNeed: 2, CanFinishDevices: true },
            "one-step targets and device item needs");
        Check(plan.Areas.First(area => area.Label == "사우스웨스트 14").RewardText == "보상 모두 받음" ||
              plan.Areas.First(area => area.Label == "사우스웨스트 14").RewardText.StartsWith("다음", StringComparison.Ordinal),
            "area rows name the next reward");
        Check(!OpsiStarBuilder.Build(catalog, new OpsiState()).HasData, "no entry snapshot, no page");
        Console.WriteLine("Operation Siren checks passed.");
    }

    private static void Check(bool condition, string description)
    {
        if (!condition) throw new InvalidOperationException(description);
    }
}
