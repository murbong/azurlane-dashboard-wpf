using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using AzurLaneDashboard.Core.Models;
using Microsoft.Data.Sqlite;

namespace AzurLaneDashboard.Data.Services;

public sealed partial class NativeSnapshotService
{
    private const int RecentBattleResultLimit = 120;
    private static readonly HashSet<int> ShipListPacketIds = new() { 12001, 12010, 12026, 12030, 12042 };
    private static readonly HashSet<int> ShipChangePacketIds = new() { 12006, 12007, 12036, 12037 };
    private static readonly HashSet<int> FleetStatePacketIds = new() { 12101, 12102, 12103, 12104, 12105, 12106 };
    private static readonly HashSet<int> SpWeaponStatePacketIds = new() { 14200, 14201, 14202, 14210 };

    private static readonly HashSet<int> LatestStatePacketIds = new()
    {
        11003, 11004,
        12003, 12009, 12024, 12026, 12044, 12046,
        12101, 12103, 12105, 12106, 12201,
        13000, 13001, 13002, 13004, 13006, 13008, 13010, 13011,
        13102, 13104, 13105, 13108, 13110, 13112, 13201,
        12007, 12037, 14001, 14003, 14005, 14007, 14009, 14011, 14014, 14016, 14101,
        14200, 14202, 14204, 14206, 14208, 14210,
        15001, 15009,
        22001, 22010, 22012, 22013, 22015, 22202, 22204,
        63000, 63002, 63004, 63006, 63008, 63010, 63012, 63014, 63016,
        64000, 64002, 64004, 64006, 64008, 64010
    };

    private static readonly HashSet<int>
        SystemBasePacketIds = new() { 12003, 12024, 13002, 13010, 13011, 22001, 63000 };

    private static readonly HashSet<int> SystemReconstructionPacketIds = new()
    {
        12002, 12003, 12006, 12007, 12008, 12009, 12024, 12025, 12026, 12036, 12037, 12043, 12044, 12045, 12046,
        12101, 12102, 12103, 12104, 12105, 12106,
        13000, 13001, 13002, 13003, 13004, 13005, 13006, 13007, 13008, 13009, 13010, 13011,
        13101, 13102, 13103, 13104, 13105, 13106, 13107, 13108, 13109, 13110, 13112,
        14001, 14002, 14003, 14004, 14005, 14006, 14007, 14008, 14009, 14010, 14011, 14013, 14014, 14015, 14016, 14101,
        14200, 14201, 14202, 14203, 14204, 14205, 14206, 14207, 14208, 14209, 14210,
        22001, 22009, 22010, 22011, 22012, 22013, 22014, 22015, 22201, 22202, 22203, 22204,
        63000, 63001, 63002, 63003, 63004, 63005, 63006, 63007, 63008, 63009, 63010, 63011, 63012, 63013, 63014, 63015,
        63016,
        64000, 64001, 64002, 64003, 64004, 64005, 64006, 64007, 64008, 64009, 64010
    };

