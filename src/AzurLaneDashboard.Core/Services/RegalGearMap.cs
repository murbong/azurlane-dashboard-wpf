using System.Globalization;
using System.Text.Json;

namespace AzurLaneDashboard.Core.Services;

/// <summary>
/// Regal's normalized gear ranking (maps/regal_gear_map.json):
/// ship[gear][usecase] → usecase[id] = five slot ids → slot[id] = ranked row ids → row[id] = equip keys
/// → equip[key] = {id: base equipment id} (or a list of such entries).
/// </summary>
public sealed class RegalGearMap(JsonElement root)
{
    private static readonly IReadOnlyList<IReadOnlyList<IReadOnlyList<int>>> NoSlots = [];

    public IReadOnlyList<string> Usecases(int gearId) =>
        Table("ship", gearId) is { ValueKind: JsonValueKind.Object } usecases
            ? usecases.EnumerateObject().Select(property => property.Name).ToList()
            : [];

    /// <summary>
    /// The usecase that fits <paramref name="wanted"/>: the exact key, a combined key such as "L/M/H",
    /// the flagship or side key for main-fleet layouts, then the generic chapter key.
    /// </summary>
    public string ResolveUsecase(int gearId, string wanted, bool isFlagship)
    {
        var keys = Usecases(gearId);
        if (keys.Count == 0) return "";
        static string[] Parts(string key) => key.Split('/', StringSplitOptions.TrimEntries);
        return keys.FirstOrDefault(key => key == wanted)
               ?? keys.FirstOrDefault(key => Parts(key).Contains(wanted))
               ?? keys.FirstOrDefault(key => Parts(key).Contains(isFlagship ? "Flag" : "Top"))
               ?? keys.FirstOrDefault(key => Parts(key).Contains(isFlagship ? "Flag" : "Bottom"))
               ?? keys.FirstOrDefault(key => key == "C")
               ?? keys.FirstOrDefault(key => key.StartsWith("C", StringComparison.Ordinal))
               ?? keys[0];
    }

    /// <summary>Per slot, the ranked rows; each row lists equal-ranked base equipment ids. An id keeps its best rank.</summary>
    public IReadOnlyList<IReadOnlyList<IReadOnlyList<int>>> Slots(int gearId, string usecase)
    {
        if (Table("ship", gearId) is not { ValueKind: JsonValueKind.Object } usecases ||
            !usecases.TryGetProperty(usecase, out var usecaseId) || usecaseId.ValueKind != JsonValueKind.Number ||
            Table("usecase", usecaseId.GetInt32()) is not { ValueKind: JsonValueKind.Array } slotIds)
            return NoSlots;

        var slots = new List<IReadOnlyList<IReadOnlyList<int>>>();
        foreach (var slotId in slotIds.EnumerateArray())
        {
            var ranks = new List<IReadOnlyList<int>>();
            var seen = new HashSet<int>();
            if (slotId.ValueKind == JsonValueKind.Number &&
                Table("slot", slotId.GetInt32()) is { ValueKind: JsonValueKind.Array } rowIds)
            {
                foreach (var rowId in rowIds.EnumerateArray())
                {
                    var rank = Row(rowId).Where(seen.Add).ToList();
                    if (rank.Count > 0) ranks.Add(rank);
                }
            }

            slots.Add(ranks);
        }

        return slots;
    }

    private IEnumerable<int> Row(JsonElement rowId)
    {
        if (rowId.ValueKind != JsonValueKind.Number ||
            Table("row", rowId.GetInt32()) is not { ValueKind: JsonValueKind.Array } keys)
            yield break;

        foreach (var key in keys.EnumerateArray())
        {
            if (key.ValueKind != JsonValueKind.Number) continue;
            var equip = Table("equip", key.GetInt32());
            var entries = equip.ValueKind == JsonValueKind.Array ? equip.EnumerateArray().ToList() : [equip];
            foreach (var entry in entries)
            {
                if (entry.ValueKind == JsonValueKind.Object && entry.TryGetProperty("id", out var id) &&
                    id.ValueKind == JsonValueKind.Number && id.TryGetInt32(out var value) && value > 0)
                    yield return value;
            }
        }
    }

    private JsonElement Table(string name, int id) =>
        root.ValueKind == JsonValueKind.Object && root.TryGetProperty(name, out var table) &&
        table.ValueKind == JsonValueKind.Object &&
        table.TryGetProperty(id.ToString(CultureInfo.InvariantCulture), out var value)
            ? value
            : default;
}
