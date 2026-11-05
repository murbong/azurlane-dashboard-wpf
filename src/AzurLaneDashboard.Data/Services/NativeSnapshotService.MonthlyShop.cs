using System.Globalization;
using System.Text.Json;
using AzurLaneDashboard.Core.Models;
using AzurLaneDashboard.Core.Services;
using Microsoft.Data.Sqlite;

namespace AzurLaneDashboard.Data.Services;

public sealed partial class NativeSnapshotService
{
    private const int CoreDataItemId = 59900;
    private const int ShamShopType = 1;
    // 성정 유닛 / 성정 유닛II; equipment boxes (크록히드 장비상자 T3–T5 by type) sit in 30011–30049.
    private static readonly int[] CognitiveChipItems = [AwakeningCost.UnitItemId, AwakeningCost.UnitIIItemId];

    /// <summary>
    /// This month's 코어 월간 goods ranked for the account. Purchases come from SC 16200 (the shop state the
    /// client receives) plus any CS 16201 core-shop buy the server accepted after it.
    /// </summary>
    private static MonthlyShopDashboard BuildMonthlyShop(
        SqliteConnection connection, SnapshotMaps maps, List<ShipState> ships, FleetPlanDashboard fleetPlan)
    {
        if (maps.MonthShop.ValueKind != JsonValueKind.Object)
        {
            return new MonthlyShopDashboard();
        }

        var purchases = ReadCoreShopPurchases(connection);
        var month = DateTimeOffset.Now.ToOffset(TimeSpan.FromHours(9)).Month;
        // A record from an earlier month was reset by the server on the 1st.
        var hasRecord = purchases.Month == month;
        var bought = hasRecord ? purchases.Bought : new Dictionary<int, int>();

        var goodsMap = Child(maps.MonthShop, "goods");
        // Item counts refresh only with the login item list (SC 15001), so buys after it still owe their price
        // and have not added their items yet.
        long spent = 0;
        var gained = new Dictionary<int, long>();
        foreach (var (boughtId, count) in purchases.SinceItemSync)
        {
            var good = Child(goodsMap, boughtId.ToString(CultureInfo.InvariantCulture));
            if (good.ValueKind != JsonValueKind.Object) continue;
            if (JsonInt(good, "currency_id") == CoreDataItemId) spent += (long)JsonInt(good, "price") * count;
            if (JsonInt(good, "type") == 2)
            {
                var item = JsonInt(good, "commodity_id");
                gained[item] = gained.GetValueOrDefault(item) + (long)Math.Max(1, JsonInt(good, "num")) * count;
            }
        }

        long Stock(int itemId) => ReadItemCount(connection, itemId) + gained.GetValueOrDefault(itemId);
        var ids = Array(Child(Child(maps.MonthShop, "months"), month.ToString(CultureInfo.InvariantCulture)), "core_shop_goods")
            .Select(IntValue)
            .Where(id => id > 0)
            .ToList();
        var inputs = fleetPlan.Inputs;
        var demand = BuildGearDemand(fleetPlan, maps);
        // Retired ships still count: the collection (SC 17001) keeps every group ever obtained.
        var owned = ships.Select(ship => ship.GroupId).Where(group => group > 0).ToHashSet();
        owned.UnionWith(ReadCollectionGroups(connection));
        var regalByGroup = maps.RegalShips.ValueKind == JsonValueKind.Array
            ? maps.RegalShips.EnumerateArray().Where(row => row.ValueKind == JsonValueKind.Object)
                .GroupBy(row => JsonInt(row, "group"))
                .ToDictionary(group => group.Key, group => group.ToList())
            : new Dictionary<int, List<JsonElement>>();
        var chipStock = Stock(AwakeningCost.UnitItemId);
        var chipNeed = AwakeningUnitsNeeded(inputs.LevelingShips, new ShipLevelCaps(maps.ShipLevels));

        var candidates = new List<MonthlyShopCandidate>();
        foreach (var id in ids)
        {
            var good = Child(goodsMap, id.ToString(CultureInfo.InvariantCulture));
            if (good.ValueKind != JsonValueKind.Object) continue;
            var type = JsonInt(good, "type");
            var commodity = JsonInt(good, "commodity_id");
            var candidate = new MonthlyShopCandidate
            {
                GoodId = id,
                CommodityId = commodity,
                Num = Math.Max(1, JsonInt(good, "num")),
                Price = JsonInt(good, "price"),
                Limit = JsonInt(good, "limit"),
                Order = JsonInt(good, "order"),
                Bought = bought.GetValueOrDefault(id)
            };
            candidates.Add(type switch
            {
                3 => EquipmentCandidate(candidate, maps, inputs, demand),
                4 => ShipCandidate(candidate, maps, owned, regalByGroup),
                2 when CognitiveChipItems.Contains(commodity) => ItemCandidate(candidate, good,
                    MonthlyShopKind.CognitiveChip) with
                {
                    ChipNeed = commodity == AwakeningCost.UnitItemId ? chipNeed : 0,
                    ChipStock = commodity == AwakeningCost.UnitItemId ? chipStock : Stock(commodity)
                },
                2 when commodity is >= 30011 and <= 30049 => ItemCandidate(candidate, good, MonthlyShopKind.EquipmentBox),
                _ => ItemCandidate(candidate, good, MonthlyShopKind.Other)
            });
        }

        var balance = Math.Max(0, ReadItemCount(connection, CoreDataItemId) - spent);
        return MonthlyShopRanker.Rank(candidates, month, balance, TableExists(connection, "current_items"), hasRecord);
    }

