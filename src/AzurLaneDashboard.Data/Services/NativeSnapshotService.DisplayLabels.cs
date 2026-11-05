using System.Globalization;
using AzurLaneDashboard.Core.Services;

namespace AzurLaneDashboard.Data.Services;

public sealed partial class NativeSnapshotService
{
    private static string SpWeaponRarityTier(int rarity, bool unique)
    {
        if (unique)
        {
            return "SSR";
        }

        return rarity switch
        {
            >= 4 => "SSR",
            3 => "SR",
            2 => "R",
            _ => "N"
        };
    }

    private static int RarityScore(string rarity)
    {
        return RarityFilterTier(rarity) switch
        {
            "UR" => 70,
            "SSR" => 52,
            "SR" => 32,
            "R" => 16,
            "N" => 4,
            _ => 0
        };
    }

    private static string RarityFilterTier(string rarity)
    {
        return RarityHelper.Normalize(rarity) switch
        {
            "DR" or "UR" => "UR",
            "PR" or "SSR" => "SSR",
            "SR" => "SR",
            "R" => "R",
            "N" => "N",
            _ => ""
        };
    }

    private static string RarityCssClass(string rarity)
    {
        var normalized = RarityHelper.Normalize(rarity);
        return string.IsNullOrWhiteSpace(normalized) ? "rarity-unknown" : $"rarity-{normalized}";
    }

    private static int EquipmentEnhanceLevel(int value) => Math.Clamp(value, 0, 13);

    private static string EquipmentTypeGroup(int typeId)
    {
        return typeId switch
        {
            1 or 2 or 3 or 4 or 11 or 12 => "gun",
            5 or 12 => "torpedo",
            6 or 21 => "antiair",
            7 or 8 or 9 or 11 => "aircraft",
            10 or 14 or 20 => "auxiliary",
            15 => "asw",
            17 => "missile",
            _ => "other"
        };
    }

    private static string ShipTypeLabel(int typeId)
    {
        return typeId switch
        {
            1 => "구축",
            2 => "경순",
            3 => "중순",
            4 => "순전",
            5 => "전함",
            6 => "경항모",
            7 => "항모",
            8 => "잠수",
            9 => "항순",
            10 => "항전",
            12 => "공작",
            13 => "모니터",
            17 => "잠항모",
            18 => "초순",
            19 => "운송",
            20 => "미사일 구축(전열)",
            21 => "미사일 구축(주력)",
            22 => "범선(잠수)",
            23 => "범선(전열)",
            24 => "범선(주력)",
            _ => typeId > 0 ? $"함종 {typeId}" : ""
        };
    }

    private static string ShipTypeGroup(int typeId)
    {
        return typeId switch
        {
            1 or 2 or 3 or 18 or 23 => "vanguard",
            4 or 5 or 6 or 7 or 10 or 12 or 13 or 19 or 21 or 24 => "main",
            8 or 17 or 22 => "submarine",
            _ => typeId > 0 ? "other" : ""
        };
    }

    private static string NationLabel(int nationId)
    {
        return nationId switch
        {
            1 => "USS",
            2 => "HMS",
            3 => "IJN",
            4 => "KMS",
            5 => "ROC",
            6 => "RN",
            7 => "SN",
            8 => "FFNF",
            9 or 10 => "MNF",
            96 => "템페스타",
            97 => "META",
            98 => "Bulin",
            100 or 101 or 102 or 103 or 104 => "기타",
            _ => nationId > 0 ? "기타" : ""
        };
    }

    private static string AttrLabel(int attr)
    {
        return attr switch
        {
            1 => "내구",
            2 => "포격",
            3 => "뇌장",
            4 => "대공",
            5 => "항공",
            6 => "장전",
            7 => "대잠",
            8 => "명중",
            9 => "기동",
            10 => "항속",
            11 => "행운",
            12 => "대잠",
            _ => $"attr {attr}"
        };
    }

    private static string MoodLabel(int energy)
    {
        return energy switch
        {
            >= 120 => "최상",
            >= 60 => "양호",
            >= 30 => "주의",
            > 0 => "저하",
            _ => "-"
        };
    }

    private static string BattleSystemLabel(int system)
    {
        return system switch
        {
            1 => "일반 해역",
            2 => "반복 전투",
            3 => "연습전",
            11 => "잠수함",
            51 => "작전/월드",
            _ => system > 0 ? $"System {system}" : ""
        };
    }

    private static string FormatEpoch(int epoch)
    {
        return epoch <= 0
            ? "-"
            : DateTimeOffset.FromUnixTimeSeconds(epoch).LocalDateTime
                .ToString("MM-dd HH:mm", CultureInfo.InvariantCulture);
    }

    private static string RemainingText(int epoch)
    {
        if (epoch <= 0)
        {
            return "-";
        }

        var remaining = epoch - NowEpoch();
        if (remaining <= 0)
        {
            return "완료";
        }

        var time = TimeSpan.FromSeconds(remaining);
        if (time.TotalDays >= 1)
        {
            return $"{(int)time.TotalDays}일 {time.Hours}시간";
        }

        return time.Hours > 0 ? $"{time.Hours}시간 {time.Minutes}분" : $"{time.Minutes}분";
    }

    private static bool IsFinished(int epoch) => epoch > 0 && epoch <= NowEpoch();

    private static string AgeText(int seconds)
    {
        if (seconds < 60)
        {
            return $"{Math.Max(0, seconds)}초";
        }

        if (seconds < 3600)
        {
            return $"{seconds / 60}분";
        }

        return $"{seconds / 3600}시간 {(seconds % 3600) / 60}분";
    }

    private static int? ParseSummaryEpoch(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var parsed)
            ? (int)new DateTimeOffset(parsed).ToUnixTimeSeconds()
            : null;
    }

    private static int NowEpoch() => (int)DateTimeOffset.Now.ToUnixTimeSeconds();
}
