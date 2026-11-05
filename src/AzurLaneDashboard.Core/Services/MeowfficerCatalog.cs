using System.Globalization;
using System.Text.Json;
using AzurLaneDashboard.Core.Models;

namespace AzurLaneDashboard.Core.Services;

/// <summary>Reads maps/commanders.json (game data) and maps/meowfficer_guide.json (hand-curated ratings).</summary>
public sealed class MeowfficerCatalog
{
    public static MeowfficerCatalog Empty { get; } = new();

    public IReadOnlyDictionary<int, MeowfficerKind> Kinds { get; init; } = new Dictionary<int, MeowfficerKind>();
    public IReadOnlyDictionary<int, MeowfficerTalent> Talents { get; init; } = new Dictionary<int, MeowfficerTalent>();
    public IReadOnlyDictionary<int, MeowfficerSkill> Skills { get; init; } = new Dictionary<int, MeowfficerSkill>();
    public MeowfficerGuide Guide { get; init; } = new();

    public static MeowfficerCatalog Parse(JsonElement commanders, JsonElement guide) => new()
    {
        Kinds = Rows(commanders, "commanders").Select(row => new MeowfficerKind(Int(row, "id"), Text(row, "name"),
                Int(row, "rarity"), Int(row, "skill_id"), Int(row, "max_level"), Int(row, "refresh_type") == 1))
            .Where(kind => kind.Id > 0).ToDictionary(kind => kind.Id),
        Talents = Rows(commanders, "talents").Select(row => new MeowfficerTalent(Int(row, "id"), Text(row, "name"),
                Int(row, "group"), Int(row, "worth"), Text(row, "desc")))
            .Where(talent => talent.Id > 0).ToDictionary(talent => talent.Id),
        Skills = Rows(commanders, "skills").Select(row => new MeowfficerSkill(Int(row, "id"), Text(row, "name"),
                Int(row, "level"), Text(row, "desc")))
            .Where(skill => skill.Id > 0).ToDictionary(skill => skill.Id),
        Guide = ParseGuide(guide)
    };

    private static MeowfficerGuide ParseGuide(JsonElement map)
    {
        if (map.ValueKind != JsonValueKind.Object) return new MeowfficerGuide();
        var rubric = Child(map, "rubric");
        return new MeowfficerGuide
        {
            Verified = map.TryGetProperty("verified", out var verified) && verified.ValueKind == JsonValueKind.True,
            LastReviewed = Text(map, "lastReviewed"),
            Sources = Array(map, "sources").Select(row => new MeowfficerSource(Text(row, "title"), Text(row, "url"),
                Text(row, "author"), Text(row, "revision"), Text(row, "license"), Text(row, "role"))).ToList(),
            UsablePoints = Positive(Int(rubric, "usable"), 2),
            GoodPoints = Positive(Int(rubric, "good"), 3),
            GodPoints = Positive(Int(rubric, "god"), 4),
            FlameGroup = Int(map, "flameGroup"),
            FleetTypes = Child(map, "fleetTypes") is { ValueKind: JsonValueKind.Object } types
                ? types.EnumerateObject().ToDictionary(type => type.Name, type => new MeowfficerFleetType(type.Name,
                    Text(type.Value, "label"), Ints(type.Value, "core"), Ints(type.Value, "filler"), Ints(type.Value, "special")))
                : new Dictionary<string, MeowfficerFleetType>(),
            Roles = Child(map, "roles") is { ValueKind: JsonValueKind.Object } roles
                ? roles.EnumerateObject()
                    .Where(role => int.TryParse(role.Name, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
                    .ToDictionary(role => int.Parse(role.Name, CultureInfo.InvariantCulture), role => new MeowfficerRole(
                        Text(role.Value, "fleet"), Text(role.Value, "slot"), Text(role.Value, "stance"),
                        Text(role.Value, "summary"), Text(role.Value, "disputed")))
                : new Dictionary<int, MeowfficerRole>(),
            FleetNotes = Child(map, "fleetNotes") is { ValueKind: JsonValueKind.Object } notes
                ? notes.EnumerateObject().ToDictionary(note => note.Name, note => note.Value.GetString() ?? "")
                : new Dictionary<string, string>()
        };
    }

    private static IEnumerable<JsonElement> Rows(JsonElement map, string key) =>
        Child(map, key) is { ValueKind: JsonValueKind.Object } table
            ? table.EnumerateObject().Select(property => property.Value).Where(row => row.ValueKind == JsonValueKind.Object)
            : [];

    private static JsonElement Child(JsonElement row, string key) =>
        row.ValueKind == JsonValueKind.Object && row.TryGetProperty(key, out var value) ? value : default;

    private static IEnumerable<JsonElement> Array(JsonElement row, string key) =>
        Child(row, key) is { ValueKind: JsonValueKind.Array } array ? array.EnumerateArray() : [];

    private static IReadOnlyList<int> Ints(JsonElement row, string key) =>
        Array(row, key).Where(item => item.ValueKind == JsonValueKind.Number).Select(item => item.GetInt32()).ToList();

    private static string Text(JsonElement row, string key) =>
        Child(row, key) is { ValueKind: JsonValueKind.String } value ? value.GetString() ?? "" : "";

    private static int Int(JsonElement row, string key) =>
        Child(row, key) is { ValueKind: JsonValueKind.Number } value && value.TryGetInt32(out var number) ? number : 0;

    private static int Positive(int value, int fallback) => value > 0 ? value : fallback;
}