    private static MonthlyShopCandidate EquipmentCandidate(
        MonthlyShopCandidate candidate, SnapshotMaps maps, FleetPlannerInputs inputs,
        IReadOnlyDictionary<int, (int Slots, string Example)> demand)
    {
        var info = MapObject(maps.Equipment, candidate.CommodityId);
        var baseId = FirstPositive(JsonInt(info, "base"), candidate.CommodityId);
        var spare = inputs.GearInventory.Spare.TryGetValue(baseId, out var copies) ? copies.Count : 0;
        var equipped = inputs.GearInventory.Equipped.Count(holder => holder.BaseId == baseId);
        var (slots, example) = demand.GetValueOrDefault(baseId);
        return candidate with
        {
            Kind = MonthlyShopKind.Equipment,
            Name = FirstNonEmpty(JsonString(info, "name"), $"equip:{candidate.CommodityId}"),
            IconUrl = JsonString(info, "icon_url"),
            Rarity = RarityTier(JsonInt(info, "rarity")),
            GearSlots = Math.Min(slots, candidate.Remaining),
            GearExample = example ?? "",
            OwnedCopies = spare + equipped
        };
    }

    private static MonthlyShopCandidate ShipCandidate(
        MonthlyShopCandidate candidate, SnapshotMaps maps, IReadOnlySet<int> owned,
        IReadOnlyDictionary<int, List<JsonElement>> regalByGroup)
    {
        var info = MapObject(maps.Ships, candidate.CommodityId);
        var group = FirstPositive(JsonInt(info, "group_type"), candidate.CommodityId / 10);
        var skin = MapObject(maps.Skins, JsonInt(info, "skin_id"));
        var best = (regalByGroup.GetValueOrDefault(group) ?? [])
            .Select(row => BestRegalRating(Child(row, "ratings")))
            .OrderByDescending(rating => rating.Score)
            .FirstOrDefault();
        return candidate with
        {
            Kind = MonthlyShopKind.Ship,
            Name = FirstNonEmpty(JsonString(info, "name"), $"ship:{candidate.CommodityId}").Trim(),
            IconUrl = JsonString(skin, "icon_url"),
            Rarity = RarityTier(JsonInt(info, "rarity")),
            ShipOwned = owned.Contains(group),
            ShipRatingScore = FleetPlanRating.Score(best.Rating),
            ShipRatingText = string.IsNullOrWhiteSpace(best.Rating) ? "" : $"{best.Rating}({best.Context})",
            FleetTechPoints = JsonInt(MapObject(maps.FleetTech, group), "pt_get")
        };
    }

