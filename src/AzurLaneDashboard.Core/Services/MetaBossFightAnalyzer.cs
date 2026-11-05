using System.Globalization;
using AzurLaneDashboard.Core.Models;

namespace AzurLaneDashboard.Core.Services;

/// <summary>
/// Turns recorded META fights against one boss into rows and findings. Findings only state what the packets
/// show: the result, ships that sank, the change against the previous full fight, the fleet against the
/// recommendation, and (for the latest fight) gear Regal would change. Packets carry no timeline, so nothing
/// here says when a ship sank or how much damage that cost.
/// </summary>
public static class MetaBossFightAnalyzer
{
    /// <summary>A fight that began above this share of the boss HP counts as a full fight.</summary>
    public const double FullFightStart = 0.99;

    public static MetaFightLog Build(
        MetaBoss boss,
        MetaBossGuide? guide,
        IReadOnlyList<MetaBossFightRecord> fights,
        IReadOnlyDictionary<string, MetaFightShipInfo> ships,
        MetaBossPlan? plan = null,
        IReadOnlyList<GearShipPlan>? latestGear = null)
    {
        var levelByEnemy = boss.Levels.Where(level => level.EnemyId > 0)
            .GroupBy(level => level.EnemyId)
            .ToDictionary(group => group.Key, group => group.First());
        var rows = fights
            .Where(fight => fight.IsFinished && levelByEnemy.ContainsKey(fight.BossEnemyId))
            .OrderBy(fight => fight.Id)
            .Select(fight => Row(fight, levelByEnemy[fight.BossEnemyId], ships))
            .ToList();
        var lead = guide is { Verified: true }
            ? guide.Mechanics.FirstOrDefault(mechanic => mechanic.Response.Length > 0 &&
                                                         (mechanic.Title.Contains("선두", StringComparison.Ordinal) ||
                                                          mechanic.Text.Contains("선두", StringComparison.Ordinal)))
            : null;
        var analysed = rows.Select((row, index) => With(row, Findings(rows, index, lead, plan,
            index == rows.Count - 1 ? latestGear : null))).ToList();
        analysed.Reverse();
        return new MetaFightLog { Fights = analysed };
    }

    private static MetaFightRow Row(MetaBossFightRecord fight, MetaBossLevel level,
        IReadOnlyDictionary<string, MetaFightShipInfo> ships)
    {
        var total = fight.BossTotalHp > 0 ? fight.BossTotalHp : level.Hp;
        var left = Math.Clamp(fight.BossHpLeft / 10000.0, 0, 1);
        var start = total <= 0 ? 0 : Math.Clamp((fight.BossDamage + left * total) / total, 0, 1);

        // Sortie order (vanguard first, then main from the flagship) when the sortie was seen, else the finish's order.
        var order = (fight.SortieUids.Count > 0 ? fight.SortieUids : fight.Stats.Select(stat => stat.Uid))
            .Select((uid, index) => (uid, index))
            .GroupBy(item => item.uid, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First().index, StringComparer.Ordinal);
        var dealt = fight.Stats.Sum(stat => stat.Damage);
        var top = fight.Stats.Select(stat => stat.Damage).DefaultIfEmpty(0).Max();
        var entries = fight.Stats
            .OrderBy(stat => order.GetValueOrDefault(stat.Uid, int.MaxValue))
            .Select(stat => (Stat: stat, Info: ships.GetValueOrDefault(stat.Uid)))
            .ToList();

        List<MetaFightShipRow> Group(Func<string, bool> position, string role) => entries
            .Where(entry => position(entry.Info?.Position ?? ""))
            .Select((entry, index) => new MetaFightShipRow
            {
                Uid = entry.Stat.Uid,
                Name = entry.Info?.Name ?? entry.Stat.Uid,
                IconUrl = entry.Info?.IconUrl ?? "",
                Rarity = entry.Info?.Rarity ?? "",
                Hull = entry.Info?.Hull ?? "",
                Position = entry.Info?.Position ?? "",
                Role = index == 0 && role.Length > 0 ? role : "",
                Damage = entry.Stat.Damage,
                Share = dealt <= 0 ? 0 : entry.Stat.Damage / (double)dealt,
                BarFraction = top <= 0 ? 0 : entry.Stat.Damage / (double)top,
                DamageTaken = entry.Stat.DamageTaken,
                HpRest = entry.Stat.HpRest,
                MaxHit = entry.Stat.MaxHit,
                Gear = fight.Gear?.GetValueOrDefault(entry.Stat.Uid) ?? []
            })
            .ToList();

        var main = Group(position => position == "Main", "기함");
        // Ships without a community position stay visible after the main fleet rather than vanishing.
        main.AddRange(Group(position => position is not ("Main" or "Vanguard"), ""));
        return new MetaFightRow
        {
            Id = fight.Id,
            FinishedAt = fight.FinishedAt,
            IsOwnBoss = fight.IsOwnBoss,
            BossLevel = level.Level,
            IsFullFight = start >= FullFightStart,
            Killed = fight.BossHpLeft <= 0 && fight.BossDamage > 0,
            BossDamage = fight.BossDamage,
            BossTotalHp = total,
            StartFraction = start,
            LeftFraction = left,
            TotalSeconds = fight.TotalSeconds,
            AutoAtStart = fight.AutoAtStart,
            AutoSwitches = fight.AutoSwitches,
            GearRecorded = fight.Gear is not null,
            MainShips = main,
            VanguardShips = Group(position => position == "Vanguard", "선두")
        };
    }

