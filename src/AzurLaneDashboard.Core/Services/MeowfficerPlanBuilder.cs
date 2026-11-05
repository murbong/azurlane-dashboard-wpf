using System.Globalization;
using AzurLaneDashboard.Core.Models;

namespace AzurLaneDashboard.Core.Services;

/// <summary>
/// Rates owned Meowfficers the way the sources do. No reputable per-cat tier list exists, so a cat is judged by
/// the fleet type and slot its source places it in, the talent points it holds that the source wants for that
/// fleet, and (before it is raised) the source's starting-talent rubric. Cats the source does not mention get no
/// rating rather than a low one, and Tempesta cats, whose talents are fixed, keep only the source's verdict.
/// </summary>
public static class MeowfficerPlanBuilder
{
    private const string AnyFleet = "ANY";
    private static readonly HashSet<string> BattleshipHulls = new(StringComparer.Ordinal) { "BB", "BC", "BBV" };
    private static readonly HashSet<string> CarrierHulls = new(StringComparer.Ordinal) { "CV", "CVL" };
    private static readonly HashSet<string> SubmarineHulls = new(StringComparer.Ordinal) { "SS", "SSV" };

    public static MeowfficerDashboard Build(
        MeowfficerCatalog catalog,
        IReadOnlyList<OwnedMeowfficer> owned,
        IReadOnlyList<MeowfficerFleetInput> fleets,
        int farmFleetId = 0)
    {
        if (owned.Count == 0) return MeowfficerDashboard.Empty;
        var guide = catalog.Guide.Verified ? catalog.Guide : new MeowfficerGuide();
        var assignments = fleets
            .SelectMany(fleet => new[] { (fleet.FleetId, Slot: "지휘", Uid: fleet.CommandUid), (fleet.FleetId, Slot: "참모", Uid: fleet.StaffUid) })
            .Where(entry => entry.Uid.Length > 0)
            .GroupBy(entry => entry.Uid, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Select(entry => $"{entry.FleetId}함대 {entry.Slot}").ToList(),
                StringComparer.Ordinal);
        var copies = owned.Select(cat => Copy(catalog, guide, cat, assignments.GetValueOrDefault(cat.Uid) ?? []))
            .ToList();

        var kinds = copies.GroupBy(copy => copy.TemplateId)
            .Select(group =>
            {
                var first = group.First();
                var role = guide.Roles.GetValueOrDefault(group.Key);
                return (Order: role?.Stance switch { "top" => 0, "ok" => 1, "weak" => 2, _ => 3 }, Row: new MeowfficerKindRow
                {
                    TemplateId = group.Key,
                    Name = first.Name,
                    Rarity = first.Rarity,
                    HasRole = role is not null,
                    RoleText = role is null ? "출처 평가 없음" : $"{first.FleetLabel} {role.Slot} · {role.Summary}",
                    Copies = group.OrderByDescending(copy => copy.Level).ThenByDescending(copy => copy.Score ?? -1)
                        .ThenBy(copy => copy.Uid, StringComparer.Ordinal).ToList()
                });
            })
            .OrderBy(kind => kind.Order).ThenBy(kind => kind.Row.TemplateId)
            .Select(kind => kind.Row)
            .ToList();

        return new MeowfficerDashboard
        {
            Kinds = kinds,
            Fleets = fleets.Where(fleet => fleet.Hulls.Count > 0)
                .Select(fleet => FleetRow(catalog, guide, fleet, copies, fleet.FleetId == farmFleetId))
                .ToList(),
            Sources = guide.Sources,
            UsesGuide = guide.Verified
        };
    }

    /// <summary>The fleet type a sortie fleet fights as, from its ships' hulls.</summary>
    public static string FleetType(int fleetId, IReadOnlyList<string> hulls)
    {
        if (fleetId >= 11 || (hulls.Count > 0 && hulls.All(SubmarineHulls.Contains))) return "SS";
        var battleships = hulls.Count(BattleshipHulls.Contains);
        var carriers = hulls.Count(CarrierHulls.Contains);
        if (battleships > 0 && battleships >= carriers) return "BB";
        if (carriers > 0) return "CV";
        return hulls.Count(hull => hull == "DD") >= 2 ? "DD_TORP" : "CL_GUN";
    }

