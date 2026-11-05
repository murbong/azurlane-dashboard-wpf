using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;
using AzurLaneDashboard.Core.Models;
using Microsoft.Data.Sqlite;

namespace AzurLaneDashboard.Data.Services;

public sealed partial class NativeSnapshotService
{
    private static bool CurrentStateAvailable(SqliteConnection connection)
    {
        if (!TableExists(connection, "current_ships") ||
            !TableExists(connection, "current_ship_equipment_slots") ||
            !TableExists(connection, "current_equipment_items") ||
            !CurrentStateFresh(connection))
        {
            return false;
        }

        return MaxRowId(connection, "decoded_packets") == 0 || CountRows(connection, "current_ships") > 0;
    }

    private static bool CurrentStateFresh(SqliteConnection connection)
    {
        if (TableExists(connection, "current_state_meta"))
        {
            using var metaCommand = connection.CreateCommand();
            metaCommand.CommandText =
                "SELECT last_decoded_rowid, last_error FROM current_state_meta WHERE id = 1 LIMIT 1";
            using var reader = metaCommand.ExecuteReader();
            if (!reader.Read())
            {
                return false;
            }

            var error = reader.IsDBNull(1)
                ? ""
                : Convert.ToString(reader.GetValue(1), CultureInfo.InvariantCulture) ?? "";
            if (!string.IsNullOrWhiteSpace(error))
            {
                return false;
            }

            return ToInt(reader.GetValue(0)) == MaxRowId(connection, "decoded_packets");
        }

        if (!TableExists(connection, "migration_validation"))
        {
            return true;
        }

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT actual FROM migration_validation WHERE key = 'copy_decoded_packets' LIMIT 1";
        var value = command.ExecuteScalar();
        if (value is null || value == DBNull.Value)
        {
            return true;
        }

        return ToInt(value) == CountRows(connection, "decoded_packets");
    }

    private static CurrentStateInfo ReadCurrentStateInfo(SqliteConnection connection, JsonElement summary)
    {
        var info = new CurrentStateInfo(
            JsonString(summary, "current_state_mode"),
            JsonInt(summary, "current_state_last_decoded_rowid"),
            JsonString(summary, "current_state_last_status"),
            JsonString(summary, "current_state_last_area"),
            JsonString(summary, "current_state_last_detail"),
            "");

        if (TableExists(connection, "current_state_meta"))
        {
            using var meta = connection.CreateCommand();
            meta.CommandText = """
                               SELECT mode, last_decoded_rowid, last_error
                               FROM current_state_meta
                               WHERE id = 1
                               LIMIT 1
                               """;
            using var reader = meta.ExecuteReader();
            if (reader.Read())
            {
                info = info with
                {
                    Mode = Text(reader, "mode"),
                    LastDecodedRowId = Int(reader, "last_decoded_rowid"),
                    LastError = Text(reader, "last_error")
                };
            }
        }

        if (TableExists(connection, "current_packet_apply_log"))
        {
            using var log = connection.CreateCommand();
            log.CommandText = """
                              SELECT status, area, detail
                              FROM current_packet_apply_log
                              ORDER BY decoded_rowid DESC
                              LIMIT 1
                              """;
            using var reader = log.ExecuteReader();
            if (reader.Read())
            {
                info = info with
                {
                    LastStatus = Text(reader, "status"),
                    LastArea = Text(reader, "area"),
                    LastDetail = Text(reader, "detail")
                };
            }
        }

        return info;
    }