    private static List<MetaFightFinding> Findings(IReadOnlyList<MetaFightRow> rows, int index,
        MetaBossMechanic? leadMechanic, MetaBossPlan? plan, IReadOnlyList<GearShipPlan>? gear)
    {
        var fight = rows[index];
        var findings = new List<MetaFightFinding> { Result(fight) };
        var earlierFull = rows.Take(index + 1).Where(row => row.IsFullFight).ToList();
        foreach (var ship in fight.Ships.Where(ship => ship.IsSunk))
        {
            var fought = earlierFull.Where(row => row.Ships.Any(item => item.Uid == ship.Uid)).ToList();
            var sank = fought.Count(row => row.Ships.Any(item => item.Uid == ship.Uid && item.IsSunk));
            var text = $"받은 피해 {N(ship.DamageTaken)}.";
            if (fought.Count >= 2) text += $" 이 보스 풀 전투 {fought.Count}번 중 {sank}번 격침됐습니다.";
            if (ship.Role == "선두" && leadMechanic is not null)
                text += $" 공략 기믹 '{leadMechanic.Title}' → {leadMechanic.Response}.";
            findings.Add(new MetaFightFinding("warn", $"{(ship.Role.Length > 0 ? ship.Role + " " : "")}{ship.Name} 격침", text));
        }

        var previous = fight.IsFullFight ? rows.Take(index).LastOrDefault(row => row.IsFullFight) : null;
        if (previous is not null && previous.BossDamage > 0)
        {
            var changes = Changes(previous, fight);
            var changeText = changes.Count > 0 ? $"바뀐 함선: {string.Join(", ", changes)}." : "편성은 같습니다.";
            if (fight.Killed && previous.Killed)
            {
                // Both fights dealt the boss's whole HP, so only the time tells them apart.
                var faster = previous.TotalSeconds - fight.TotalSeconds;
                findings.Add(new MetaFightFinding(faster >= 0 ? "good" : "info",
                    faster > 0 ? $"직전 풀 전투보다 {faster}초 빨리 처치"
                    : faster < 0 ? $"직전 풀 전투보다 {-faster}초 늦게 처치"
                    : $"직전 풀 전투와 같은 {fight.TotalSeconds}초에 처치",
                    $"둘 다 처치해 피해량이 같으므로 시간으로 비교했습니다. 비교한 전투: {previous.WhenText} {previous.OwnerText} {previous.TotalSeconds}초. {changeText}"));
            }
            else
            {
                var delta = fight.BossDamage - previous.BossDamage;
                findings.Add(new MetaFightFinding(delta >= 0 ? "good" : "warn",
                    $"직전 풀 전투보다 {Signed(delta)} ({(delta >= 0 ? "+" : "")}{(delta / (double)previous.BossDamage).ToString("0.0%", CultureInfo.InvariantCulture)})",
                    $"비교한 전투: {previous.WhenText} {previous.OwnerText} {previous.DamageText}. {changeText}"));
            }
        }

        if (plan is not null && plan.Slots.Any()) findings.Add(AgainstPlan(fight, plan));
        if (gear is not null)
        {
            var finding = Gear(fight, gear);
            if (finding is not null) findings.Add(finding);
        }

        return findings;
    }

    private static MetaFightFinding Result(MetaFightRow fight)
    {
        if (!fight.IsFullFight)
            return new MetaFightFinding("info", fight.Killed ? "마무리 처치" : "마무리 전투",
                $"보스 HP {fight.StartFraction.ToString("0.0%", CultureInfo.InvariantCulture)}에서 시작한 전투라 다른 전투와 피해량을 비교하지 않습니다.");
        return fight.Killed
            ? new MetaFightFinding("good", "처치", $"{fight.TotalSeconds}초 만에 보스를 처치했습니다.")
            : new MetaFightFinding("warn", $"처치까지 {N(fight.ShortBy)} 부족",
                $"보스 HP의 {fight.DamageFraction.ToString("0.0%", CultureInfo.InvariantCulture)}를 깎고 {fight.LeftFraction.ToString("0.0%", CultureInfo.InvariantCulture)}가 남았습니다.");
    }