    private static MeowfficerCopyRow Copy(MeowfficerCatalog catalog, MeowfficerGuide guide, OwnedMeowfficer cat,
        IReadOnlyList<string> assignments)
    {
        var kind = catalog.Kinds.GetValueOrDefault(cat.TemplateId) ??
                   new MeowfficerKind(cat.TemplateId, cat.TemplateId.ToString(CultureInfo.InvariantCulture), 0, 0, 30, false);
        var role = guide.Roles.GetValueOrDefault(cat.TemplateId);
        var fleet = role is null ? null : guide.FleetTypes.GetValueOrDefault(role.Fleet);
        var wanted = fleet is null ? [] : fleet.Core.Concat(fleet.Filler).ToHashSet();
        var talents = cat.Talents.Select(id => catalog.Talents.GetValueOrDefault(id)).OfType<MeowfficerTalent>().ToList();
        var origin = cat.OriginTalents.Select(id => catalog.Talents.GetValueOrDefault(id)).OfType<MeowfficerTalent>().ToList();
        var skill = catalog.Skills.GetValueOrDefault(cat.SkillId);
        var (next, replace) = kind.FixedTalents || fleet is null ? ("", "") : NextTalent(catalog, fleet, talents);
        return new MeowfficerCopyRow
        {
            Uid = cat.Uid,
            TemplateId = cat.TemplateId,
            Name = kind.Name,
            Nickname = cat.Nickname,
            Rarity = kind.RarityText,
            Level = cat.Level,
            MaxLevel = kind.MaxLevel,
            Locked = cat.Locked,
            Fixed = kind.FixedTalents,
            HasRole = role is not null,
            FleetLabel = fleet?.Label ?? "",
            Slot = role?.Slot ?? "",
            RoleSummary = role?.Summary ?? "",
            Disputed = role?.Disputed ?? "",
            SkillText = skill is null ? "" : $"{skill.Name} Lv.{skill.Level}",
            Talents = talents.Select(talent => new MeowfficerTalentChip
            {
                Name = talent.Name,
                Group = talent.Group,
                Worth = talent.Worth,
                Description = talent.Description,
                Kind = kind.FixedTalents ? "fixed"
                    : fleet is null ? "off"
                    : fleet.Core.Contains(talent.Group) ? "core"
                    : fleet.Filler.Contains(talent.Group) ? "filler"
                    : "off"
            }).ToList(),
            Score = kind.FixedTalents || fleet is null ? null : talents.Where(talent => wanted.Contains(talent.Group)).Sum(talent => talent.Worth),
            Cap = kind.TalentCap,
            Verdict = Verdict(guide, kind, role, fleet, origin),
            NextTalent = next,
            ReplaceTalent = replace,
            Assignments = assignments
        };
    }

    // The source's rubric on the talents the cat was born with: Usable, Good (+ a special talent), God (all four points).
    private static MeowfficerVerdict Verdict(MeowfficerGuide guide, MeowfficerKind kind, MeowfficerRole? role,
        MeowfficerFleetType? fleet, IReadOnlyList<MeowfficerTalent> origin)
    {
        // A Tempesta cat the source never mentions has no verdict to keep either.
        if (role is null || fleet is null) return MeowfficerVerdict.NoSource;
        if (kind.FixedTalents) return MeowfficerVerdict.Fixed;
        var wanted = fleet.Core.Concat(fleet.Filler).ToHashSet();
        var points = origin.Where(talent => wanted.Contains(talent.Group)).Sum(talent => talent.Worth);
        var special = origin.Any(talent => fleet.Special.Contains(talent.Group) || talent.Group == guide.FlameGroup);
        if (special && points >= guide.GodPoints) return MeowfficerVerdict.God;
        if (special && points >= guide.GoodPoints) return MeowfficerVerdict.Good;
        return points >= guide.UsablePoints ? MeowfficerVerdict.Usable : MeowfficerVerdict.Feed;
    }