    private static MonthlyShopCandidate ItemCandidate(MonthlyShopCandidate candidate, JsonElement good, MonthlyShopKind kind) =>
        candidate with
        {
            Kind = kind,
            Name = FirstNonEmpty(JsonString(good, "name"), $"item:{candidate.CommodityId}"),
            IconUrl = JsonString(good, "icon_url"),
            // Item rarity sits one step below the ship and equipment scale, as on the resources tab.
            Rarity = RarityHelper.Normalize((Math.Max(1, JsonInt(good, "rarity")) + 1).ToString(CultureInfo.InvariantCulture))
        };

    /// <summary>
    /// For each equipment piece, the slots of the recommended fleets (every planner content) where it ranks
    /// above what the fleet-wide allocation can put there today, counting gear moved from other ships.
    /// </summary>
    private static Dictionary<int, (int Slots, string Example)> BuildGearDemand(FleetPlanDashboard plan, SnapshotMaps maps)
    {
        var demand = new Dictionary<int, (int Slots, string Example)>();
        var inputs = plan.Inputs;
        if (inputs.GearMap is null) return demand;

        var metaUsecase = MetaUsecase(inputs);
        var counted = new HashSet<(int BaseId, string Uid, int Slot)>();
        foreach (var content in plan.Contents)
        {
            var usecase = content.Key switch
            {
                "w1516" or "w14" => "C-13+",
                "meta" => metaUsecase,
                _ => "H"
            };
            foreach (var fleet in content.Fleets)
            {
                var ships = fleet.MainSlots.Select((slot, index) => (slot.ShipUid, Flagship: index == 0))
                    .Concat(fleet.VanguardSlots.Select(slot => (slot.ShipUid, Flagship: false)))
                    .Where(item => inputs.GearShips.ContainsKey(item.ShipUid))
                    .Select(item => inputs.GearShips[item.ShipUid] with { IsFlagship = item.Flagship })
                    .ToList();
                var plans = GearAllocator.Allocate(ships, inputs.GearMap, usecase, inputs.GearInventory, GearSourceMode.Move);
                foreach (var shipPlan in plans)
                {
                    var ranks = inputs.GearMap.Slots(shipPlan.Ship.RegalGearId, shipPlan.Usecase);
                    foreach (var slot in shipPlan.Slots)
                    {
                        if (slot.Slot > ranks.Count) continue;
                        var rows = ranks[slot.Slot - 1];
                        // Pieces ranked strictly above what the slot ends up with (or anything, if it stays empty).
                        var better = slot.Action == GearSlotAction.Missing || slot.Rank == 0
                            ? rows.Count
                            : slot.Rank - 1;
                        for (var rank = 0; rank < better && rank < rows.Count; rank++)
                        {
                            foreach (var baseId in rows[rank])
                            {
                                if (!counted.Add((baseId, shipPlan.Ship.Uid, slot.Slot))) continue;
                                var (count, example) = demand.GetValueOrDefault(baseId);
                                demand[baseId] = (count + 1, example ?? $"{shipPlan.Ship.Name} {slot.Slot}번 칸 ({content.Label})");
                            }
                        }
                    }
                }
            }
        }

        return demand;
    }

    private static string MetaUsecase(FleetPlannerInputs inputs)
    {
        var current = MetaBossCatalog.Current(inputs.MetaBosses, MetaBossCatalog.ServerTime(DateTimeOffset.Now));
        if (current is null) return "H";
        var guide = inputs.MetaBossGuides.GetValueOrDefault(current.Id);
        return MetaBossCatalog.GearUsecase(MetaBossCatalog.Armors(current, guide is { Verified: true } ? guide : null));
    }

