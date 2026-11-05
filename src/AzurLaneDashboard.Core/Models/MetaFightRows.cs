using System.Globalization;

namespace AzurLaneDashboard.Core.Models;

/// <summary>One ship's line in a META fight's CS 40003 statistics.</summary>
/// <param name="HpRest">HP the ship kept, out of 10,000; 0 means it sank.</param>
public sealed record MetaFightShipStat(string Uid, long Damage, long DamageTaken, int HpRest, long MaxHit, int GearScore);

/// <summary>A gear piece a ship carried when the fight started.</summary>
public sealed record MetaFightGear(int Slot, int TemplateId, string Name, int Enhance);

/// <summary>How a ship that took part reads on screen: dock name and icon, Regal hull and position.</summary>
public sealed record MetaFightShipInfo(
    string Uid, string Name, string IconUrl, string Rarity, string Hull, string Position, int Level, string RegalUid);

/// <summary>A META boss fight read from CS 40001 (sortie) and CS 40003 (finish).</summary>
public sealed class MetaBossFightRecord
{
    /// <summary>Row of the sortie packet, or of the finish when the sortie was not captured.</summary>
    public long Id { get; init; }
    public string OwnerUid { get; init; } = "";
    public bool IsOwnBoss { get; init; }
    public DateTime? FinishedAt { get; init; }
    public int Score { get; init; }
    public int TotalSeconds { get; init; }
    /// <summary>Boss HP left at the end, out of 10,000.</summary>
    public int BossHpLeft { get; init; }
    public int BossEnemyId { get; init; }
    public long BossDamage { get; init; }
    public long BossTotalHp { get; init; }
    public bool AutoAtStart { get; init; }
    public int AutoSwitches { get; init; }
    /// <summary>Ships in sortie order (vanguard then main); empty when only the finish was captured.</summary>
    public IReadOnlyList<string> SortieUids { get; init; } = [];
    public IReadOnlyList<MetaFightShipStat> Stats { get; init; } = [];
    /// <summary>Gear each ship carried at the sortie; null for fights recorded before gear was copied.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<MetaFightGear>>? Gear { get; init; }

    public bool IsFinished => Stats.Count > 0 || BossEnemyId > 0;
}

/// <summary>A finding under a fight. Kind is "good", "warn" or "info"; the text always says it in words.</summary>
public sealed record MetaFightFinding(string Kind, string Title, string Text)
{
    public string Mark => Kind switch { "good" => "✓", "warn" => "!", _ => "i" };
}

public sealed class MetaFightShipRow
{
    public string Uid { get; init; } = "";
    public string Name { get; init; } = "";
    public string IconUrl { get; init; } = "";
    public string Rarity { get; init; } = "";
    public string Hull { get; init; } = "";
    /// <summary>"Main", "Vanguard" or "" when the ship has no community position.</summary>
    public string Position { get; init; } = "";
    /// <summary>"기함", "선두" or "".</summary>
    public string Role { get; init; } = "";
    public long Damage { get; init; }
    public double Share { get; init; }
    /// <summary>Damage against the fight's top ship, for the bar.</summary>
    public double BarFraction { get; init; }
    public long DamageTaken { get; init; }
    public int HpRest { get; init; }
    public long MaxHit { get; init; }
    public IReadOnlyList<MetaFightGear> Gear { get; init; } = [];

    public bool IsSunk => HpRest <= 0;
    public bool IsLow => !IsSunk && HpRest < 3000;
    public string PositionText => Position switch { "Main" => "주력", "Vanguard" => "전위", _ => "" };
    public string SubText => string.Join(" · ", new[] { PositionText, Role }.Where(part => part.Length > 0));
    public string DamageText => Damage.ToString("N0", CultureInfo.CurrentCulture);
    public string ShareText => Share.ToString("0.0%", CultureInfo.InvariantCulture);
    public string TakenText => $"받은 피해 {DamageTaken.ToString("N0", CultureInfo.CurrentCulture)}";
    public string MaxHitText => $"최대 1회 {MaxHit.ToString("N0", CultureInfo.CurrentCulture)}";
    public string HpText => IsSunk ? "격침" : $"HP {(HpRest / 100.0).ToString("0", CultureInfo.InvariantCulture)}%";
    /// <summary>"sunk", "low" or "ok", for the HP chip colour.</summary>
    public string HpKind => IsSunk ? "sunk" : IsLow ? "low" : "ok";
    public bool HasGear => Gear.Count > 0;
    public string GearText => string.Join(" · ", Gear.Where(item => item.TemplateId > 0)
        .Select(item => item.Enhance > 0 ? $"{item.Name} +{item.Enhance}" : item.Name));
}

