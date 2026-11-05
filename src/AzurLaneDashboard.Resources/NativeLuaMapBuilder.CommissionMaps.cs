using System.Globalization;

namespace AzurLaneDashboard.Resources;

internal sealed partial class NativeLuaMapBuilder
{
    /// <summary>
    /// collection_template: how long each commission runs and the oil it takes. SC 13004 only answers the start,
    /// so the dashboard times a commission the way EventStartCommand does (server time + collect_time).
    /// </summary>
    private static Dictionary<string, object?> BuildCommissionMap(string luaDirectory)
    {
        var commissions = new SortedDictionary<int, Dictionary<string, object?>>();
        foreach (var row in LoadLuaRecords(
                     Path.Combine(luaDirectory, "sharecfg", "collection_template.lua"),
                     "collection_template",
                     ["id", "collect_time", "oil"]))
        {
            var id = SafeInt(Get(row, "id"));
            var collectTime = SafeInt(Get(row, "collect_time"));
            if (id > 0 && collectTime > 0)
            {
                commissions[id] = new Dictionary<string, object?>
                {
                    ["collect_time"] = collectTime,
                    ["oil"] = SafeInt(Get(row, "oil"))
                };
            }
        }

        return commissions.ToDictionary(pair => pair.Key.ToString(CultureInfo.InvariantCulture),
            pair => (object?)pair.Value);
    }
}