    private static readonly Regex ShipStatsEntryRegex =
        new(@"ship_data_statistics\[(\d+)\]\s*=", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex ShipTemplateEntryRegex =
        new(@"ship_data_template\[(\d+)\]\s*=", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex ShipStrengthenEntryRegex =
        new(@"ship_data_strengthen\[(\d+)\]\s*=", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex ShipBlueprintEntryRegex =
        new(@"ship_data_blueprint\[(\d+)\]\s*=", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex ShipStrengthenBlueprintEntryRegex =
        new(@"ship_strengthen_blueprint\[(\d+)\]\s*=", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex EquipmentStatisticsEntryRegex =
        new(@"equip_data_statistics\[(\d+)\]\s*=", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex TransformDataTemplateEntryRegex =
        new(@"transform_data_template\[(\d+)\]\s*=", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex LuaNumberRegex =
        new(@"-?\d+(?:\.\d+)?", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public DashboardSnapshot Load(string databasePath, CancellationToken cancellationToken = default)
    {
        databasePath = string.IsNullOrWhiteSpace(databasePath)
            ? ProjectLocator.DefaultDatabasePath()
            : Path.GetFullPath(databasePath);
        if (!File.Exists(databasePath))
        {
            return new DashboardSnapshot
            {
                DatabasePath = databasePath,
                SnapshotSource = "native-missing",
                Status = "SQLite 데이터베이스를 찾지 못했습니다."
            };
        }

        var mapsDirectory = ProjectLocator.FindMapsDirectory();
        var signature = $"{SnapshotSignature(databasePath)}|{MapFileStore.Signature(mapsDirectory)}";
        if (TryGetCachedSnapshot(databasePath, signature, out var cachedSnapshot))
        {
            return cachedSnapshot;
        }

        cancellationToken.ThrowIfCancellationRequested();

        // Private cache: a read-only connection in a shared cache (kept open by the pool) makes the capture
        // store that joins it later fail with "attempt to write a readonly database". WAL lets both run.
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadOnly
        }.ToString());
        connection.Open();
        ConfigureReadConnection(connection);

        var projectRoot = ProjectLocator.FindProjectRoot();
        var summary = ReadSummary(connection);
        var currentStateInfo = ReadCurrentStateInfo(connection, summary);
        var maps = ReadMaps(mapsDirectory);
        var auction = BuildAuctionDashboard(connection, maps.AuctionCollection);
        var (packetStats, packetInterpretationGaps) = ReadPacketStats(connection);
        var decodeFailures = ReadDecodeFailures(connection);
        var packetPayloadsSaved = JsonInt(summary, "packet_payloads_saved");
        if (packetPayloadsSaved == 0 && TableExists(connection, "packet_payloads"))
        {
            packetPayloadsSaved = CountRows(connection, "packet_payloads");
        }

        cancellationToken.ThrowIfCancellationRequested();

        if (CurrentStateAvailable(connection))
        {
            var currentSnapshot = BuildSnapshotFromCurrentState(
                connection,
                databasePath,
                summary,
                maps,
                packetStats,
                packetInterpretationGaps,
                decodeFailures,
                packetPayloadsSaved,
                currentStateInfo,
                projectRoot,
                auction);
            CacheSnapshot(databasePath, signature, currentSnapshot);
            return currentSnapshot;
        }

        var packets = ReadPackets(connection, cancellationToken);
        var fullShips = CollectFullShips(packets);
        var specialLookup = BuildEquippedSpecialLookup(packets, maps.SpWeapons, projectRoot);
        var ships = BuildShips(fullShips, specialLookup, maps, projectRoot);
        ApplyBattleExpUpdates(ships, packets, maps.ShipLevels);
        AttachCommunityRatings(ships, maps.RegalShips);
        ApplyFleetTechUnlockState(ships);
        ApplyFleetTechReloadAdditions(ships, maps.FleetTech);
        ApplyFleetTechNationReloadAdditions(connection, ships, maps.FleetTechNation, packets);

        var equipment = BuildEquipment(LatestBagEquipment(packets), ships, maps.Equipment, projectRoot);
        var specialEquipment = BuildSpecialEquipmentInventory(packets, maps.SpWeapons, projectRoot, ships);
        var equipmentSkins = BuildEquipmentSkinInventory(packets, maps.EquipmentSkins, projectRoot);
        AttachCommunityRecommendations(ships, maps, equipment, specialEquipment, projectRoot);
        var resources = BuildResources(packets, summary, maps.Resources);
        var commander = BuildCommanderProfile(packets);
        var battle = BuildBattleTraining(packets, ships, maps.ShipLevels, maps.Stages);
        var levelPriorities = BuildLevelPriorities(ships, maps.ShipLevels);
        var skillPriorities = BuildSkillPriorities(ships, maps.Skills, projectRoot);
        var fleetLayout = BuildFleetLayoutFromPackets(packets);
        if (!fleetLayout.HasFleetData)
        {
            fleetLayout = ReadCurrentFleetLayout(connection);
        }
        ApplyFleetLabels(ships, fleetLayout);
        var gearGaps = BuildGearGaps(ships);
        var equipmentRecommendations = BuildEquipmentRecommendations(equipment);
        var communityGearShortages = BuildCommunityGearShortages(ships);
        var systems = BuildSystems(packets, summary, ships, maps.Skills);
        var cleanup = BuildCleanupCandidates(ships);
        var fleetDamageRows = BuildFleetDamageRows(ships, maps.FireRateEquipment, maps.Equipment, maps.Skills, fleetLayout);
        var fleetCompositionGroups =
            BuildFleetCompositionGroups(ships, fleetLayout, fleetDamageRows, maps.FireRateEquipment, maps.Equipment);

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
        var snapshot = new DashboardSnapshot
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
            Opsi = BuildOpsi(connection, maps),
            PersonalTasks = BuildPersonalTasks(connection, ships, maps),
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
            SnapshotSource = "native-sqlite",
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
            Status = $"{ships.Count:N0}척 / 장비 {equipment.Count:N0}종 / 네이티브 SQLite 로드"
        };

        CacheSnapshot(databasePath, signature, snapshot);
        return snapshot;
    }

    private static CaptureStatus BuildCaptureStatus(JsonElement summary)
    {
        var live = JsonString(summary, "live_updated_at");
        var epoch = ParseSummaryEpoch(live);
        if (epoch is null)
        {
            return new CaptureStatus(false, "OFF", "실시간 캡처 수신 기록 없음");
        }

        var age = NowEpoch() - epoch.Value;
        var running = age >= 0 && age <= 90;
        return new CaptureStatus(running, running ? "ON" : "OFF",
            running ? $"마지막 수신 {AgeText(age)} 전" : $"실시간 캡처 꺼짐 · 마지막 수신 {AgeText(age)} 전");
    }

    private static int CompareTuple(int levelA, int rarityA, string nameA, int levelB, int rarityB, string nameB)
    {
        var level = levelA.CompareTo(levelB);
        if (level != 0) return level;
        var rarity = rarityA.CompareTo(rarityB);
        if (rarity != 0) return rarity;
        return string.Compare(nameA, nameB, StringComparison.CurrentCulture);
    }
}