public sealed class MetaFightRow
{
    public long Id { get; init; }
    public string Key => Id.ToString(CultureInfo.InvariantCulture);
    public DateTime? FinishedAt { get; init; }
    public bool IsOwnBoss { get; init; }
    public int BossLevel { get; init; }
    /// <summary>The fight began at (nearly) full boss HP, so its damage compares with other full fights.</summary>
    public bool IsFullFight { get; init; }
    public bool Killed { get; init; }
    public long BossDamage { get; init; }
    public long BossTotalHp { get; init; }
    /// <summary>Boss HP at the start, out of 1.</summary>
    public double StartFraction { get; init; }
    /// <summary>Boss HP left at the end, out of 1.</summary>
    public double LeftFraction { get; init; }
    public int TotalSeconds { get; init; }
    public bool AutoAtStart { get; init; }
    public int AutoSwitches { get; init; }
    public bool GearRecorded { get; init; }
    public IReadOnlyList<MetaFightShipRow> MainShips { get; init; } = [];
    public IReadOnlyList<MetaFightShipRow> VanguardShips { get; init; } = [];
    public IReadOnlyList<MetaFightFinding> Findings { get; init; } = [];

    public IEnumerable<MetaFightShipRow> Ships => MainShips.Concat(VanguardShips);
    public double DamageFraction => BossTotalHp <= 0 ? 0 : Math.Clamp(BossDamage / (double)BossTotalHp, 0, 1);
    /// <summary>
    /// HP the boss kept. CS 40003 rounds it down to 1/10,000 of the boss HP, so a fight that started at full HP
    /// (every recorded full fight so far) is measured exactly as total HP minus damage.
    /// </summary>
    public long ShortBy
    {
        get
        {
            if (Killed) return 0;
            var reported = LeftFraction * BossTotalHp;
            var fromFull = BossTotalHp - BossDamage;
            return fromFull >= reported && fromFull - reported <= BossTotalHp / 10000.0 + 1
                ? fromFull
                : (long)Math.Round(reported);
        }
    }
    public string WhenText => FinishedAt?.ToString("M/d HH:mm", CultureInfo.InvariantCulture) ?? $"#{Key}";
    public string OwnerText => IsOwnBoss ? "내 보스" : "지원";
    public string LevelText => BossLevel > 0 ? $"Lv.{BossLevel}" : "";
    public string DamageText => BossDamage.ToString("N0", CultureInfo.CurrentCulture);
    public string PercentText => $"HP의 {DamageFraction.ToString("0.0%", CultureInfo.InvariantCulture)}";
    public string ResultText => Killed
        ? IsFullFight ? $"처치 · {TotalSeconds}초" : $"마무리 처치 · {TotalSeconds}초"
        : IsFullFight ? $"{LeftFraction.ToString("0.0%", CultureInfo.InvariantCulture)} 남음"
        : $"마무리 · {StartFraction.ToString("0.0%", CultureInfo.InvariantCulture)}에서 시작";
    /// <summary>"good" when the boss fell, "warn" when a full fight left HP, "info" for a mop-up.</summary>
    public string ResultKind => Killed ? "good" : IsFullFight ? "warn" : "info";
    public string TimeText => $"{TotalSeconds}초 · {(AutoAtStart ? "자동" : "수동")}" + (AutoSwitches > 0 ? $" · 전환 {AutoSwitches}회" : "");
    public string ShareSummaryText
    {
        get
        {
            var total = Ships.Sum(ship => ship.Damage);
            if (total <= 0) return "";
            var main = MainShips.Sum(ship => ship.Damage) / (double)total;
            return $"주력 {main.ToString("0%", CultureInfo.InvariantCulture)} · 전위 {(1 - main).ToString("0%", CultureInfo.InvariantCulture)}";
        }
    }
    public string GearNoteText => GearRecorded ? "출격 당시 장비" : "기록 없음 · 기능 추가 전 전투";
}

/// <summary>Every recorded fight against one META boss, latest first, with what stands out in each.</summary>
public sealed class MetaFightLog
{
    public static MetaFightLog Empty { get; } = new();

    public IReadOnlyList<MetaFightRow> Fights { get; init; } = [];
    /// <summary>The fleet of the latest fight against this boss: the closest thing to its META fleet the app can see.</summary>
    public IReadOnlyList<string> LatestFleetUids => Fights.FirstOrDefault()?.Ships.Select(ship => ship.Uid).ToList() ?? [];
    public int OwnCount => Fights.Count(fight => fight.IsOwnBoss);
    public int SupportCount => Fights.Count(fight => !fight.IsOwnBoss);
    public long BestFullDamage => Fights.Where(fight => fight.IsFullFight).Select(fight => fight.BossDamage).DefaultIfEmpty(0).Max();
    public bool HasFights => Fights.Count > 0;
    public string SummaryText => HasFights
        ? $"내 보스 {OwnCount} · 지원 {SupportCount}" +
          (BestFullDamage > 0 ? $" · 최고 {BestFullDamage.ToString("N0", CultureInfo.CurrentCulture)}" : "")
        : "";
}
