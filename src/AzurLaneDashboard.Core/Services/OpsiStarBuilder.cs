using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using AzurLaneDashboard.Core.Models;

namespace AzurLaneDashboard.Core.Services;

/// <summary>maps/world_targets.json: sea areas, targets and reward item names.</summary>
public sealed class OpsiCatalog
{
    public IReadOnlyList<OpsiArea> Areas { get; init; } = [];
    public IReadOnlyDictionary<int, OpsiTarget> Targets { get; init; } = new Dictionary<int, OpsiTarget>();
    /// <summary>"type:id" → name.</summary>
    public IReadOnlyDictionary<string, string> ItemNames { get; init; } = new Dictionary<string, string>();

    public static OpsiCatalog Parse(JsonElement map)
    {
        if (map.ValueKind != JsonValueKind.Object) return new OpsiCatalog();
        static int Int(JsonElement element, string name) =>
            element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetInt32() : 0;
        static string Text(JsonElement element, string name) =>
            element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
        static IEnumerable<JsonElement> Items(JsonElement element, string name) =>
            element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Array ? value.EnumerateArray() : [];
        static IEnumerable<JsonProperty> Entries(JsonElement element, string name) =>
            element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Object ? value.EnumerateObject() : [];

        var areas = Entries(map, "areas").Select(entry => entry.Value).Select(area => new OpsiArea(
                Int(area, "id"), Text(area, "serial"), Text(area, "region"),
                Items(area, "normal").Select(item => item.GetInt32()).ToList(),
                Items(area, "cryptic").Select(item => item.GetInt32()).ToList(),
                Items(area, "awards").Select(award => new OpsiAward(Int(award, "star"), Int(award, "type"), Int(award, "id"),
                    Int(award, "count"))).OrderBy(award => award.Star).ToList()))
            .Where(area => area.Id > 0)
            .OrderBy(area => area.Id)
            .ToList();
        var targets = Entries(map, "targets").Select(entry => entry.Value).Select(target => new OpsiTarget(
                Int(target, "id"), Text(target, "name"), Text(target, "desc"),
                Items(target, "conditions").Select(condition => (Int(condition, "trigger"), Int(condition, "count"))).ToList()))
            .Where(target => target.Id > 0)
            .ToDictionary(target => target.Id);
        var names = Entries(map, "items").Where(entry => entry.Value.ValueKind == JsonValueKind.String)
            .ToDictionary(entry => entry.Name, entry => entry.Value.GetString() ?? "", StringComparer.Ordinal);
        return new OpsiCatalog { Areas = areas, Targets = targets, ItemNames = names };
    }
}

/// <summary>
/// The 대형작전 star helper. Stars are achieved targets (normal and hidden); each area pays a reward at set star
/// counts. Unfinished targets are grouped by the action they ask for, with the game's own description of the
/// object involved.
/// </summary>
public static class OpsiStarBuilder
{
    /// <summary>World item 110, which the Siren devices consume.</summary>
    public const int EnergyContainerItem = 110;
    /// <summary>World item 101, the alternative cost of the detection device's resource search (2 per use).</summary>
    public const int SpecialTokenItem = 101;
    public const string DetectionDevice = "세이렌 탐지 장치 사용";
    public const string InfoDevice = "세이렌 정보 수집 장치 사용";

    // From world_event_data scan_desc and the event dialogs (gamecfg/story w1015, w1020).
    private static readonly IReadOnlyDictionary<string, (string What, string How)> Guides = new Dictionary<string, (string, string)>
    {
        [DetectionDevice] = ("해역에 설치된 세이렌 설비로, 이동해서 사용합니다.",
            "쓸 때마다 에너지 저장 용기 1개(숨은 적 찾기) 또는 특별 교환 토큰 2개(숨은 자원 찾기)가 듭니다."),
        [InfoDevice] = ("에너지가 바닥난 세이렌 신호 발생 장치입니다.",
            "「설비 가동」에 에너지 저장 용기 1개가 듭니다. 「설비 파괴」는 재료만 회수합니다."),
        ["기관 활성화"] = ("두 함대가 동시에 조작해야 켜지는 장치입니다.", "함대 두 개를 기관에 함께 보내세요."),
        ["신비한 상인 조우"] = ("보급품을 파는 신비한 상인 이벤트입니다.", "해역에서 상인을 만나면 횟수가 오릅니다."),
        ["비정상 구역 정찰"] = ("일반 목표입니다.", "비정상 구역 지점을 정찰하면 오릅니다.")
    };

