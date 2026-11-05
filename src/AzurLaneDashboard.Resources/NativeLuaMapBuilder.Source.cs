using System.Globalization;
using System.Net.Http;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AzurLaneDashboard.Resources;

internal sealed partial class NativeLuaMapBuilder
{
    private static readonly string[] WeaponPropertyScalarFields =
    [
        "id", "base", "type", "range", "damage", "corrected", "reload_max", "recover_time",
        "initial_over_heat", "queue", "attack_attribute", "attack_attribute_ratio", "search_type"
    ];

    private static readonly string[] WeaponPropertyArrayFields = ["bullet_ID", "barrage_ID"];

    private static LuaSource LoadLuaSource(string luaDirectory)
    {
        return new LuaSource(
            LoadLuaRecords(
                Path.Combine(luaDirectory, "sharecfgdata", "ship_data_statistics.lua"),
                "ship_data_statistics",
                ["id", "name", "english_name", "rarity", "star", "skin_id", "type", "nationality"]),
            LoadLuaRecords(
                Path.Combine(luaDirectory, "sharecfgdata", "ship_data_template.lua"),
                "ship_data_template",
                ["id", "group_type", "star_max", "max_level", "type", "oil_at_start", "oil_at_end"]),
            LoadLuaRecords(
                Path.Combine(luaDirectory, "sharecfgdata", "ship_skin_template.lua"),
                "ship_skin_template",
                ["id", "name", "ship_group", "skin_type", "shop_id", "prefab", "painting"]),
            LoadLuaRecords(
                Path.Combine(luaDirectory, "sharecfgdata", "equip_data_template.lua"),
                "equip_data_template",
                ["id", "base", "type", "group", "next", "prev", "level"]),
            LoadLuaRecords(
                Path.Combine(luaDirectory, "sharecfgdata", "equip_data_statistics.lua"),
                "equip_data_statistics",
                [
                    "id", "base", "name", "tech", "type", "rarity", "icon", "nationality", "ammo", "damage",
                    "attribute_1", "attribute_2", "attribute_3", "value_1", "value_2", "value_3"
                ],
                ["weapon_id"]),
            LoadWeaponProperties(luaDirectory),
            LoadLuaRecords(
                Path.Combine(luaDirectory, "sharecfg", "equip_skin_template.lua"),
                "equip_skin_template",
                ["id", "name", "desc", "rarity", "type", "themeid", "icon"],
                ["equip_type"]),
            LoadLuaRecords(
                Path.Combine(luaDirectory, "sharecfg", "skill_data_template.lua"),
                "skill_data_template",
                ["id", "name", "type", "max_level"]),
            LoadShipLevelRecords(Path.Combine(luaDirectory, "sharecfg", "ship_level.lua")),
            LoadLuaRecords(
                Path.Combine(luaDirectory, "sharecfgdata", "expedition_data_template.lua"),
                "expedition_data_template",
                ["id", "name", "type", "dungeon_id", "level", "exp", "icon", "difficulty"]),
            LoadLuaRecords(
                Path.Combine(luaDirectory, "sharecfgdata", "chapter_template.lua"),
                "chapter_template",
                ["id", "name", "chapter_name", "map", "type", "difficulty", "oil"],
                [
                    "use_oil_limit",
                    "expedition_id_weight_list",
                    "ambush_expedition_list",
                    "guarder_expedition_list",
                    "elite_expedition_list",
                    "ai_expedition_list",
                    "boss_expedition_id"
                ]),
            LoadLuaRecords(
                Path.Combine(luaDirectory, "sharecfg", "fleet_tech_ship_template.lua"),
                "fleet_tech_ship_template",
                [
                    "id", "class", "max_star", "pt_get", "pt_level", "pt_upgrage",
                    "add_get_attr", "add_get_value", "add_level_attr", "add_level_value"
                ],
                ["add_get_shiptype", "add_level_shiptype"]),
            LoadFleetTechNationTemplateRecords(
                Path.Combine(luaDirectory, "sharecfg", "fleet_tech_template.lua")),
            LoadLuaRecords(
                Path.Combine(luaDirectory, "sharecfg", "ship_data_blueprint.lua"),
                "ship_data_blueprint",
                ["id", "blueprint_version", "is_pursuing", "strengthen_item"]),
            LoadShipTransformRecords(Path.Combine(luaDirectory, "sharecfg", "ship_data_trans.lua")),
            LoadLuaRecords(
                Path.Combine(luaDirectory, "sharecfgdata", "spweapon_data_statistics.lua"),
                "spweapon_data_statistics",
                [
                    "id", "base", "name", "rarity", "level", "type", "unique", "attribute_1", "attribute_2",
                    "value_1", "value_2", "effect_id", "icon"
                ]),
            LoadLuaRecords(
                Path.Combine(luaDirectory, "sharecfg", "player_resource.lua"),
                "player_resource",
                ["id", "itemid", "item_id", "name"]),
            LoadMetaBossRecords(Path.Combine(luaDirectory, "sharecfg", "world_joint_boss_template.lua")),
            LoadLuaRecords(
                Path.Combine(luaDirectory, "sharecfg", "world_boss_level.lua"),
                "world_boss_level",
                ["id", "hp", "enemy_id", "expedition_id"]),
            Path.Combine(luaDirectory, "sharecfgdata", "enemy_data_statistics.lua"),
            Path.Combine(luaDirectory, "gamecfg", "dungeon"),
            Path.Combine(luaDirectory, "gamecfg", "buff"));
    }

    private static List<Dictionary<string, object?>> LoadShipLevelRecords(string path)
    {
        var records = new List<Dictionary<string, object?>>();
        foreach (var (id, block) in LoadLuaBlocks(path, "ship_level"))
        {
            var record = ParseLuaRecordBlock(block, ["id", "level", "exp", "exp_ur", "level_limit", "fight_oil_ratio"]);
            record.TryAdd("id", id);
            // Cognitive awakening cost charged at a level cap; one list per rarity (2 = N ... 6 = UR/DR).
            for (var rarity = 2; rarity <= 6; rarity++)
            {
                var items = ParseLuaIntTuples(block, $"need_item_rarity{rarity}");
                if (items.Count > 0)
                {
                    record[$"need_item_rarity{rarity}"] = items;
                }
            }

            records.Add(record);
        }

        return records;
    }

    private static List<Dictionary<string, object?>> LoadWeaponProperties(string luaDirectory)
    {
        string[] files =
        [
            "weapon_property.lua",
            "weapon_property_1.lua",
            "weapon_property_2.lua",
            "weapon_property_3.lua"
        ];

        return files
            .SelectMany(file => LoadLuaRecords(
                Path.Combine(luaDirectory, "sharecfgdata", file),
                "weapon_property",
                WeaponPropertyScalarFields,
                WeaponPropertyArrayFields))
            .ToList();
    }
}
