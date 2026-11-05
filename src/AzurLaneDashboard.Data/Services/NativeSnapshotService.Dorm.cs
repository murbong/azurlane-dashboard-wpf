using System.Text.Json;
using AzurLaneDashboard.Core.Models;
using Microsoft.Data.Sqlite;

namespace AzurLaneDashboard.Data.Services;

public sealed partial class NativeSnapshotService
{
    /// <summary>
    /// The courtyard as of the last capture: slots and floors from the latest login list (SC 19001), who sits on
    /// which floor after the placements since, and the farming fleet's floors in fleet order.
    /// </summary>
    private static DormState ReadDormState(
        SqliteConnection connection, List<ShipState> ships, int farmFleetId, IReadOnlyList<FleetCompositionGroupRow> groups)
    {
        if (!TableExists(connection, "decoded_packets")) return new DormState();

        JsonElement login;
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT json FROM decoded_packets WHERE packet_id = 19001 ORDER BY rowid DESC LIMIT 1";
            if (command.ExecuteScalar() is not string json) return new DormState();
            login = ParseJson(json);
        }

        var floors = ReadDormFloors(connection);
        var byUid = ships.Where(ship => ship.Uid.Length > 0)
            .GroupBy(ship => ship.Uid, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

        DormShip Ship(string uid, int floor)
        {
            var ship = byUid.GetValueOrDefault(uid);
            return new DormShip(uid, ship?.Name.Trim() ?? uid, ship?.IconUrl ?? "", ship?.Rarity ?? "", ship?.Level ?? 0, floor);
        }

        var placed = floors
            .Where(pair => pair.Value is DormState.TrainingFloor or DormState.RestFloor)
            .ToDictionary(pair => pair.Key, pair => Ship(pair.Key, pair.Value), StringComparer.Ordinal);
        var farm = farmFleetId > 0
            ? groups.FirstOrDefault(group => group.FleetId == farmFleetId)?.Ships
                  .Select(row => row.ShipUid)
                  .Where(uid => !string.IsNullOrWhiteSpace(uid))
                  .Select(uid => Ship(uid, placed.GetValueOrDefault(uid)?.Floor ?? 0))
                  .ToList() ?? []
            : [];
        return new DormState
        {
            IsKnown = true,
            SlotsPerFloor = JsonInt(login, "exp_pos"),
            FloorCount = Math.Max(1, JsonInt(login, "floor_num")),
            Ships = placed,
            FarmFleet = farm,
            FarmFleetId = farmFleetId
        };
    }
}
