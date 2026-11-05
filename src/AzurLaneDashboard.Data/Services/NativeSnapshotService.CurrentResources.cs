using System.Globalization;
using System.Text.Json;
using AzurLaneDashboard.Core.Models;
using AzurLaneDashboard.Core.Services;
using Microsoft.Data.Sqlite;

namespace AzurLaneDashboard.Data.Services;

public sealed partial class NativeSnapshotService
{
    // Listed first in this order and kept at 0: gold, oil, gems, merit, furniture and fleet coins, prototype
    // blueprints, game coins and tickets, the mailbox stock and what the production fields hold.
    private static readonly int[] CoreResourceTypes = [1, 2, 4, 14, 3, 6, 8, 9, 11, 12, 15, 16, 17, 5, 7, 10];

    private static List<ResourceRow> BuildCurrentResources(SqliteConnection connection, JsonElement resourceMap)
    {
        if (!TableExists(connection, "current_resources"))
        {
            return [];
        }

        var resources = new List<(int Type, int Count)>();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT resource_type, num FROM current_resources";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            resources.Add((Int(reader, "resource_type"), Int(reader, "num")));
        }

        return ResourceRows(resources, resourceMap);
    }

    /// <summary>Core resources first, then the others the account still holds, newest (highest type) first.</summary>
    private static List<ResourceRow> ResourceRows(IEnumerable<(int Type, int Count)> resources, JsonElement resourceMap) =>
        resources
            .Where(resource => resource.Type > 0)
            .GroupBy(resource => resource.Type)
            .Select(group => ToResourceRow(group.Key, group.Last().Count, resourceMap))
            .Where(row => row.IsCore || row.Count > 0)
            .OrderBy(row => row.IsCore ? System.Array.IndexOf(CoreResourceTypes, row.Type) : int.MaxValue)
            .ThenByDescending(row => row.Type)
            .ToList();

    private static ResourceRow ToResourceRow(int type, int count, JsonElement resourceMap)
    {
        var info = MapObject(resourceMap, type);
        return new ResourceRow
        {
            Type = type,
            Label = FirstNonEmpty(JsonString(info, "label"), JsonString(info, "name"), $"resource:{type}"),
            IconUrl = JsonString(info, "icon_url"),
            Rarity = RarityHelper.Normalize((Math.Max(1, JsonInt(info, "rarity")) + 1).ToString(CultureInfo.InvariantCulture)),
            IsCore = CoreResourceTypes.Contains(type),
            Count = count
        };
    }

    private static Dictionary<string, string> BuildCurrentCommanderProfile(SqliteConnection connection)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!TableExists(connection, "current_commander_profile"))
        {
            return result;
        }

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT key, value_json FROM current_commander_profile";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var key = Text(reader, "key");
            if (string.IsNullOrWhiteSpace(key))
            {
                continue;
            }

            var value = ParseJson(Text(reader, "value_json"));
            result[key] = ValueText(value);
        }

        return result;
    }
}
