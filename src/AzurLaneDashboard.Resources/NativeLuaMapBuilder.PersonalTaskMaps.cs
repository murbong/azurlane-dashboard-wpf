using System.Globalization;

namespace AzurLaneDashboard.Resources;

internal sealed partial class NativeLuaMapBuilder
{
    private const int MaxChainSteps = 64;

    /// <summary>
    /// Character personal tasks (캐릭터 임무). task_data_trigger names the first task of each chain and the ship
    /// group whose secretary touch starts it (group 0: an activity starts it, as Akashi's requests); each task's
    /// next_task in task_data_template leads to the next step.
    /// </summary>
    private static Dictionary<string, object?> BuildPersonalTaskMap(string luaDirectory)
    {
        var tasks = LoadLuaRecords(Path.Combine(luaDirectory, "sharecfgdata", "task_data_template.lua"), "task_data_template",
                ["id", "name", "desc", "type", "next_task", "target_num", "sub_type", "story_id"])
            .ToDictionary(row => SafeInt(Get(row, "id")));
        var chains = new List<object?>();
        foreach (var trigger in LoadLuaRecords(Path.Combine(luaDirectory, "sharecfg", "task_data_trigger.lua"), "task_data_trigger",
                     ["id", "task_id", "group_id", "activity_id", "type"]))
        {
            var first = SafeInt(Get(trigger, "task_id"));
            if (first <= 0 || !tasks.ContainsKey(first)) continue;
            var steps = new List<object?>();
            var seen = new HashSet<int>();
            for (var id = first; id > 0 && tasks.TryGetValue(id, out var task) && seen.Add(id) && steps.Count < MaxChainSteps;
                 id = SafeInt(Get(task, "next_task")))
            {
                steps.Add(new Dictionary<string, object?>
                {
                    ["id"] = id,
                    ["name"] = PlainText(Get(task, "name")),
                    ["desc"] = PlainText(Get(task, "desc")),
                    ["target"] = SafeInt(Get(task, "target_num")),
                    ["sub_type"] = SafeInt(Get(task, "sub_type"))
                });
            }

            chains.Add(new Dictionary<string, object?>
            {
                ["trigger"] = SafeInt(Get(trigger, "id")),
                ["group"] = SafeInt(Get(trigger, "group_id")),
                ["activity"] = SafeInt(Get(trigger, "activity_id")),
                ["steps"] = steps
            });
        }

        return new Dictionary<string, object?>
        {
            ["chains"] = chains,
            ["count"] = chains.Count.ToString(CultureInfo.InvariantCulture)
        };
    }
}
