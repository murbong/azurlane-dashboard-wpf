using System.Text.Json;
using System.Text.RegularExpressions;
using AzurLaneDashboard.Core.Models;

namespace AzurLaneDashboard.Core.Services;

/// <summary>
/// 캐릭터 임무: each character chain's state from the player's task list. A chain is done when every step was
/// submitted, active while one step is open (or the next is still to come), ready when the player owns the
/// ship but never started it (the secretary touch starts it), and locked without the ship.
/// </summary>
public static class PersonalTaskBuilder
{
    private static readonly Regex NameCode = new(@"\{namecode:\d+\}", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static IReadOnlyList<PersonalTaskChain> ParseChains(JsonElement map)
    {
        if (map.ValueKind != JsonValueKind.Object || !map.TryGetProperty("chains", out var chains) ||
            chains.ValueKind != JsonValueKind.Array)
            return [];
        static int Int(JsonElement element, string name) =>
            element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetInt32() : 0;
        static string Text(JsonElement element, string name) =>
            element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
        return chains.EnumerateArray()
            .Select(chain => new PersonalTaskChain(Int(chain, "group"),
                chain.TryGetProperty("steps", out var steps) && steps.ValueKind == JsonValueKind.Array
                    ? steps.EnumerateArray().Select(step => new PersonalTaskStep(Int(step, "id"), Text(step, "name"),
                        Text(step, "desc"), Int(step, "target"))).Where(step => step.Id > 0).ToList()
                    : []))
            .Where(chain => chain.Steps.Count > 0)
            .ToList();
    }

    /// <param name="owned">Owned ship groups (the best copy of each).</param>
    /// <param name="names">Every ship group's name, for ships the player does not own.</param>
    public static PersonalTaskDashboard Build(
        IReadOnlyList<PersonalTaskChain> chains, IReadOnlyDictionary<int, PlayerTask> tasks,
        IReadOnlyDictionary<int, PersonalTaskShip> owned, IReadOnlyDictionary<int, string> names, string basisText)
    {
        if (chains.Count == 0 || tasks.Count == 0) return PersonalTaskDashboard.Empty;

        var rows = new List<PersonalTaskRow>();
        foreach (var chain in chains)
        {
            var ship = owned.GetValueOrDefault(chain.Group);
            // Activity chains (Akashi's requests) have no ship; their first step's name stands in.
            var name = ship?.Name ?? names.GetValueOrDefault(chain.Group) ?? "";
            if (name.Length == 0) name = Regex.Replace(chain.Steps[0].Name, @"\s*\d+$", "");
            var done = chain.Steps.Count(step => tasks.GetValueOrDefault(step.Id)?.SubmitTime > 0);
            var open = chain.Steps.FirstOrDefault(step => tasks.TryGetValue(step.Id, out var task) && task.SubmitTime == 0);
            var started = chain.Steps.Any(step => tasks.ContainsKey(step.Id));
            var status = done == chain.Steps.Count ? PersonalTaskStatus.Done
                : started ? PersonalTaskStatus.Active
                : chain.Group == 0 || ship is not null ? PersonalTaskStatus.Ready
                : PersonalTaskStatus.Locked;
            var current = open ?? chain.Steps.FirstOrDefault(step => tasks.GetValueOrDefault(step.Id)?.SubmitTime is null or 0);
            rows.Add(new PersonalTaskRow
            {
                Group = chain.Group,
                ShipName = name,
                IconUrl = ship?.IconUrl ?? "",
                Rarity = ship?.Rarity ?? "",
                IsOwned = ship is not null,
                Level = ship?.Level ?? 0,
                Status = status,
                DoneSteps = done,
                StepCount = chain.Steps.Count,
                StepName = current?.Name ?? "",
                StepDesc = current is null ? "" : NameCode.Replace(current.Desc, name),
                Progress = open is null ? 0 : tasks[open.Id].Progress,
                Target = current?.Target ?? 0
            });
        }

        // In progress first (closest to the next step on top), then the ones to start, then done, then locked.
        var order = new Dictionary<string, int>
        {
            [PersonalTaskStatus.Active] = 0, [PersonalTaskStatus.Ready] = 1, [PersonalTaskStatus.Done] = 2,
            [PersonalTaskStatus.Locked] = 3
        };
        return new PersonalTaskDashboard
        {
            HasData = true,
            BasisText = basisText,
            Rows = rows.OrderBy(row => order[row.Status])
                .ThenByDescending(row => row.Status == PersonalTaskStatus.Active ? row.Fraction : row.Level)
                .ThenBy(row => row.ShipName, StringComparer.CurrentCulture)
                .ToList()
        };
    }
}
