using System.Globalization;

namespace AzurLaneDashboard.Resources;

internal sealed partial class NativeLuaMapBuilder
{
    private const int DropTypeItem = 2;
    private const int DropTypeWorldItem = 12;

    /// <summary>
    /// Operation Siren (대형작전) sea-area targets: world_chapter_colormask gives each area its normal and hidden
    /// (cryptic) target ids and the reward at each star count; world_target_data the target text and its
    /// conditions (trigger id, count); world_regions_data the region names. Reward item names come along so the
    /// page needs no other table.
    /// </summary>
    private static Dictionary<string, object?> BuildWorldTargetMap(string luaDirectory)
    {
        var sharecfg = Path.Combine(luaDirectory, "sharecfg");
        var sharecfgdata = Path.Combine(luaDirectory, "sharecfgdata");
        string Key(int id) => id.ToString(CultureInfo.InvariantCulture);

        var regions = LoadLuaRecords(Path.Combine(sharecfg, "world_regions_data.lua"), "world_regions_data", ["id", "name"])
            .ToDictionary(row => SafeInt(Get(row, "id")), row => PlainText(Get(row, "name")));

        var rewardKeys = new HashSet<(int Type, int Id)>();
        var areas = new SortedDictionary<int, Dictionary<string, object?>>();
        foreach (var row in LoadLuaRecords(Path.Combine(sharecfg, "world_chapter_colormask.lua"), "world_chapter_colormask",
                     ["id", "serial_number", "regions", "world", "chapter"],
                     ["normal_target", "cryptic_target", "target_drop_show"]))
        {
            var id = SafeInt(Get(row, "id"));
            var normal = IntList(row, "normal_target");
            var cryptic = IntList(row, "cryptic_target");
            if (id <= 0 || normal.Count + cryptic.Count == 0) continue;

            // target_drop_show is {star, {drop type, id, count}} per entry; flattened that is groups of four.
            var flat = IntList(row, "target_drop_show");
            var awards = new List<object?>();
            for (var index = 0; index + 3 < flat.Count; index += 4)
            {
                rewardKeys.Add((flat[index + 1], flat[index + 2]));
                awards.Add(new Dictionary<string, object?>
                {
                    ["star"] = flat[index], ["type"] = flat[index + 1], ["id"] = flat[index + 2], ["count"] = flat[index + 3]
                });
            }

            var region = SafeInt(Get(row, "regions"));
            areas[id] = new Dictionary<string, object?>
            {
                ["id"] = id,
                ["serial"] = PlainText(Get(row, "serial_number")),
                ["region"] = regions.GetValueOrDefault(region, ""),
                ["normal"] = normal,
                ["cryptic"] = cryptic,
                ["awards"] = awards
            };
        }

        var targets = new SortedDictionary<int, Dictionary<string, object?>>();
        foreach (var row in LoadLuaRecords(Path.Combine(sharecfg, "world_target_data.lua"), "world_target_data",
                     ["id", "target_name", "target_desc"], ["condition"]))
        {
            var id = SafeInt(Get(row, "id"));
            if (id <= 0) continue;
            var flat = IntList(row, "condition");
            var conditions = new List<object?>();
            for (var index = 0; index + 1 < flat.Count; index += 2)
                conditions.Add(new Dictionary<string, object?> { ["trigger"] = flat[index], ["count"] = flat[index + 1] });
            targets[id] = new Dictionary<string, object?>
            {
                ["id"] = id,
                ["name"] = PlainText(Get(row, "target_name")),
                ["desc"] = PlainText(Get(row, "target_desc")),
                ["conditions"] = conditions
            };
        }

        // Reward names: ordinary items, then virtual items (supplies, random parts), then Operation Siren items.
        // World item 110, the Siren energy container that runs the Siren devices, is always listed.
        var itemNames = NamesById(Path.Combine(sharecfgdata, "item_data_statistics.lua"), "item_data_statistics");
        var virtualNames = NamesById(Path.Combine(sharecfgdata, "item_virtual_data_statistics.lua"), "item_virtual_data_statistics");
        var worldNames = NamesById(Path.Combine(sharecfg, "world_item_data_template.lua"), "world_item_data_template");
        rewardKeys.Add((DropTypeWorldItem, 110));
        var items = new SortedDictionary<string, object?>(StringComparer.Ordinal);
        foreach (var (type, id) in rewardKeys)
        {
            var name = type == DropTypeWorldItem
                ? worldNames.GetValueOrDefault(id, "")
                : type == DropTypeItem
                    ? itemNames.GetValueOrDefault(id) ?? virtualNames.GetValueOrDefault(id, "")
                    : virtualNames.GetValueOrDefault(id, "");
            if (name.Length > 0) items[$"{Key(type)}:{Key(id)}"] = name;
        }

        return new Dictionary<string, object?>
        {
            ["areas"] = areas.ToDictionary(pair => Key(pair.Key), pair => (object?)pair.Value),
            ["targets"] = targets.ToDictionary(pair => Key(pair.Key), pair => (object?)pair.Value),
            ["items"] = items
        };
    }

    private static List<int> IntList(Dictionary<string, object?> row, string field) =>
        Get(row, field) is int[] values ? values.ToList() : [];

    private static Dictionary<int, string> NamesById(string path, string table) =>
        LoadLuaRecords(path, table, ["id", "name"])
            .Select(row => (Id: SafeInt(Get(row, "id")), Name: PlainText(Get(row, "name"))))
            .Where(item => item.Id > 0 && item.Name.Length > 0)
            .GroupBy(item => item.Id)
            .ToDictionary(group => group.Key, group => group.First().Name);
}
