using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace AzurLaneDashboard.Data.Services;

public sealed partial class NativeSnapshotService
{
    private static JsonElement ReadSummary(SqliteConnection connection)
    {
        if (!TableExists(connection, "meta"))
        {
            return default;
        }

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT value FROM meta WHERE key = 'summary_json' LIMIT 1";
        var json = command.ExecuteScalar() as string;
        if (string.IsNullOrWhiteSpace(json))
        {
            return default;
        }

        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private static SnapshotMaps ReadMaps(string mapsDirectory)
    {
        JsonElement Map(string name) => MapFileStore.Read(mapsDirectory, name);
        return new SnapshotMaps(
            Map("ships.json"),
            Map("skins.json"),
            Map("equipment.json"),
            Map("fire_rate_equipment.json"),
            Map("equipment_skins.json"),
            Map("spweapons.json"),
            Map("resources.json"),
            Map("skills.json"),
            Map("ship_level.json"),
            Map("stages.json"),
            Map("fleet_tech.json"),
            Map("fleet_tech_nation.json"),
            Map("ship_blueprints.json"),
            Map("ship_transforms.json"),
            Map("regal_ships.json"),
            Map("regal_equips.json"),
            Map("regal_gear_map.json"),
            Map("regal_augments.json"),
            Map("regal_changelog.json"),
            Map("meta_bosses.json"),
            Map("meta_boss_guides.json"),
            Map("commanders.json"),
            Map("meowfficer_guide.json"),
            Map("month_shop.json"),
            Map("oil_costs.json"),
            Map("commissions.json"),
            Map("auction_collection.json"),
            Map("dashboard_settings.json"));
    }
}
