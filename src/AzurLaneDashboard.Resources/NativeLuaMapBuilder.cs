using System.Globalization;
using System.Net.Http;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AzurLaneDashboard.Resources;

internal sealed partial class NativeLuaMapBuilder
{
    private const string RegalBaseUrl = "https://azur-lane-regal.suchiguma.com/assets";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(60)
    };

    private static readonly IReadOnlyDictionary<string, string> RegalAssets = new Dictionary<string, string>
    {
        ["ships"] = "ships.json",
        ["equips"] = "equips.json",
        ["gear_map"] = "gear_map.json",
        ["augments"] = "augments.json",
        ["changelog"] = "changelog.json"
    };

    public async Task<IReadOnlyDictionary<string, int>> BuildAsync(
        string luaDirectory,
        string outputDirectory,
        bool includeRegal,
        IList<string> logs,
        CancellationToken cancellationToken)
    {
        luaDirectory = Path.GetFullPath(luaDirectory);
        outputDirectory = Path.GetFullPath(outputDirectory);
        if (!Directory.Exists(luaDirectory))
        {
            throw new DirectoryNotFoundException($"AzurLaneLuaScripts region directory not found: {luaDirectory}");
        }

        Directory.CreateDirectory(outputDirectory);
        var data = LoadLuaSource(luaDirectory);

        var shipMap = BuildShipMap(data.ShipStats, data.ShipTemplates);
        var skinMap = BuildSkinMap(data.Skins);
        var equipmentMap = BuildEquipmentMap(data.EquipmentTemplates, data.EquipmentStats);
        var fireRateEquipmentMap = BuildFireRateEquipmentMap(data.EquipmentStats, data.WeaponProperties);
        var equipmentSkinMap = BuildEquipmentSkinMap(data.EquipmentSkins);
        var skillMap = BuildSkillMap(data.Skills, data.BuffDirectory);
        var shipLevelMap = BuildShipLevelMap(data.ShipLevels);
        var stageMap = BuildStageMap(data.Stages, data.Chapters);
        var fleetTechMap = BuildFleetTechMap(data.FleetTech);
        var fleetTechNationMap = BuildFleetTechNationMap(data.FleetTechNation);
        var shipBlueprintMap = BuildShipBlueprintMap(data.ShipBlueprints);
        var shipTransformMap = BuildShipTransformMap(data.ShipTransforms);
        var spWeaponMap = BuildSpWeaponMap(data.SpWeaponStats);
        var resourceMap = BuildResourceMap(data.Resources, luaDirectory);
        var oilCostMap = BuildOilCostMap(data, luaDirectory);
        var commissionMap = BuildCommissionMap(luaDirectory);
        var equipmentFormulaMap = BuildEquipmentFormulaMap(luaDirectory);
        var shipUpgradeMap = BuildShipUpgradeMap(luaDirectory);
        var monthShopMap = BuildMonthShopMap(luaDirectory);
        var commanderMap = BuildCommanderMap(luaDirectory);
        var metaBossMap = BuildMetaBossMap(data.MetaBosses, data.MetaBossLevels, data.EnemyStatisticsPath,
            data.DungeonDirectory);

        WriteJson(Path.Combine(outputDirectory, "ships.json"), shipMap);
        WriteJson(Path.Combine(outputDirectory, "skins.json"), skinMap);
        WriteJson(Path.Combine(outputDirectory, "equipment.json"), equipmentMap);
        WriteJson(Path.Combine(outputDirectory, "fire_rate_equipment.json"), fireRateEquipmentMap);
        WriteJson(Path.Combine(outputDirectory, "equipment_skins.json"), equipmentSkinMap);
        WriteJson(Path.Combine(outputDirectory, "skills.json"), skillMap);
        WriteJson(Path.Combine(outputDirectory, "ship_level.json"), shipLevelMap);
        WriteJson(Path.Combine(outputDirectory, "stages.json"), stageMap);
        WriteJson(Path.Combine(outputDirectory, "fleet_tech.json"), fleetTechMap);
        WriteJson(Path.Combine(outputDirectory, "fleet_tech_nation.json"), fleetTechNationMap);
        WriteJson(Path.Combine(outputDirectory, "ship_blueprints.json"), shipBlueprintMap);
        WriteJson(Path.Combine(outputDirectory, "ship_transforms.json"), shipTransformMap);
        WriteJson(Path.Combine(outputDirectory, "spweapons.json"), spWeaponMap);
        WriteJson(Path.Combine(outputDirectory, "resources.json"), resourceMap);
        WriteJson(Path.Combine(outputDirectory, "meta_bosses.json"), metaBossMap);
        WriteJson(Path.Combine(outputDirectory, "oil_costs.json"), oilCostMap);
        WriteJson(Path.Combine(outputDirectory, "commissions.json"), commissionMap);
        WriteJson(Path.Combine(outputDirectory, "equipment_formulas.json"), equipmentFormulaMap);
        WriteJson(Path.Combine(outputDirectory, "ship_upgrades.json"), shipUpgradeMap);
        WriteJson(Path.Combine(outputDirectory, "month_shop.json"), monthShopMap);
        WriteJson(Path.Combine(outputDirectory, "commanders.json"), commanderMap);

        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["ships"] = shipMap.Count,
            ["skins"] = skinMap.Count,
            ["equipment"] = equipmentMap.Count,
            ["fire_rate_equipment"] = fireRateEquipmentMap.Count,
            ["equipment_skins"] = equipmentSkinMap.Count,
            ["skills"] = skillMap.Count,
            ["ship_level"] = shipLevelMap.Count,
            ["stages"] = stageMap.Count,
            ["fleet_tech"] = fleetTechMap.Count,
            ["fleet_tech_nation"] = fleetTechNationMap.Count,
            ["ship_blueprints"] = shipBlueprintMap.Count,
            ["ship_transforms"] = shipTransformMap.Count,
            ["spweapons"] = spWeaponMap.Count,
            ["resources"] = resourceMap.Count,
            ["meta_bosses"] = metaBossMap.Count,
            ["oil_costs"] = ((System.Collections.IDictionary)oilCostMap["ships"]!).Count,
            ["commissions"] = commissionMap.Count,
            ["month_shop"] = ((System.Collections.IDictionary)monthShopMap["goods"]!).Count,
            ["commanders"] = ((System.Collections.IDictionary)commanderMap["commanders"]!).Count
        };

        logs.Add($"maps built: ships={shipMap.Count}, equipment={equipmentMap.Count}, spweapons={spWeaponMap.Count}");
        if (includeRegal)
        {
            await FetchRegalAssetsAsync(outputDirectory, counts, logs, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            logs.Add("community recommendation sync skipped by option");
        }

        return counts;
    }

    private sealed record LuaSource(
        IReadOnlyList<Dictionary<string, object?>> ShipStats,
        IReadOnlyList<Dictionary<string, object?>> ShipTemplates,
        IReadOnlyList<Dictionary<string, object?>> Skins,
        IReadOnlyList<Dictionary<string, object?>> EquipmentTemplates,
        IReadOnlyList<Dictionary<string, object?>> EquipmentStats,
        IReadOnlyList<Dictionary<string, object?>> WeaponProperties,
        IReadOnlyList<Dictionary<string, object?>> EquipmentSkins,
        IReadOnlyList<Dictionary<string, object?>> Skills,
        IReadOnlyList<Dictionary<string, object?>> ShipLevels,
        IReadOnlyList<Dictionary<string, object?>> Stages,
        IReadOnlyList<Dictionary<string, object?>> Chapters,
        IReadOnlyList<Dictionary<string, object?>> FleetTech,
        IReadOnlyList<Dictionary<string, object?>> FleetTechNation,
        IReadOnlyList<Dictionary<string, object?>> ShipBlueprints,
        IReadOnlyList<Dictionary<string, object?>> ShipTransforms,
        IReadOnlyList<Dictionary<string, object?>> SpWeaponStats,
        IReadOnlyList<Dictionary<string, object?>> Resources,
        IReadOnlyList<Dictionary<string, object?>> MetaBosses,
        IReadOnlyList<Dictionary<string, object?>> MetaBossLevels,
        string EnemyStatisticsPath,
        string DungeonDirectory,
        string BuffDirectory);
}