    private static readonly Regex CountPattern = new(@"\s*\d+\s*(회|개 지점|개)", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static OpsiDashboard Build(OpsiCatalog catalog, OpsiState state)
    {
        if (!state.IsKnown || catalog.Areas.Count == 0) return OpsiDashboard.Empty;

        var open = new List<OpsiTargetRow>();
        var areaRows = new List<OpsiAreaRow>();
        var claimable = new List<OpsiClaimRow>();
        int stars = 0, total = 0, complete = 0, hiddenTotal = 0, hiddenDone = 0, masked = 0;
        foreach (var area in catalog.Areas)
        {
            var label = AreaLabel(area);
            var normalDone = area.Normal.All(id => Achieved(catalog, state, id));
            var achieved = area.Normal.Concat(area.Cryptic).Count(id => Achieved(catalog, state, id));
            var count = area.Normal.Count + area.Cryptic.Count;
            stars += achieved;
            total += count;
            if (achieved == count) complete++;
            hiddenTotal += area.Cryptic.Count;
            hiddenDone += area.Cryptic.Count(id => Achieved(catalog, state, id));

            var claimed = state.Claimed.GetValueOrDefault(area.Id) ?? new HashSet<int>();
            claimable.AddRange(area.Awards.Where(award => award.Star <= achieved && !claimed.Contains(award.Star))
                .Select(award => new OpsiClaimRow { AreaLabel = label, Star = award.Star, RewardText = Reward(catalog, award) }));
            var next = area.Awards.FirstOrDefault(award => !claimed.Contains(award.Star) && award.Star > achieved)
                       ?? area.Awards.FirstOrDefault(award => !claimed.Contains(award.Star));
            var rewardText = next is null ? "보상 모두 받음" : $"다음 {next.Star}★ {Reward(catalog, next)}";
            var starsText = $"{achieved}/{count}★";

            var rows = area.Normal.Select(id => (Id: id, Hidden: false)).Concat(area.Cryptic.Select(id => (Id: id, Hidden: true)))
                .Where(item => catalog.Targets.ContainsKey(item.Id) && !Achieved(catalog, state, item.Id))
                .Select(item =>
                {
                    var target = catalog.Targets[item.Id];
                    var (got, need) = Progress(target, state.Progress.GetValueOrDefault(item.Id));
                    return new OpsiTargetRow
                    {
                        AreaId = area.Id, AreaLabel = label, Action = Action(target.Desc), TargetName = target.Name,
                        Desc = target.Desc, IsHidden = item.Hidden, Got = got, Need = need, StarsText = starsText,
                        RewardText = rewardText
                    };
                })
                .ToList();
            masked += normalDone ? 0 : rows.Count(row => row.IsHidden);
            open.AddRange(rows);
            if (rows.Count > 0)
                areaRows.Add(new OpsiAreaRow { Label = label, StarsText = starsText, RewardText = rewardText, Targets = rows });
        }

        var groups = open.GroupBy(row => row.Action)
            .Select(group => new OpsiActionGroup
            {
                Action = group.Key,
                What = Guides.GetValueOrDefault(group.Key).What ?? "",
                How = Guides.GetValueOrDefault(group.Key).How ?? "",
                Rows = group.OrderBy(row => row.Left).ThenBy(row => row.AreaId).ToList()
            })
            .OrderByDescending(group => group.Rows.Count)
            .ThenBy(group => group.Action, StringComparer.Ordinal)
            .ToList();
        int Uses(string action) => groups.FirstOrDefault(group => group.Action == action)?.LeftTotal ?? 0;

        return new OpsiDashboard
        {
            HasData = true,
            BasisText = state.UpdatedAt.Length > 0 ? $"대형작전 진입 기록 · {state.UpdatedAt}" : "대형작전 진입 기록",
            Stars = stars,
            TotalStars = total,
            CompleteAreas = complete,
            AreaCount = catalog.Areas.Count,
            Groups = groups,
            Areas = areaRows.OrderBy(row => row.Targets.Sum(target => target.Left)).ToList(),
            QuickWins = open.Where(row => row.IsOneLeft).OrderBy(row => row.AreaId).ToList(),
            Claimable = claimable,
            EnergyContainers = state.Items.GetValueOrDefault(EnergyContainerItem),
            SpecialTokens = state.Items.GetValueOrDefault(SpecialTokenItem),
            InfoDeviceUses = Uses(InfoDevice),
            DetectionDeviceUses = Uses(DetectionDevice),
            HiddenTotal = hiddenTotal,
            HiddenDone = hiddenDone,
            HiddenStillMasked = masked,
            Adaptability = state.Adaptability
        };
    }

    /// <summary>A target is achieved when every condition's trigger has reached its count (WorldAchievement).</summary>
    public static bool Achieved(OpsiCatalog catalog, OpsiState state, int targetId)
    {
        if (!catalog.Targets.TryGetValue(targetId, out var target) || target.Conditions.Count == 0) return false;
        var progress = state.Progress.GetValueOrDefault(targetId);
        return target.Conditions.All(condition => (progress?.GetValueOrDefault(condition.Trigger) ?? 0) >= condition.Count);
    }

    // One condition shows its count; several show how many conditions are met, as the game does.
    private static (int Got, int Need) Progress(OpsiTarget target, IReadOnlyDictionary<int, int>? progress)
    {
        int Count(int trigger) => progress?.GetValueOrDefault(trigger) ?? 0;
        if (target.Conditions.Count == 1)
        {
            var (trigger, need) = target.Conditions[0];
            return (Math.Min(Count(trigger), need), need);
        }

        return (target.Conditions.Count(condition => Count(condition.Trigger) >= condition.Count), target.Conditions.Count);
    }

    private static string Action(string desc) => CountPattern.Replace(desc, "").Trim();

    private static string AreaLabel(OpsiArea area) =>
        $"{area.Region.Replace("NA해역", "", StringComparison.Ordinal).Trim()} {area.Serial}".Trim();

    private static string Reward(OpsiCatalog catalog, OpsiAward award)
    {
        var name = catalog.ItemNames.GetValueOrDefault(
            string.Create(CultureInfo.InvariantCulture, $"{award.Type}:{award.ItemId}"), $"#{award.ItemId}");
        return string.Create(CultureInfo.InvariantCulture, $"{name} ×{award.Count}");
    }
}