    // The highest-priority talent line the cat lacks or holds below its top rank, and the unwanted talent it would push
    // out once all five talent slots are taken.
    private static (string Next, string Replace) NextTalent(MeowfficerCatalog catalog, MeowfficerFleetType fleet,
        IReadOnlyList<MeowfficerTalent> talents)
    {
        foreach (var group in fleet.Core.Concat(fleet.Filler))
        {
            var top = catalog.Talents.Values.Where(talent => talent.Group == group).MaxBy(talent => talent.Worth);
            if (top is null) continue;
            var held = talents.FirstOrDefault(talent => talent.Group == group);
            if (held is not null && held.Worth >= top.Worth) continue;
            if (held is not null) return ($"{top.Name}까지 올리기", "");
            var replace = talents.Count >= 5
                ? talents.Where(talent => !fleet.Core.Contains(talent.Group) && !fleet.Filler.Contains(talent.Group))
                    .MinBy(talent => talent.Worth)?.Name ?? ""
                : "";
            return ($"{top.Name} (훈련 목록에 뜨면)", replace);
        }

        return ("", "");
    }

    private static MeowfficerFleetRow FleetRow(MeowfficerCatalog catalog, MeowfficerGuide guide, MeowfficerFleetInput fleet,
        IReadOnlyList<MeowfficerCopyRow> copies, bool isFarmFleet)
    {
        var type = FleetType(fleet.FleetId, fleet.Hulls);
        var label = guide.FleetTypes.GetValueOrDefault(type)?.Label ?? type;
        var command = copies.FirstOrDefault(copy => copy.Uid == fleet.CommandUid);
        var staff = copies.FirstOrDefault(copy => copy.Uid == fleet.StaffUid);
        var seated = new[] { command, staff }.OfType<MeowfficerCopyRow>().ToList();
        return new MeowfficerFleetRow
        {
            FleetId = fleet.FleetId,
            TypeLabel = label,
            Purpose = isFarmFleet ? "쫄작" : "",
            HullText = string.Join(" · ", fleet.Hulls),
            Slots =
            [
                SlotRow(catalog, guide, type, "지휘", command, staff, copies, seated),
                SlotRow(catalog, guide, type, "참모", staff, command, copies, seated)
            ]
        };
    }

    private static MeowfficerSlotRow SlotRow(MeowfficerCatalog catalog, MeowfficerGuide guide, string type, string slot,
        MeowfficerCopyRow? current, MeowfficerCopyRow? other, IReadOnlyList<MeowfficerCopyRow> copies,
        IReadOnlyList<MeowfficerCopyRow> seated)
    {
        var role = current is null ? null : guide.Roles.GetValueOrDefault(current.TemplateId);
        var (status, kind) = current is null ? ("비어 있음", "warn")
            : role is null ? ("출처 평가 없음", "muted")
            : role.Fleet != type && role.Fleet != AnyFleet ? ($"{current.FleetLabel} 함대용", "warn")
            : role.Slot != slot ? ($"출처는 {role.Slot} 칸 추천", "info")
            : ("출처 역할과 맞음", "good");
        // The fleet's other cat may be the one the source wants here: then the answer is a swap, not another cat.
        var otherRole = other is null ? null : guide.Roles.GetValueOrDefault(other.TemplateId);
        var swap = kind != "good" && other is not null && otherRole is not null && otherRole.Slot == slot &&
                   (otherRole.Fleet == type || otherRole.Fleet == AnyFleet)
            ? $"{other.Name}{Particle(other.Name, "을", "를")} {slot} 칸으로 옮기면 출처 역할과 맞음"
            : "";
        var note = guide.FleetNotes.GetValueOrDefault($"{type}.{slot}") ?? "";
        return new MeowfficerSlotRow
        {
            Slot = slot,
            Current = current,
            Status = status,
            StatusKind = kind,
            Note = string.Join(" · ", new[] { swap, note }.Where(part => part.Length > 0)),
            Candidates = Candidates(catalog, guide, type, slot, copies, seated)
        };
    }

