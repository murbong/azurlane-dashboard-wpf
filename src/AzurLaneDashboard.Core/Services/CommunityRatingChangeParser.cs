using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using AzurLaneDashboard.Core.Models;

namespace AzurLaneDashboard.Core.Services;

/// <summary>
/// Reduces the community changelog (free English text) to changes that mention owned ships.
/// Unrecognised sentences fall back to a generic label; the original text stays available.
/// </summary>
public static partial class CommunityRatingChangeParser
{
    private const int MaximumRows = 6;
    private const int WindowDays = 28;

    private static readonly (string Pattern, string Label)[] ContextLabels =
    [
        (@"^(Chapter|W)\s*15/16\s*Mob", "15·16 해역 잡몹"),
        (@"^(Chapter|W)\s*15/16\s*Boss", "15·16 해역 보스"),
        (@"^(Chapter|W)\s*14\s*Mob", "14 해역 잡몹"),
        (@"^(Chapter|W)\s*14\s*Boss", "14 해역 보스"),
        (@"^Hard Arbiter", "하드 아비터"),
        (@"^META", "META 보스전"),
        (@"^(CM|Challenge Mode)", "챌린지 모드"),
        (@"^EX", "EX 랭킹")
    ];

    public static string LatestDate(JsonElement changelog) =>
        Entries(changelog).Select(entry => entry.Date).DefaultIfEmpty("").Max(StringComparer.Ordinal) ?? "";

    public static IReadOnlyList<CommunityRatingChangeRow> Parse(
        JsonElement changelog, IReadOnlyDictionary<string, string> ownedEnglishToKorean)
    {
        var entries = Entries(changelog).ToList();
        if (entries.Count == 0 || ownedEnglishToKorean.Count == 0)
            return [];

        var names = ownedEnglishToKorean.Keys
            .Where(name => name.Length > 1)
            .OrderByDescending(name => name.Length)
            .Select(name => (English: name, Korean: ownedEnglishToKorean[name],
                Pattern: new Regex($@"(?<![\p{{L}}]){Regex.Escape(name)}(?![\p{{L}}])", RegexOptions.CultureInvariant)))
            .ToList();
        var newest = entries.Max(entry => entry.Day);
        var rows = new List<CommunityRatingChangeRow>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in entries.Where(entry => (newest - entry.Day).TotalDays <= WindowDays)
                     .OrderByDescending(entry => entry.Day))
        {
            foreach (var (line, isShipChange) in entry.Lines)
            {
                // Renames say nothing about strength.
                if (line.Contains("Rename", StringComparison.OrdinalIgnoreCase))
                    continue;
                var ships = MatchShips(line, names, isShipChange);
                if (ships.Count == 0)
                    continue;
                var shipText = ships.Count > 3 ? $"{string.Join(" · ", ships.Take(3))} 외 {ships.Count - 3}척" : string.Join(" · ", ships);
                var parts = Describe(line);
                // The changelog sometimes repeats a change on a later date; keep only the newest mention.
                if (!seen.Add(shipText + "|" + string.Join("|", parts.Select(part => $"{part.Label}{part.From}{part.To}"))))
                    continue;
                rows.Add(new CommunityRatingChangeRow
                {
                    DateText = entry.Day.ToString("MM-dd", CultureInfo.InvariantCulture),
                    ShipText = shipText,
                    Parts = parts,
                    OriginalText = line
                });
                if (rows.Count >= MaximumRows)
                    return rows;
            }
        }

        return rows;
    }

    // Ship names that are also fleet terms in site notes ("hull filters default to Main + Vanguard").
    private static readonly HashSet<string> SiteVocabulary = new(StringComparer.Ordinal) { "Vanguard", "Main" };

    private static List<string> MatchShips(string line, IEnumerable<(string English, string Korean, Regex Pattern)> names,
        bool isShipChange)
    {
        var claimed = new List<(int Start, int End)>();
        var found = new List<(int Index, string Korean)>();
        foreach (var name in names)
        {
            if (!isShipChange && SiteVocabulary.Contains(name.English))
                continue;
            foreach (Match match in name.Pattern.Matches(line))
            {
                // Longer names win, so "Admiral Hipper" is not also counted as "Hipper".
                if (claimed.Any(range => match.Index < range.End && match.Index + match.Length > range.Start))
                    continue;
                claimed.Add((match.Index, match.Index + match.Length));
                found.Add((match.Index, name.Korean));
            }
        }

        return found.OrderBy(item => item.Index).Select(item => item.Korean).Distinct().ToList();
    }

    private static IReadOnlyList<CommunityRatingChangePart> Describe(string line)
    {
        var parts = RatingChange().Matches(line)
            .Select(match => new CommunityRatingChangePart
            {
                Label = ContextLabel(match.Groups["context"].Value),
                From = match.Groups["from"].Value,
                To = match.Groups["to"].Value
            })
            .Where(part => part.Label.Length > 0)
            .ToList();
        if (parts.Count > 0)
            return parts;

        var label = line switch
        {
            _ when line.Contains("unique augment", StringComparison.OrdinalIgnoreCase) => "고유 증강 평가 추가",
            _ when line.Contains("Self Survival", StringComparison.OrdinalIgnoreCase) => "생존력 수치 조정",
            _ when line.StartsWith("Add ", StringComparison.OrdinalIgnoreCase) => "평가 추가",
            _ when line.Contains("gun", StringComparison.OrdinalIgnoreCase) ||
                   line.Contains("gear", StringComparison.OrdinalIgnoreCase) => "장비 평가 조정",
            _ => "평가 내용 변경"
        };
        return [new CommunityRatingChangePart { Label = label }];
    }

    private static string ContextLabel(string context)
    {
        foreach (var (pattern, label) in ContextLabels)
        {
            if (Regex.IsMatch(context, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                return label;
        }

        return "";
    }

    private static IEnumerable<(string Date, DateTime Day, IReadOnlyList<(string Text, bool IsShipChange)> Lines)> Entries(
        JsonElement changelog)
    {
        if (changelog.ValueKind != JsonValueKind.Object ||
            !changelog.TryGetProperty("dates", out var dates) || dates.ValueKind != JsonValueKind.Array)
            yield break;

        foreach (var entry in dates.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object ||
                !entry.TryGetProperty("date", out var dateValue) || dateValue.ValueKind != JsonValueKind.String ||
                !DateTime.TryParseExact(dateValue.GetString(), "yyyy-MM-dd", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var day))
                continue;

            var lines = new List<(string Text, bool IsShipChange)>();
            foreach (var key in new[] { "ship_changes", "changes" })
            {
                if (entry.TryGetProperty(key, out var values) && values.ValueKind == JsonValueKind.Array)
                    lines.AddRange(values.EnumerateArray()
                        .Where(value => value.ValueKind == JsonValueKind.String)
                        .Select(value => value.GetString() ?? "")
                        .Where(value => value.Length > 0)
                        .Select(value => (value, key == "ship_changes")));
            }

            yield return (dateValue.GetString()!, day, lines);
        }
    }

    [GeneratedRegex(
        @"(?<context>(?:Chapter|W)\s*1[456](?:/16)?\s*(?:Mob|Boss)|Hard Arbiter|META|Challenge Mode|CM|EX)\s+(?<from>EX|SS|S\+|S|A\+|A|B|C|D)\*?\s*(?:→|->)\s*(?<to>EX|SS|S\+|S|A\+|A|B|C|D)\*?",
        RegexOptions.CultureInvariant)]
    private static partial Regex RatingChange();
}
