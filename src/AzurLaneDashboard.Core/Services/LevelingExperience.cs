namespace AzurLaneDashboard.Core.Services;

/// <summary>
/// Battle EXP per ship as the server grants it. The client only receives the result, so the steps
/// were matched against captured battles: D3 elite (Lua exp 702) gave 842 at S rank, 1,010 with
/// morale above 120, 1,515 as flagship and 2,020 as MVP. Every step rounds down.
/// </summary>
public static class LevelingExperience
{
    public const int MoraleBonusAbove = 120;
    public const int MoralePerBattle = 2;

    /// <summary>S rank pays 1.2x the stage's Lua exp; A rank pays the base (namu wiki: A ≈ 0.83 of S).</summary>
    public static int SRank(int luaExp) => Math.Max(0, luaExp) * 6 / 5;

    public static int PerBattle(int sRankExp, bool moraleBonus, bool flagship = false, bool mvp = false)
    {
        var exp = Math.Max(0, sRankExp);
        if (moraleBonus) exp = exp * 6 / 5;
        if (flagship) exp = exp * 3 / 2;
        if (mvp) exp *= 2;
        return exp;
    }

    /// <summary>Battles still fought above 120 morale, assuming morale is judged before each battle.</summary>
    public static int BonusBattles(int morale) =>
        morale <= MoraleBonusAbove ? 0 : (morale - MoraleBonusAbove + MoralePerBattle - 1) / MoralePerBattle;

    public static int BattlesUntilExhausted(int morale) => Math.Max(0, morale) / MoralePerBattle;

    /// <summary>Battles to earn <paramref name="experience"/>: bonus battles first, then the plain rate.</summary>
    public static int? BattlesFor(long experience, int sRankExp, int morale, bool flagship = false)
    {
        if (experience <= 0) return 0;
        var high = PerBattle(sRankExp, moraleBonus: true, flagship);
        var low = PerBattle(sRankExp, moraleBonus: false, flagship);
        if (low <= 0) return null;

        var bonus = BonusBattles(morale);
        var bonusTotal = (long)high * bonus;
        if (experience <= bonusTotal) return (int)((experience + high - 1) / high);
        var rest = experience - bonusTotal;
        return (int)Math.Min(int.MaxValue, bonus + (rest + low - 1) / low);
    }
}