    // 을/를 after the name's last syllable: 받침 picks the first form.
    private static string Particle(string word, string withFinal, string withoutFinal)
    {
        var last = word.TrimEnd().LastOrDefault();
        return last is >= '가' and <= '힣' && (last - '가') % 28 != 0 ? withFinal : withoutFinal;
    }

    // Owned cats the source places in this fleet type and slot, one per kind, most raised first. A carrier fleet's
    // 지휘 slot has no skill cat, so there the cats are ranked on carrier talents alone, as the source advises.
    private static IReadOnlyList<MeowfficerCopyRow> Candidates(MeowfficerCatalog catalog, MeowfficerGuide guide, string type,
        string slot, IReadOnlyList<MeowfficerCopyRow> copies, IReadOnlyList<MeowfficerCopyRow> seated)
    {
        // A copy no better than the same kind already seated here would only be a weaker stand-in (the swap note
        // covers that case); a better-trained copy stays, as an upgrade.
        var inFleet = seated.Select(copy => copy.Uid).ToHashSet(StringComparer.Ordinal);
        bool Weaker(MeowfficerCopyRow copy) => seated.Any(other => other.TemplateId == copy.TemplateId &&
                                                                    copy.Level <= other.Level &&
                                                                    (copy.Score ?? -1) <= (other.Score ?? -1));
        var pool = copies.Where(copy => !inFleet.Contains(copy.Uid) && !Weaker(copy) &&
                                        guide.Roles.GetValueOrDefault(copy.TemplateId) is { } role &&
                                        role.Fleet == type && role.Slot == slot && role.Stance != "weak")
            .OrderByDescending(copy => guide.Roles[copy.TemplateId].Stance == "top")
            .ThenByDescending(copy => copy.Level)
            .ThenByDescending(copy => copy.Score ?? 0)
            .ToList();
        if (pool.Count == 0 && guide.FleetTypes.GetValueOrDefault(type) is { } fleet &&
            guide.FleetNotes.ContainsKey($"{type}.{slot}"))
        {
            var wanted = fleet.Core.Concat(fleet.Filler).ToHashSet();
            int Points(MeowfficerCopyRow copy) =>
                copy.Talents.Where(chip => wanted.Contains(chip.Group)).Sum(chip => chip.Worth);
            pool = copies.Where(copy => !inFleet.Contains(copy.Uid) && !Weaker(copy) && !copy.Fixed &&
                                        copy.IsMaxLevel && Points(copy) > 0)
                .OrderByDescending(Points).ThenByDescending(copy => copy.Level)
                .Select(copy => With(copy, Points(copy), fleet.Label))
                .ToList();
        }

        return pool.GroupBy(copy => copy.TemplateId).Select(group => group.First()).Take(2).ToList();
    }

    // A copy re-scored on another fleet's talents, for the carrier 지휘 slot.
    private static MeowfficerCopyRow With(MeowfficerCopyRow copy, int score, string fleetLabel) => new()
    {
        Uid = copy.Uid, TemplateId = copy.TemplateId, Name = copy.Name, Nickname = copy.Nickname, Rarity = copy.Rarity,
        Level = copy.Level, MaxLevel = copy.MaxLevel, Locked = copy.Locked, Fixed = copy.Fixed, HasRole = copy.HasRole,
        FleetLabel = copy.FleetLabel, Slot = copy.Slot, RoleSummary = $"{fleetLabel} 재능 {score}포인트",
        Disputed = copy.Disputed, SkillText = copy.SkillText, Talents = copy.Talents, Score = score, Cap = copy.Cap,
        Verdict = copy.Verdict, NextTalent = copy.NextTalent, ReplaceTalent = copy.ReplaceTalent, Assignments = copy.Assignments
    };
}
