using System.Globalization;

namespace AzurLaneDashboard.Resources;

internal sealed partial class NativeLuaMapBuilder
{
    /// <summary>
    /// What equipment packets that only answer with a result produce: a transform formula's target
    /// (equip_upgrade_data, CS 14013/14015 send the formula id) and a compose recipe's equipment
    /// (compose_data_template, CS 14006 sends the recipe id).
    /// </summary>
    private static Dictionary<string, object?> BuildEquipmentFormulaMap(string luaDirectory)
    {
        Dictionary<string, int> Load(string table, string valueField) =>
            LoadLuaRecords(Path.Combine(luaDirectory, "sharecfg", table + ".lua"), table, ["id", valueField])
                .Select(row => (Id: SafeInt(Get(row, "id")), Value: SafeInt(Get(row, valueField))))
                .Where(row => row.Id > 0 && row.Value > 0)
                .OrderBy(row => row.Id)
                .ToDictionary(row => row.Id.ToString(CultureInfo.InvariantCulture), row => row.Value);

        return new Dictionary<string, object?>
        {
            ["upgrades"] = Load("equip_upgrade_data", "target_id"),
            ["composes"] = Load("compose_data_template", "equip_id")
        };
    }
}