    // Slot by slot within each position, in sortie order.
    private static List<string> Changes(MetaFightRow before, MetaFightRow after)
    {
        var changes = new List<string>();
        foreach (var (was, now) in new[] { (before.MainShips, after.MainShips), (before.VanguardShips, after.VanguardShips) })
        {
            for (var slot = 0; slot < Math.Max(was.Count, now.Count); slot++)
            {
                var from = slot < was.Count ? was[slot] : null;
                var to = slot < now.Count ? now[slot] : null;
                if (from?.Uid == to?.Uid) continue;
                changes.Add($"{from?.Name ?? "빈 칸"} → {to?.Name ?? "빈 칸"}");
            }
        }

        return changes;
    }

    private static MetaFightFinding AgainstPlan(MetaFightRow fight, MetaBossPlan plan)
    {
        var planned = plan.Slots.Select(slot => slot.ShipUid).ToHashSet(StringComparer.Ordinal);
        var actual = fight.Ships.Select(ship => ship.Uid).ToHashSet(StringComparer.Ordinal);
        var plannedFlagship = plan.Slots.FirstOrDefault(slot => slot.IsFlagship);
        var flagship = fight.MainShips.FirstOrDefault();
        var flagshipNote = plannedFlagship is not null && flagship is not null && plannedFlagship.ShipUid != flagship.Uid
            ? $" 기함은 추천({plannedFlagship.Name})과 다릅니다."
            : "";
        if (actual.SetEquals(planned))
            return new MetaFightFinding(flagshipNote.Length > 0 ? "info" : "good", "추천 편성과 같음",
                $"{actual.Count}척 모두 위 추천 편성과 같습니다." + flagshipNote);

        var pairs = new List<string>();
        foreach (var (ships, slots) in new[] { (fight.MainShips, plan.MainSlots), (fight.VanguardShips, plan.VanguardSlots) })
        {
            var extra = ships.Where(ship => !planned.Contains(ship.Uid)).ToList();
            var missing = slots.OfType<MetaBossSlot>().Where(slot => !actual.Contains(slot.ShipUid)).ToList();
            for (var index = 0; index < Math.Max(extra.Count, missing.Count); index++)
            {
                var from = index < extra.Count ? extra[index].Name : "빈 칸";
                pairs.Add(index < missing.Count ? $"{from} → 추천 {missing[index].Name}" : $"{from} (추천에 없음)");
            }
        }

        return new MetaFightFinding("info", $"추천과 다른 함선 {actual.Count(uid => !planned.Contains(uid))}척",
            string.Join(", ", pairs) + "." + flagshipNote);
    }

    private static MetaFightFinding? Gear(MetaFightRow fight, IReadOnlyList<GearShipPlan> gear)
    {
        var names = fight.Ships.ToDictionary(ship => ship.Uid, ship => ship.Name, StringComparer.Ordinal);
        var ranked = gear.Where(plan => names.ContainsKey(plan.Ship.Uid) &&
                                        plan.Slots.Any(slot => slot.Action != GearSlotAction.NoData))
            .ToList();
        if (ranked.Count == 0) return null;
        var changing = ranked.Where(plan => plan.ChangeCount > 0).ToList();
        if (changing.Count == 0)
            return new MetaFightFinding("good", "장비는 Regal 순위대로",
                "지금 장착한 장비에서 창고나 다른 함선의 장비로 바꿀 칸이 없습니다.");
        return new MetaFightFinding("info", $"장비 교체 가능 {changing.Sum(plan => plan.ChangeCount)}칸",
            string.Join(" · ", changing.Select(plan => $"{names[plan.Ship.Uid]} {plan.ChangeCount}칸")) +
            ". 지금 장비 기준이며, 위 장비 추천에서 함선을 누르면 칸별로 보입니다.");
    }

    private static MetaFightRow With(MetaFightRow row, IReadOnlyList<MetaFightFinding> findings) => new()
    {
        Id = row.Id,
        FinishedAt = row.FinishedAt,
        IsOwnBoss = row.IsOwnBoss,
        BossLevel = row.BossLevel,
        IsFullFight = row.IsFullFight,
        Killed = row.Killed,
        BossDamage = row.BossDamage,
        BossTotalHp = row.BossTotalHp,
        StartFraction = row.StartFraction,
        LeftFraction = row.LeftFraction,
        TotalSeconds = row.TotalSeconds,
        AutoAtStart = row.AutoAtStart,
        AutoSwitches = row.AutoSwitches,
        GearRecorded = row.GearRecorded,
        MainShips = row.MainShips,
        VanguardShips = row.VanguardShips,
        Findings = findings
    };

    private static string N(long value) => value.ToString("N0", CultureInfo.CurrentCulture);

    private static string Signed(long value) => (value >= 0 ? "+" : "−") + N(Math.Abs(value));
}
