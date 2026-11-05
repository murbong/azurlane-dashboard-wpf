using System.Globalization;
using System.Text.Json;
using AzurLaneDashboard.Core.Models;
using AzurLaneDashboard.Core.Services;
using AzurLaneDashboard.Data.Services;

/// <summary>The 코어 월간 ranking rules, and purchases and balance read back from captured packets.</summary>
internal static class MonthlyShopChecks
{
    public static void Run()
    {
        CheckTiers();
        CheckBudget();
        CheckPackets();
        Console.WriteLine("Monthly shop checks passed.");
    }

    private static void CheckTiers()
    {
        MonthlyShopCandidate Good(int id, MonthlyShopKind kind, int price, int limit = 1) =>
            new() { GoodId = id, Kind = kind, Name = $"good {id}", Price = price, Limit = limit };

        var shop = MonthlyShopRanker.Rank(
        [
            Good(1, MonthlyShopKind.Equipment, 800) with { GearSlots = 2, GearExample = "A 1번 칸 (META 보스전)" },
            Good(2, MonthlyShopKind.Equipment, 500) with { OwnedCopies = 3 },
            Good(3, MonthlyShopKind.CognitiveChip, 35, 10) with { ChipNeed = 5000, ChipStock = 800 },
            Good(4, MonthlyShopKind.CognitiveChip, 100, 5) with { ChipNeed = 0, ChipStock = 90 },
            Good(5, MonthlyShopKind.Ship, 1200) with { ShipRatingScore = 50, ShipRatingText = "S(EX)" },
            Good(6, MonthlyShopKind.Ship, 1200) with { FleetTechPoints = 8 },
            Good(7, MonthlyShopKind.Ship, 1000) with { FleetTechPoints = 16 },
            Good(8, MonthlyShopKind.Ship, 1200) with { ShipOwned = true, ShipRatingScore = 60 },
            Good(9, MonthlyShopKind.EquipmentBox, 100, 2),
            Good(10, MonthlyShopKind.EquipmentBox, 500),
            Good(11, MonthlyShopKind.Equipment, 500) with { GearSlots = 1, Bought = 1 },
            Good(12, MonthlyShopKind.Other, 300)
        ], 10, 100_000, hasBalance: true, hasPurchaseRecord: true);

        var order = shop.Rows.Select(row => row.Good.GoodId).ToList();
        Check(order.SequenceEqual([1, 3, 5, 4, 7, 6, 10, 9, 8, 12, 2, 11]),
            $"need, then suggested, then spare (chips, ships by fleet tech, boxes by price), then skip and done: {string.Join(",", order)}");
        var rows = shop.Rows.ToDictionary(row => row.Good.GoodId);
        Check(rows[1].Tier == 1 && rows[1].Reason.Contains("2칸") && rows[1].Reason.Contains("META 보스전"),
            "gear that improves recommended slots is needed and says where");
        Check(rows[2].Tier == 4 && rows[2].Reason.Contains("보유 3개"), "gear no fleet wants is not needed");
        Check(rows[3].Tier == 1 && rows[3].Reason.Contains("4,200"), "chips short of the queue's awakenings are needed");
        Check(rows[4].Tier == 3, "chips the queue does not lack are a spare buy");
        Check(rows[5].Tier == 2 && rows[6].Tier == 3 && rows[6].Reason.Contains("+8pt") && rows[8].Tier == 4,
            "a missing S ship is suggested, a weaker one is spare with its fleet tech, an owned one is skipped");
        Check(rows[11].Tier == 9 && rows[11].IsDone && rows[11].IsDimmed && rows[11].StockText == "1/1",
            "a good bought up to its limit is done even when the fleet wants it");
        Check(rows[12].Tier == 4 && rows[12].BudgetStatusText == "", "unknown goods are not ranked as buys");
        Check(rows[1].TierKind == "need" && rows[5].TierKind == "suggest" && rows[4].TierKind == "spare",
            "tier kinds drive the chip colour");
    }