    private static DashboardSnapshot BuildSnapshotFromCurrentState(
        SqliteConnection connection,
        string databasePath,
        JsonElement summary,
        SnapshotMaps maps,
        List<PacketStatRow> packetStats,
        List<PacketStatRow> packetInterpretationGaps,
        List<DecodeFailureRow> decodeFailures,
        int packetPayloadsSaved,
        CurrentStateInfo currentStateInfo,
        string projectRoot,
        AuctionDashboard auction)
    {
        var ships = BuildCurrentShips(connection, maps, projectRoot);
        AttachCommunityRatings(ships, maps.RegalShips);
        ApplyFleetTechUnlockState(ships);
        ApplyFleetTechReloadAdditions(ships, maps.FleetTech);
        ApplyFleetTechNationReloadAdditions(connection, ships, maps.FleetTechNation);

        var equipment = BuildCurrentEquipmentInventory(connection, maps.Equipment, projectRoot, ships);
        if (equipment.Count == 0)
        {
            equipment = BuildEquipment([], ships, maps.Equipment, projectRoot);
        }

        var specialEquipment = BuildCurrentSpecialEquipmentInventory(connection, maps.SpWeapons, projectRoot, ships);
        var equipmentSkins = BuildCurrentEquipmentSkins(connection, maps.EquipmentSkins, projectRoot);
        AttachCommunityRecommendations(ships, maps, equipment, specialEquipment, projectRoot);
        var battle = BuildCurrentBattleSnapshot(connection, ships, maps.ShipLevels, maps.Stages);
        var levelPriorities = BuildLevelPriorities(ships, maps.ShipLevels);
        var skillPriorities = BuildSkillPriorities(ships, maps.Skills, projectRoot);
        var fleetLayout = ReadCurrentFleetLayout(connection);
        ApplyFleetLabels(ships, fleetLayout);
        var gearGaps = BuildGearGaps(ships);
        var equipmentRecommendations = BuildEquipmentRecommendations(equipment);
        var communityGearShortages = BuildCommunityGearShortages(ships);
        var cleanup = BuildCleanupCandidates(ships);
        var fleetDamageRows = BuildFleetDamageRows(ships, maps.FireRateEquipment, maps.Equipment, maps.Skills, fleetLayout);
        var fleetCompositionGroups =
            BuildFleetCompositionGroups(ships, fleetLayout, fleetDamageRows, maps.FireRateEquipment, maps.Equipment);
        var resources = BuildCurrentResources(connection, maps.Resources);
        var shipsByUid = ships
            .Where(ship => !string.IsNullOrWhiteSpace(ship.Uid))
            .GroupBy(ship => ship.Uid, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var systems = BuildCurrentSystems(connection, shipsByUid, maps.Skills, maps.Commissions);
        var commander = BuildCurrentCommanderProfile(connection);

        var dockBaseMax = ToInt(commander.GetValueOrDefault("ship_bag_max"));
        var guildDockBonus = JsonInt(maps.AppSettings, "guild_dock_capacity_bonus");
        var dockMax = dockBaseMax > 0 ? dockBaseMax + guildDockBonus : 0;
        var equipBagMax = ToInt(commander.GetValueOrDefault("equip_bag_max"));
        var dockExempt = ships.Count(ship => IsDockExempt(ship));
        var dockUsed = Math.Max(0, ships.Count - dockExempt);
        var equipmentBagCount = equipment.Sum(item => item.BagCount);
        var equipmentEquippedCount = equipment.Sum(item => item.EquippedCount);
        var captureStatus = BuildCaptureStatus(summary);
        var fleetTechPending = ships.Count(ship => ship.FleetTechLevelPending);
        var communityMatched = ships.Count(ship => !string.IsNullOrWhiteSpace(ship.CommunityLabel));
        var battleSummary = battle.SummaryText;

        var fleetPlan = BuildFleetPlan(connection, ships, maps, fleetCompositionGroups, equipment, specialEquipment,
            projectRoot);
        return new DashboardSnapshot
        {
            Auction = auction,
            Kpis = new ObservableCollection<DashboardKpiRow>
            {
                new()
                {
                    Title = "도크", Value = dockMax > 0 ? $"{dockUsed:N0}/{dockMax:N0}" : $"{ships.Count:N0}",
                    Detail = $"면제 {dockExempt:N0} · 120+ {ships.Count(ship => ship.Level >= 120):N0}"
                },
                new()
                {
                    Title = "장비",
                    Value = equipBagMax > 0 ? $"{equipmentBagCount:N0}/{equipBagMax:N0}" : $"{equipment.Count:N0}",
                    Detail =
                        $"장착 {equipmentEquippedCount:N0} · SP {specialEquipment.Count:N0} · 스킨 {equipmentSkins.Count:N0}"
                },
                new()
                {
                    Title = "전투", Value = $"{battle.SampleRunCount:N0}",
                    Detail = $"{battle.SampleLabel} · 표본 {battle.ShipRates.Count:N0}척"
                },
                new() { Title = "커뮤니티", Value = $"{communityMatched:N0}", Detail = $"평가 기준 {maps.RegalShipCount:N0}척" },
                new() { Title = "스킬", Value = $"{skillPriorities.Count:N0}", Detail = "Lv.10 미만 후보" },
                new()
                {
                    Title = "패킷", Value = $"{JsonInt(summary, "known_packets_decoded"):N0}",
                    Detail = $"저장 {packetPayloadsSaved:N0} · 실패 {JsonInt(summary, "decode_failures_saved"):N0}"
                }
            },
            Ships = new ObservableCollection<ShipRow>(ships.Select(ship => ToShipRow(ship, maps.Skills))),
            ShipLevelTable = maps.ShipLevels.ValueKind == JsonValueKind.Object ? maps.ShipLevels.Clone() : default,
            Equipment = new ObservableCollection<EquipmentRow>(equipment),
            EquipmentRecommendations = new ObservableCollection<EquipmentRow>(equipmentRecommendations),
            SpecialEquipment = new ObservableCollection<SpecialEquipmentRow>(specialEquipment),
            EquipmentSkins = new ObservableCollection<EquipmentSkinRow>(equipmentSkins),
            LevelPriorities = new ObservableCollection<LevelPriorityRow>(levelPriorities),
            GearGaps = new ObservableCollection<GearGapRow>(gearGaps),
            BattleRuns = new ObservableCollection<BattleRunRow>(battle.Runs),
            BattleShipRates = new ObservableCollection<BattleShipRateRow>(battle.ShipRates),
            FleetDamageRows = new ObservableCollection<FleetDamageRow>(fleetDamageRows),
            FleetCompositionGroups = new ObservableCollection<FleetCompositionGroupRow>(fleetCompositionGroups),
            FleetPlan = fleetPlan,
            MonthlyShop = BuildMonthlyShop(connection, maps, ships, fleetPlan),
            Meowfficers = BuildMeowfficers(connection, ships, maps, fleetPlan.Inputs.FarmFleetId),
            SkillPriorities = new ObservableCollection<SkillPriorityRow>(skillPriorities),
            SystemStatuses = new ObservableCollection<SystemStatusRow>(systems.Statuses),
            AcademyRooms = new ObservableCollection<AcademyRoomRow>(systems.AcademyRooms),
            ResearchQueue = new ObservableCollection<ResearchQueueRow>(systems.ResearchQueue),
            Commissions = new ObservableCollection<CommissionRow>(systems.Commissions),
            Builds = new ObservableCollection<BuildQueueRow>(systems.Builds),
            Resources = new ObservableCollection<ResourceRow>(resources),
            CommunityGearShortages = new ObservableCollection<CommunityGearShortageRow>(communityGearShortages),
            CleanupCandidates = new ObservableCollection<CleanupCandidateRow>(cleanup),
            PacketStats = new ObservableCollection<PacketStatRow>(packetStats),
            PacketInterpretationGaps = new ObservableCollection<PacketStatRow>(packetInterpretationGaps),
            DecodeFailures = new ObservableCollection<DecodeFailureRow>(decodeFailures),
            DatabasePath = databasePath,
            SnapshotSource = "current-sqlite",
            CurrentStateMode = currentStateInfo.Mode,
            CurrentStateLastDecodedRowId = currentStateInfo.LastDecodedRowId,
            CurrentStateLastStatus = currentStateInfo.LastStatus,
            CurrentStateLastArea = currentStateInfo.LastArea,
            CurrentStateLastDetail = currentStateInfo.LastDetail,
            CurrentStateLastError = currentStateInfo.LastError,
            PacketsSeen = JsonInt(summary, "packets_seen"),
            KnownPacketsSeen = JsonInt(summary, "known_packets_seen"),
            KnownPacketsDecoded = JsonInt(summary, "known_packets_decoded"),
            PacketPayloadsSaved = packetPayloadsSaved,
            DecodeFailuresSaved = JsonInt(summary, "decode_failures_saved") == 0
                ? decodeFailures.Sum(row => row.Count)
                : JsonInt(summary, "decode_failures_saved"),
            LiveUpdatedAt = JsonString(summary, "live_updated_at"),
            CaptureStatusLabel = captureStatus.Label,
            CaptureStatusDetail = captureStatus.Detail,
            IsCaptureRunning = captureStatus.Running,
            GeneratedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            LatestBattle = ReadLatestBattle(connection, ships, maps.Stages),
            CurrentChapterId = ReadCurrentChapterId(connection),
            Oil = ReadOilEstimate(connection, ships, maps),
            DormFood = ReadDormFood(connection),
            DockUsed = dockUsed,
            DockMax = dockMax,
            DockExemptCount = dockExempt,
            EquipmentBagCount = equipmentBagCount,
            EquipmentBagMax = equipBagMax,
            EquipmentEquippedCount = equipmentEquippedCount,
            BattleSummaryText = battleSummary,
            BattleSubText = $"{battle.DisplayRunCount:N0}전투 표시 · 캡처 {battle.CapturedRunCount:N0}회",
            FleetTechSummaryText =
                $"보유 {ships.Count(ship => !string.IsNullOrWhiteSpace(ship.FleetTechGetBonus)):N0}종 · 120 대기 {fleetTechPending:N0}종",
            CommunitySummaryText = $"평가 매칭 {communityMatched:N0}척 · REGAL 기준 {maps.RegalShipCount:N0}척",
            Status = $"{ships.Count:N0}척 / 장비 {equipment.Count:N0}종 / current SQLite 로드"
        };
    }
}
