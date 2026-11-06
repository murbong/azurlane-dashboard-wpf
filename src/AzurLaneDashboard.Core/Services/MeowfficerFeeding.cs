using AzurLaneDashboard.Core.Models;

namespace AzurLaneDashboard.Core.Services;

/// <summary>
/// Cat EXP since the last login list as the game client applies it. Feeding (CS 25008 / SC 25009) answers with a
/// result only, so UpgradeCommanderCommand's arithmetic is replayed; a battle's EXP (SC 40004) goes through the
/// same Commander.addExp. The next login list carries the true levels.
/// </summary>
public static class MeowfficerFeeding
{
    public static IReadOnlyList<OwnedMeowfficer> Apply(
        MeowfficerCatalog catalog, IReadOnlyList<OwnedMeowfficer> cats, IReadOnlyList<MeowfficerFeed> feeds)
    {
        if (feeds.Count == 0) return cats;
        var byUid = cats.ToDictionary(cat => cat.Uid, StringComparer.Ordinal);
        foreach (var feed in feeds)
        {
            if (!byUid.TryGetValue(feed.TargetUid, out var target)) continue;
            var kind = catalog.Kinds.GetValueOrDefault(target.TemplateId);
            var group = kind?.Group ?? 0;
            var exp = feed.Exp + (int)Math.Floor(feed.Materials.Sum(material => GivenExp(catalog, material, group)));
            // Battle EXP leaves the skill alone (FinishStageCommand calls only Commander.addExp).
            var isFeed = feed.Materials.Count > 0;
            var skillExp = !isFeed ? 0 : feed.Materials.Count(material => catalog.Kinds.GetValueOrDefault(material.TemplateId)?.Group == group && group > 0)
                           * catalog.Growth.SkillExp;
            byUid[target.Uid] = Grow(catalog, target, kind, exp, skillExp, isFeed);
        }

        return cats.Select(cat => byUid[cat.Uid]).ToList();
    }

    /// <summary>
    /// Commander.getDestoryedExp: the eaten cat's base EXP plus a share (commander_exp_a) of every EXP it had
    /// earned, ×commander_exp_same_rate for the same kind, then its talents' destroy-EXP ratio and value.
    /// </summary>
    public static double GivenExp(MeowfficerCatalog catalog, OwnedMeowfficer material, int targetGroup)
    {
        var kind = catalog.Kinds.GetValueOrDefault(material.TemplateId);
        if (kind is null) return 0;
        var growth = catalog.Growth;
        long earned = material.Exp;
        for (var level = 1; level < material.Level; level++) earned += growth.LevelExp(level, kind.Rarity);
        var talents = material.Talents.Select(id => catalog.Talents.GetValueOrDefault(id)).OfType<MeowfficerTalent>().ToList();
        var ratio = talents.Sum(talent => talent.DestroyRatio) / 10000.0;
        var value = talents.Sum(talent => talent.DestroyExp);
        var same = targetGroup > 0 && kind.Group == targetGroup ? growth.SameKindRate / 10000.0 : 1;
        return (kind.Exp + earned * growth.ExpShare / 10000.0) * same * (1 + ratio) + value;
    }

    // Commander.addExp and CommanderSkill.addExp: level up while the EXP covers the next level.
    private static OwnedMeowfficer Grow(MeowfficerCatalog catalog, OwnedMeowfficer cat, MeowfficerKind? kind, int exp, int skillExp,
        bool feedsSkill)
    {
        var growth = catalog.Growth;
        var maxLevel = kind?.MaxLevel > 0 ? kind.MaxLevel : growth.MaxLevel;
        var level = cat.Level;
        var catExp = cat.Exp;
        if (level < maxLevel)
        {
            catExp += exp;
            while (level < maxLevel && growth.LevelExp(level, kind?.Rarity ?? 0) is var need and > 0 && catExp >= need)
            {
                catExp -= need;
                level++;
            }
        }

        var skillId = cat.SkillId;
        var skillLeft = cat.SkillExp + skillExp;
        while (feedsSkill && catalog.Skills.GetValueOrDefault(skillId) is { Next: > 0 } skill && skillLeft >= skill.Exp)
        {
            skillLeft -= skill.Exp;
            skillId = skill.Next;
        }

        return cat with { Level = level, Exp = catExp, SkillId = skillId, SkillExp = skillLeft };
    }
}