    private static void CheckBudget()
    {
        var goods = new List<MonthlyShopCandidate>
        {
            new() { GoodId = 1, Kind = MonthlyShopKind.CognitiveChip, Price = 35, Limit = 10, ChipNeed = 1000 },
            new() { GoodId = 2, Kind = MonthlyShopKind.Ship, Price = 1200, Limit = 1, FleetTechPoints = 10 },
            new() { GoodId = 3, Kind = MonthlyShopKind.Ship, Price = 1000, Limit = 1, FleetTechPoints = 5 },
            new() { GoodId = 4, Kind = MonthlyShopKind.EquipmentBox, Price = 25, Limit = 5 },
            new() { GoodId = 5, Kind = MonthlyShopKind.Ship, Price = 1200, Limit = 1, ShipOwned = true }
        };
        var shop = MonthlyShopRanker.Rank(goods, 10, 1600, hasBalance: true, hasPurchaseRecord: false);
        var rows = shop.Rows.ToDictionary(row => row.Good.GoodId);
        Check(rows[1].CumulativeCost == 350 && rows[1].IsWithinBudget && rows[2].CumulativeCost == 1550 && rows[2].IsWithinBudget,
            "cumulative cost runs over the remaining buys in rank order");
        Check(!rows[3].IsWithinBudget && !rows[4].IsWithinBudget && rows[3].BudgetStatusText == "코어 부족",
            "once the running total passes the balance, later rows are out of budget");
        Check(rows[5].CumulativeCost == 0 && rows[5].BudgetStatusText == "" && rows[5].CumulativeText == "",
            "goods not worth buying stay out of the running total");
        Check(shop.BudgetRowCount == 2 && shop.PlannedCost == 350 + 1200 + 1000 + 125 && shop.BuyRowCount == 4,
            "the header counts what fits and what everything worth buying costs");
        Check(shop.BudgetText.Contains("2,675") && shop.BudgetText.Contains("2개") && shop.BalanceValueText == "1,600" &&
              shop.RecordText.Contains("구매 기록 없음"),
            $"the header says how far the balance goes ({shop.BudgetText})");

        var unknown = MonthlyShopRanker.Rank(goods, 10, 0, hasBalance: false, hasPurchaseRecord: false);
        Check(unknown.Rows.All(row => !row.IsWithinBudget && row.BudgetStatusText == "") &&
              unknown.BalanceValueText == "-" && unknown.BudgetText.Contains("모르면"),
            "without a balance the budget column stays empty instead of saying short");
    }

    private static void CheckPackets()
    {
        var month = DateTimeOffset.Now.ToOffset(TimeSpan.FromHours(9)).Month;
        using var map = JsonDocument.Parse(File.ReadAllText(Path.Combine(ProjectLocator.FindMapsDirectory(), "month_shop.json")));
        var goods = map.RootElement.GetProperty("goods");
        var chipGood = map.RootElement.GetProperty("months").GetProperty(month.ToString(CultureInfo.InvariantCulture))
            .GetProperty("core_shop_goods").EnumerateArray().Select(id => id.GetInt32())
            .First(id => goods.GetProperty(id.ToString(CultureInfo.InvariantCulture)).GetProperty("commodity_id").GetInt32() ==
                         AwakeningCost.UnitItemId);
        var chip = goods.GetProperty(chipGood.ToString(CultureInfo.InvariantCulture));
        var price = chip.GetProperty("price").GetInt32();
        var num = chip.GetProperty("num").GetInt32();

        using var db = new PacketDatabase();
        // Two buys before the login item list are already in its counts; three after it are not.
        db.Apply("SC", 16200, $$"""{"month":{{month}},"core_shop_list":[{"shop_id":{{chipGood}},"pay_count":1}]}""");
        db.Apply("CS", 16201, $$"""{"type":1,"id":{{chipGood}},"count":1}""");
        db.Apply("SC", 16202, """{"result":0}""");
        db.Apply("SC", 15001, """{"item_list":[{"id":59900,"count":5000},{"id":15008,"count":200}]}""");
        db.Apply("CS", 16201, $$"""{"type":1,"id":{{chipGood}},"count":3}""");
        db.Apply("SC", 16202, """{"result":0}""");
        db.Apply("CS", 16201, $$"""{"type":1,"id":{{chipGood}},"count":1}""");
        db.Apply("SC", 16202, """{"result":1}""");
        db.Apply("CS", 16201, $$"""{"type":2,"id":{{chipGood}},"count":1}""");
        db.Apply("SC", 16202, """{"result":0}""");

        var shop = new NativeSnapshotService().Load(db.Path).MonthlyShop;
        var row = shop.Rows.Single(item => item.Good.GoodId == chipGood);
        Check(shop.Month == month && shop.HasPurchaseRecord && row.Good.Bought == 5,
            $"the shop state plus accepted core-shop buys count, failed and other-shop buys do not ({row.Good.Bought})");
        Check(shop.HasBalance && shop.Balance == 5000 - 3L * price,
            $"buys after the item list come off the core data it reported ({shop.Balance})");
        Check(row.Good.ChipStock == 200 + 3L * num, $"chips bought after the item list are on hand ({row.Good.ChipStock})");

        using var old = new PacketDatabase();
        old.Apply("SC", 16200, $$"""{"month":{{month % 12 + 1}},"core_shop_list":[{"shop_id":{{chipGood}},"pay_count":10}]}""");
        var stale = new NativeSnapshotService().Load(old.Path).MonthlyShop;
        Check(!stale.HasPurchaseRecord && stale.Rows.Single(item => item.Good.GoodId == chipGood).Good.Bought == 0,
            "a shop state from another month does not count as this month's buys");
    }

    private static void Check(bool condition, string description)
    {
        if (!condition) throw new InvalidOperationException(description);
    }
}