    /// <summary>
    /// 성정 유닛 the leveling queue's next stretch needs: every awakening from each queued ship's next cap up
    /// to Lv.120, for ships the planner wants (content candidates, META guide ships, the farming fleet).
    /// </summary>
    private static long AwakeningUnitsNeeded(IReadOnlyList<LevelingShip> leveling, ShipLevelCaps caps)
    {
        long total = 0;
        foreach (var ship in leveling.Where(ship => ship.IsLocked && ship.Level < LevelingPlanBuilder.ReadyLevel &&
                                                    (ship.ContentReach > 0 || ship.IsMetaGuideShip || ship.FarmFleetId > 0)))
        {
            var waiting = caps.IsWaitingForAwakening(ship.Level, ship.Experience, ship.Rarity);
            foreach (var cap in caps.Caps.Where(cap => cap is >= 100 and < LevelingPlanBuilder.ReadyLevel))
            {
                if (cap < ship.Level || (cap == ship.Level && !waiting)) continue;
                total += caps.Awakening(cap, ship.Rarity)?.Units ?? 0;
            }
        }

        return total;
    }

    private static HashSet<int> ReadCollectionGroups(SqliteConnection connection)
    {
        var groups = new HashSet<int>();
        if (!TableExists(connection, "decoded_packets")) return groups;
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT json FROM decoded_packets WHERE packet_id = 17001 ORDER BY rowid DESC LIMIT 1";
        if (command.ExecuteScalar() is not string text) return groups;
        try
        {
            using var document = JsonDocument.Parse(text);
            foreach (var row in Array(document.RootElement, "ship_info_list"))
            {
                var group = JsonInt(row, "id");
                if (group > 0) groups.Add(group);
            }
        }
        catch (JsonException)
        {
        }

        return groups;
    }

    private static long ReadItemCount(SqliteConnection connection, int itemId)
    {
        if (!TableExists(connection, "current_items")) return 0;
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT count FROM current_items WHERE item_id = $id";
        command.Parameters.AddWithValue("$id", itemId);
        return command.ExecuteScalar() is long count ? count : 0;
    }

    private sealed record CoreShopPurchases(
        int Month, Dictionary<int, int> Bought, IReadOnlyList<(int Id, int Count)> SinceItemSync);

    private static CoreShopPurchases ReadCoreShopPurchases(SqliteConnection connection)
    {
        var bought = new Dictionary<int, int>();
        var sinceItemSync = new List<(int Id, int Count)>();
        if (!TableExists(connection, "decoded_packets")) return new CoreShopPurchases(0, bought, sinceItemSync);

        using var command = connection.CreateCommand();
        // The login item list only marks where item counts were last refreshed; its body is not needed.
        command.CommandText = """
                              SELECT packet_id, CASE WHEN packet_id = 15001 THEN '{}' ELSE json END
                              FROM decoded_packets
                              WHERE packet_id IN (15001, 16200, 16201, 16202)
                              ORDER BY rowid
                              """;
        using var reader = command.ExecuteReader();
        var month = 0;
        (int Id, int Count)? pending = null;
        while (reader.Read())
        {
            if (reader.GetInt32(0) == 15001)
            {
                sinceItemSync.Clear();
                continue;
            }

            JsonElement json;
            try
            {
                using var document = JsonDocument.Parse(reader.GetString(1));
                json = document.RootElement.Clone();
            }
            catch (JsonException)
            {
                continue;
            }

            switch (reader.GetInt32(0))
            {
                case 16200:
                    // The full state replaces whatever was counted before it.
                    month = JsonInt(json, "month");
                    bought.Clear();
                    foreach (var row in Array(json, "core_shop_list"))
                        bought[JsonInt(row, "shop_id")] = JsonInt(row, "pay_count");
                    pending = null;
                    break;
                case 16201:
                    pending = JsonInt(json, "type") == ShamShopType
                        ? (JsonInt(json, "id"), Math.Max(1, JsonInt(json, "count")))
                        : null;
                    break;
                case 16202 when pending is { } buy:
                    if (JsonInt(json, "result") == 0)
                    {
                        bought[buy.Id] = bought.GetValueOrDefault(buy.Id) + buy.Count;
                        sinceItemSync.Add(buy);
                    }

                    pending = null;
                    break;
            }
        }

        return new CoreShopPurchases(month, bought, sinceItemSync);
    }
}
