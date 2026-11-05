using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using AzurLaneDashboard.App.Views;
using AzurLaneDashboard.Core.Models;
using AzurLaneDashboard.Core.Services;

/// <summary>The 월간 상점 page: tier groups in rank order, the budget column, and the empty state.</summary>
internal static class MonthlyShopPageChecks
{
    public static void Run(string output, Action<FrameworkElement, int, int, string> render)
    {
        var shop = MonthlyShopRanker.Rank(
        [
            new() { GoodId = 1, Kind = MonthlyShopKind.Equipment, Name = "초중량탄 (SHS)", Rarity = "SSR", Price = 800, Limit = 1,
                    GearSlots = 3, GearExample = "괴츠 폰 베를리힝겐 5번 칸 (META 보스전)" },
            new() { GoodId = 2, Kind = MonthlyShopKind.CognitiveChip, Name = "성정 유닛", Rarity = "SR", Num = 100, Price = 35,
                    Limit = 10, Bought = 4, ChipNeed = 3000, ChipStock = 1200 },
            new() { GoodId = 3, Kind = MonthlyShopKind.Ship, Name = "에밀 베르탱", Rarity = "SR", Price = 1200, Limit = 1,
                    FleetTechPoints = 12, ShipRatingText = "C(W14 Mob)" },
            new() { GoodId = 4, Kind = MonthlyShopKind.Ship, Name = "아주 긴 이름을 가진 함선 · 개조형 시험 사양", Rarity = "SSR",
                    Price = 1500, Limit = 1, FleetTechPoints = 16 },
            new() { GoodId = 5, Kind = MonthlyShopKind.EquipmentBox, Name = "크록히드 장비상자 T5", Rarity = "SSR", Price = 500, Limit = 1 },
            new() { GoodId = 6, Kind = MonthlyShopKind.Equipment, Name = "VC 장갑 강판", Rarity = "SSR", Price = 500, Limit = 1, OwnedCopies = 1 },
            new() { GoodId = 7, Kind = MonthlyShopKind.Ship, Name = "인디펜던스", Rarity = "SR", Price = 1200, Limit = 1, ShipOwned = true },
            new() { GoodId = 8, Kind = MonthlyShopKind.CognitiveChip, Name = "성정 유닛II", Rarity = "SR", Num = 10, Price = 100,
                    Limit = 5, Bought = 5 }
        ], 10, 3000, hasBalance: true, hasPurchaseRecord: true);
        var view = new MonthlyShopView { DataContext = shop, FontFamily = new FontFamily("Segoe UI, Malgun Gothic") };
        render(view, 1320, 760, Path.Combine(output, "monthly-shop.png"));
        var grid = Descendants(view).OfType<DataGrid>().Single();
        Check(grid.Visibility == Visibility.Visible && grid.Items.Count == shop.Rows.Count,
            "the page lists every good of the month");
        var groups = grid.Items.Groups!.Cast<CollectionViewGroup>().Select(group => group.Name).ToList();
        Check(groups.SequenceEqual(["필요", "여유 있으면", "필요 없음", "구매 완료"]),
            $"goods are grouped by tier in rank order ({string.Join(", ", groups)})");
        Check(shop.Rows.Where(row => row.IsBuy).Select(row => row.BudgetStatusText)
                  .SequenceEqual(["살 수 있음", "살 수 있음", "살 수 있음", "코어 부족", "코어 부족"]),
            "the budget column turns short once the running total passes the balance");
        render(view, 980, 640, Path.Combine(output, "monthly-shop-compact.png"));

        var empty = new MonthlyShopView { DataContext = new MonthlyShopDashboard(), FontFamily = view.FontFamily };
        render(empty, 980, 640, Path.Combine(output, "monthly-shop-empty.png"));
        Check(Descendants(empty).OfType<DataGrid>().Single().Visibility == Visibility.Collapsed,
            "with no goods the page shows the empty state instead of an empty grid");
        Console.WriteLine("Monthly shop page checks passed.");
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            yield return child;
            foreach (var nested in Descendants(child)) yield return nested;
        }
    }

    private static void Check(bool condition, string description)
    {
        if (!condition) throw new InvalidOperationException(description);
    }
}
