using System.Globalization;

namespace AzurLaneDashboard.Resources;

internal sealed partial class NativeLuaMapBuilder
{
    // month_shop_template lists per calendar month; the 월간 상점 shows them as 코어 월간 and 원형 tabs.
    private static readonly string[] MonthShopLists =
        ["core_shop_goods", "blueprint_shop_goods", "blueprint_shop_limit_goods"];

    /// <summary>
    /// The monthly shop (보급 상점 › 월간 상점): month_shop_template names this month's goods, and
    /// activity_shop_template (the MonthlyShop goods config) gives each one's commodity, price and limit.
    /// Item goods carry their name, icon and rarity here because the dashboard has no item map.
    /// </summary>
    private static Dictionary<string, object?> BuildMonthShopMap(string luaDirectory)
    {
        var months = new SortedDictionary<int, Dictionary<string, object?>>();
        var wanted = new HashSet<int>();
        foreach (var (id, block) in LoadLuaBlocks(
                     Path.Combine(luaDirectory, "sharecfg", "month_shop_template.lua"), "month_shop_template"))
        {
            var lists = new Dictionary<string, object?>();
            foreach (var key in MonthShopLists)
            {
                var goods = ParseLuaIntArray(block, key);
                lists[key] = goods;
                wanted.UnionWith(goods);
            }

            months[id] = lists;
        }

        var items = new Dictionary<int, Dictionary<string, object?>>();
        foreach (var (folder, table) in new[]
                 {
                     ("sharecfgdata", "item_data_statistics"), ("sharecfgdata", "item_virtual_data_statistics")
                 })
        {
            foreach (var row in LoadLuaRecords(Path.Combine(luaDirectory, folder, table + ".lua"), table,
                         ["id", "name", "icon", "rarity"]))
            {
                if (SafeInt(Get(row, "id")) is > 0 and var itemId) items[itemId] = row;
            }
        }

        var goodsMap = new SortedDictionary<int, Dictionary<string, object?>>();
        foreach (var row in LoadLuaRecords(
                     Path.Combine(luaDirectory, "sharecfg", "activity_shop_template.lua"),
                     "activity_shop_template",
                     ["id", "commodity_type", "commodity_id", "num", "resource_category", "resource_type",
                         "resource_num", "num_limit", "order"]))
        {
            var id = SafeInt(Get(row, "id"));
            if (!wanted.Contains(id)) continue;

            var type = SafeInt(Get(row, "commodity_type"));
            var commodity = SafeInt(Get(row, "commodity_id"));
            var entry = new Dictionary<string, object?>
            {
                ["id"] = id,
                // 1 resource, 2 item, 3 equipment, 4 ship (DROP_TYPE_*).
                ["type"] = type,
                ["commodity_id"] = commodity,
                ["num"] = Math.Max(1, SafeInt(Get(row, "num"))),
                ["currency_category"] = SafeInt(Get(row, "resource_category")),
                ["currency_id"] = SafeInt(Get(row, "resource_type")),
                ["price"] = SafeInt(Get(row, "resource_num")),
                ["limit"] = SafeInt(Get(row, "num_limit")),
                ["order"] = SafeInt(Get(row, "order"))
            };
            if (type == 2 && items.TryGetValue(commodity, out var item))
            {
                var icon = (Convert.ToString(Get(item, "icon"), CultureInfo.InvariantCulture) ?? "").Trim();
                entry["name"] = (Convert.ToString(Get(item, "name"), CultureInfo.InvariantCulture) ?? "").Trim();
                entry["icon_url"] = icon.StartsWith("Props/", StringComparison.OrdinalIgnoreCase)
                    ? $"/static/game-assets/props/{icon["Props/".Length..].ToLowerInvariant()}.png"
                    : "";
                entry["rarity"] = Math.Max(1, SafeInt(Get(item, "rarity")));
            }

            goodsMap[id] = entry;
        }

        return new Dictionary<string, object?>
        {
            ["months"] = months.ToDictionary(pair => pair.Key.ToString(CultureInfo.InvariantCulture),
                pair => (object?)pair.Value),
            ["goods"] = goodsMap.ToDictionary(pair => pair.Key.ToString(CultureInfo.InvariantCulture),
                pair => (object?)pair.Value)
        };
    }
}
