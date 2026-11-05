using System.Text.Json;
using AzurLaneDashboard.Core.Models;

namespace AzurLaneDashboard.Data.Services;

public sealed partial class NativeSnapshotService
{
    private sealed class SystemsSnapshot
    {
        public List<SystemStatusRow> Statuses { get; init; } = new();
        public List<AcademyRoomRow> AcademyRooms { get; init; } = new();
        public List<ResearchQueueRow> ResearchQueue { get; init; } = new();
        public List<CommissionRow> Commissions { get; init; } = new();
        public List<BuildQueueRow> Builds { get; init; } = new();
    }

    private sealed record AcademySystem(string StatusLabel, string Detail, string Kind, List<AcademyRoomRow> Rooms);

    private sealed record ResearchSystem(string StatusLabel, string Detail, string Kind, List<ResearchQueueRow> Queue);

    private sealed record CommissionSystem(string StatusLabel, string Detail, string Kind, List<CommissionRow> Items);

    private sealed record BuildSystem(string StatusLabel, string Detail, string Kind, List<BuildQueueRow> Items);
}
