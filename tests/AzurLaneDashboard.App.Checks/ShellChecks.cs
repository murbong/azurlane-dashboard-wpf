using System.Collections.ObjectModel;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using AzurLaneDashboard.App.ViewModels;
using AzurLaneDashboard.App.Views;
using AzurLaneDashboard.Core.Models;
using AzurLaneDashboard.Data.Services;

internal static class ShellChecks
{
    public static void Run(string output, Action<FrameworkElement, int, int, string> render)
    {
        var settingsPath = Path.Combine(Path.GetTempPath(), $"azurlane-shell-{Guid.NewGuid():N}.json");
        var settings = new AppSettingsService(settingsPath);
        settings.Save(new AppSettings { IsAutoRefreshEnabled = false, IsDarkMode = true });
        try
        {
            using var model = new MainWindowViewModel(settings, startBackgroundServices: false);
            var shell = new DashboardShellView
            {
                DataContext = model, FontFamily = new FontFamily("Segoe UI, Malgun Gothic")
            };
            model.SelectedMainTabIndex = 0;
            render(shell, 1080, 640, Path.Combine(output, "shell-empty.png"));
            CheckMacroMenu(model);
            using var levels = JsonDocument.Parse(JsonSerializer.Serialize(
                Enumerable.Range(1, 124).ToDictionary(level => level.ToString(), _ => new { exp = 50000 })));
            var ships = new ObservableCollection<ShipRow>(Enumerable.Range(1, 30).Select(index => new ShipRow
            {
                ShipUid = index.ToString(), Name = new[] { "엔터프라이즈", "벨파스트", "뉴저지", "시마카제", "유니콘" }[(index - 1) % 5],
                Rarity = "SSR", RarityTier = "SSR", Level = 118 - index % 10, HasFullState = true, Locked = true,
                ShipTypeLabel = "항공모함", NationLabel = "이글 유니온", MoodValue = 120,
                RecentBattleExpAvg = 2200, EquipmentSlots = [],
                SkillDetails = [new() { Index = 1, Name = "전투 지원", Level = 6, MaxLevel = 10 }]
            }));
            var snapshot = new DashboardSnapshot
            {
                DatabasePath = Path.Combine(Path.GetTempPath(), "shell-fixture.sqlite"), Ships = ships,
                IsCaptureRunning = true,
                ShipLevelTable = levels.RootElement.Clone(), Status = "테스트 데이터 · 30척 확인", DockUsed = 30, DockMax = 150,
                BattleSummaryText = "최근 전투 12회 · 함선별 경험치 분석",
                Kpis = [new() { Title = "함선", Value = "30 / 150", Detail = "레벨 100 이상 30척" },
                    new() { Title = "장비", Value = "148", Detail = "보유 장비" },
                    new() { Title = "전투", Value = "12", Detail = "최근 수집 기록" }],
                SystemStatuses = [new() { Name = "전술 교실", StatusLabel = "진행 중", Detail = "스킬 훈련 2개" },
                    new() { Name = "의뢰", StatusLabel = "확인 필요", Detail = "게임에서 최신 상태를 확인하세요" }],
                LevelPriorities = new(ships.Select(ship => new LevelPriorityRow
                    { ShipUid = ship.ShipUid, DisplayName = ship.Name, Level = ship.Level, LevelsTo120 = 120 - ship.Level, Rarity = "SSR" })),
                BattleRuns = [new() { StageLabel = "12-4", ShipExpAvg = 2200, MvpName = "엔터프라이즈" }],
                Equipment = [new() { Name = "테스트 장비", Rarity = "SSR", RarityTier = "SSR", Count = 3 }],
                FleetPlan = FleetPlanFixture(),
                Meowfficers = MeowfficerFixture()
            };
            typeof(MainWindowViewModel).GetMethod("ApplySnapshot", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(model, [snapshot]);
            typeof(MainWindowViewModel).GetMethod("NotifyCounts", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(model, null);
            Check(!model.IsCaptureRunning && model.StartCaptureCommand.CanExecute(null),
                "historical capture records must not block starting a real capture after restart");
            model.GrowthPlanner.SelectedShip = model.GrowthPlanner.AvailableShips[0];
            model.GrowthPlanner.SaveGoalCommand.Execute(null);
            Check(!model.GrowthPlanner.IsEditorExpanded, "saving folds the editor to show plans");
            for (var index = 0; index < 12; index++)
            {
                model.NavigateCommand.Execute(index.ToString());
                Check(model.SelectedDestination.Index == index, "navigation and content stay in sync");
                render(shell, 1320, 800, Path.Combine(output, $"shell-page-{index}.png"));
                Check(model.SelectedMainTabIndex == index, "render must not navigate to another page");
                var toolbar = (TopToolbarView)shell.FindName("Toolbar");
                var tools = (System.Windows.Controls.Border)toolbar.FindName("InventoryTools");
                Check(tools.Visibility == (model.IsToolbarSearchVisible ? Visibility.Visible : Visibility.Collapsed),
                    $"page {index}: toolbar search only on pages without their own tabs");
                var shownBars = Descendants(shell).OfType<InventorySearchBar>().Where(IsShown).ToList();
                Check(shownBars.Count == (model.IsInventorySearchVisible ? 1 : 0),
                    $"page {index}: exactly one search bar when the page lists items");
                if (model.IsToolbarSearchVisible)
                {
                    // The search row is a single 34px line; anything shorter means it was collapsed or clipped.
                    Check(tools.ActualHeight >= 34, $"page {index}: search tools receive layout space");
                    Check(tools.TranslatePoint(new Point(), toolbar).Y + tools.ActualHeight <= toolbar.ActualHeight,
                        $"page {index}: search tools fit within toolbar instead of overlapping content");
                }
            }
            CheckTabStripStays(model, shell, output, render, 1, "LevelSections", value => model.SelectedLevelSectionIndex = value);
            CheckTabStripStays(model, shell, output, render, 2, "EquipmentSections", value => model.SelectedEquipmentSectionIndex = value);
            CheckFleetPlanner(model, shell, output, render);
            CheckMeowfficers(model, shell, output, render);
            model.SelectedMainTabIndex = 6;
            model.Query = "존재하지 않는 함선";
            model.SelectedRarityFilter = "UR";
            render(shell, 1080, 640, Path.Combine(output, "shell-filtered.png"));
            Check(model.HasActiveFilters && model.ShowNoSearchResults, "filtered empty state explained");
            model.ResetFiltersCommand.Execute(null);
            Check(model.Query == "" && model.SelectedRarityFilter == "전체" && model.FilteredShipCount == 30,
                "reset immediately restores visible ships");
            render(shell, 1080, 640, Path.Combine(output, "shell-dock-compact.png"));
            model.SelectedMainTabIndex = 0;
            model.IsDarkMode = false;
            render(shell, 1320, 800, Path.Combine(output, "shell-home-light.png"));
            model.SelectedMainTabIndex = 10;
            render(shell, 1440, 900, Path.Combine(output, "fleet-planner-light.png"));
            model.SelectedMainTabIndex = 1;
            model.SelectedLevelSectionIndex = MainWindowViewModel.GrowthQueueSection;
            Check(model.IsInventorySearchVisible && model.IsShipFilterVisible && !model.IsShipSortVisible,
                "the queue takes the ship filters but keeps its own order");
            model.SelectedLevelSectionIndex = MainWindowViewModel.GrowthGoalsSection;
            Check(!model.IsInventorySearchVisible, "goal page hides unrelated inventory search");
            model.SelectedLevelSectionIndex = 3;
            Check(model.IsInventorySearchVisible && model.IsShipSortVisible, "gear gaps keep filters and sort");
            model.SelectedMainTabIndex = 9;
            Check(!model.IsInventorySearchVisible, "settings hides unrelated inventory search");
            model.SelectedMainTabIndex = 2;
            model.SelectedEquipmentTypeFilter = "함포";
            Check(model.IsEquipmentFilterVisible && model.HasActiveFilters, "owned equipment exposes type filter");
            model.SelectedEquipmentSectionIndex = 2;
            Check(model.IsInventorySearchVisible && !model.IsEquipmentFilterVisible && !model.HasActiveFilters,
                "special equipment exposes search without unrelated type filters");
            model.SelectedEquipmentSectionIndex = 0;
            Check(model.IsEquipmentFilterVisible && model.HasActiveFilters, "returning restores owned equipment filter state");
            Console.WriteLine("Shell navigation, filters, all twelve pages, fleet planner and compact layout checks passed.");
        }
        finally { if (File.Exists(settingsPath)) File.Delete(settingsPath); }
    }

    // The macro page needs the user's own ALAuto clone; without one it stays out of the menu.
    private static void CheckMacroMenu(MainWindowViewModel model)
    {
        bool InMenu(MainWindowViewModel shell) => shell.NavigationView.Cast<MainWindowViewModel.NavigationDestination>()
            .Any(destination => destination.Index == MainWindowViewModel.MacroPageIndex);
        Check(InMenu(model) == model.Macros.HasAlautoRoot, "the macro page is listed exactly when an ALAuto clone is found");

        var emptyFolder = Path.Combine(Path.GetTempPath(), $"azurlane-no-alauto-{Guid.NewGuid():N}");
        Directory.CreateDirectory(emptyFolder);
        var settingsPath = Path.Combine(Path.GetTempPath(), $"azurlane-no-macro-{Guid.NewGuid():N}.json");
        try
        {
            var settings = new AppSettingsService(settingsPath);
            settings.Save(new AppSettings { IsAutoRefreshEnabled = false, AlautoRoot = emptyFolder });
            using var withoutAlauto = new MainWindowViewModel(settings, startBackgroundServices: false);
            Check(!withoutAlauto.Macros.HasAlautoRoot && !InMenu(withoutAlauto) &&
                  withoutAlauto.NavigationView.Cast<object>().Count() == withoutAlauto.Destinations.Count - 1,
                "without an ALAuto clone only the macro page leaves the menu");
        }
        finally
        {
            if (File.Exists(settingsPath)) File.Delete(settingsPath);
            Directory.Delete(emptyFolder);
        }
    }

    // Clicking a tab must never move the tab strip under the pointer, whatever tools that tab shows.
    private static void CheckTabStripStays(MainWindowViewModel model, DashboardShellView shell, string output,
        Action<FrameworkElement, int, int, string> render, int page, string tabsName, Action<int> selectSection)
    {
        model.NavigateCommand.Execute(page.ToString());
        double? firstY = null;
        for (var section = 0; section < 5; section++)
        {
            selectSection(section);
            render(shell, 1320, 800, Path.Combine(output, $"tabs-{page}-{section}.png"));
            var tabs = Descendants(shell).OfType<System.Windows.Controls.TabControl>().Single(tab => tab.Name == tabsName);
            var header = (FrameworkElement)tabs.Template.FindName("HeaderPanel", tabs);
            var y = header.TranslatePoint(new Point(), shell).Y;
            firstY ??= y;
            Check(Math.Abs(y - firstY.Value) < 0.5, $"page {page} section {section}: tab strip stays at y={firstY:0} (was {y:0})");
        }
        selectSection(0);
    }

    private static bool IsShown(FrameworkElement element)
    {
        for (DependencyObject? current = element; current is not null; current = VisualTreeHelper.GetParent(current))
            if (current is UIElement { Visibility: not Visibility.Visible }) return false;
        return true;
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

    private static void CheckFleetPlanner(MainWindowViewModel model, DashboardShellView shell, string output,
        Action<FrameworkElement, int, int, string> render)
    {
        var planner = model.FleetPlanner;
        model.NavigateCommand.Execute("10");
        Check(model.PageTitle == "편성·육성" && !model.IsInventorySearchVisible, "fleet planner page hides inventory search");
        Check(planner.SelectedContent?.Key == "w1516" && planner.ChapterTabs[0].IsSelected, "first content is selected by default");
        Check(planner.SelectedContent!.Fleets.Count == 2 && planner.SelectedContent.Fleets[0].ShipCount == 6,
            "chapter content shows boss and mob fleets");
        render(shell, 1440, 900, Path.Combine(output, "fleet-planner.png"));
        render(shell, 1080, 900, Path.Combine(output, "fleet-planner-compact.png"));

        var candidate = planner.SelectedContent.Candidates[0];
        planner.HighlightedShipUid = candidate.ReplacesUid;
        render(shell, 1440, 900, Path.Combine(output, "fleet-planner-highlight.png"));
        planner.HighlightedShipUid = "";
        planner.AddGoalCommand.Execute(candidate);
        Check(model.GrowthPlanner.SavedGoals.Any(goal => goal.ShipUid == candidate.ShipUid && goal.TargetLevel == 120),
            "adding a candidate saves a level goal");
        Check(planner.Feedback.Contains("Lv.120"), "adding a goal confirms what was saved");

        planner.BossTabs.Single(tab => tab.Key == "ex").IsSelected = true;
        Check(planner.SelectedContent?.Key == "ex" && !planner.ChapterTabs[0].IsSelected && planner.Feedback == "",
            "switching content moves the selection and clears stale feedback");
        render(shell, 1440, 900, Path.Combine(output, "fleet-planner-empty-content.png"));

        planner.LevelingTabs[0].IsSelected = true;
        Check(planner.IsLevelingSelected && planner.ShowsLevelingBody && !planner.ShowsStandardBody &&
              planner.ChapterTabs.Concat(planner.BossTabs).All(tab => !tab.IsSelected),
            "the 쫄작 tab swaps in its own layout");
        var leveling = planner.LevelingPlan!;
        Check(leveling.Stage.Label == "12-4" && leveling.Slots.Count(slot => slot.IsLeech) == 2 &&
              leveling.Slots.All(slot => slot.ShipUid != "23"),
            "leeches fill the fleet; a vanguard far below the enemy waits");
        Check(planner.WaitingQueue.Any(row => row.ShipUid == "23" && row.StatusText == "이 해역 제외"), "the waiting list says why");

        // One growth order: the growth page lists the whole pool, home shows its top, cards look ships up in it.
        Check(planner.GrowthQueue.Count == leveling.PoolCount &&
              planner.HomeQueue.SequenceEqual(planner.GrowthQueue.Take(FleetPlannerViewModel.HomeQueueLength)),
            "home shows the top of the same queue the growth page lists");
        var outsider = planner.GrowthQueue.Last(row => !row.IsGoal);
        planner.TogglePinCommand.Execute(outsider.ShipUid);
        var pinned = planner.QueueIndex.Row(outsider.ShipUid)!;
        Check(pinned.IsGoal && pinned.Group == 0 && planner.GrowthQueue.First().IsGoal &&
              planner.HomeQueue.Any(row => row.ShipUid == outsider.ShipUid),
            "pinning makes a goal that leads the queue everywhere");
        planner.TogglePinCommand.Execute(outsider.ShipUid);
        Check(!planner.QueueIndex.Row(outsider.ShipUid)!.IsGoal, "unpinning releases the goal");
        render(shell, 1440, 900, Path.Combine(output, "fleet-planner-leveling.png"));

        // Gear: the whole leveling fleet is allocated, drivers first; the card opens on the vanguard lead driver.
        var lead = planner.LevelingVanguardSlots.OfType<LevelingSlot>().Single(slot => slot.IsLead);
        Check(planner.SelectedLevelingShipUid == lead.ShipUid && planner.LevelingGearPanel.Rows.Count == 5 &&
              planner.LevelingGearPanel.Rows[0].ActionKind == "move" && lead.GearChanges == 3,
            "the 쫄작 gear card opens on the lead driver, who gets the best pieces first");
        Check(planner.LevelingMainSlots.Concat(planner.LevelingVanguardSlots).OfType<LevelingSlot>()
                  .Where(slot => slot.IsLeech).All(slot => !slot.HasGearChanges),
            "leeches get what is left after the drivers");
        var leech = planner.LevelingMainSlots.OfType<LevelingSlot>().First(slot => slot.IsLeech);
        planner.SelectLevelingShipCommand.Execute(leech);
        Check(planner.SelectedLevelingShipUid == leech.ShipUid && planner.LevelingGearPanel.ShipUid == leech.ShipUid &&
              planner.LevelingGearPanel.Rows.Count == 5, "choosing a tile opens that ship's gear");
        render(shell, 1440, 1200, Path.Combine(output, "fleet-planner-leveling-gear.png"));
        planner.SetGearModeCommand.Execute("bag");
        Check(planner.LevelingGearPanel.Rows.All(row => row.ActionKind is not ("move" or "move-sortie")),
            "the warehouse-only toggle applies to the 쫄작 card too");
        planner.SetGearModeCommand.Execute("move");
        render(shell, 860, 1400, Path.Combine(output, "fleet-planner-leveling-stacked.png"));
        planner.SetDriverCountCommand.Execute(planner.DriverChoices.Single(choice => choice.Count == 3));
        Check(planner.DriverOverride == 3 && planner.LevelingPlan!.DriverCount == 3 &&
              planner.DriverChoices.Single(choice => choice.Count == 3).IsSelected, "a manual driver count sticks");
        planner.SetDriverCountCommand.Execute(planner.DriverChoices[0]);
        Check(planner.DriverOverride is null, "자동 returns to the automatic rule");

        planner.BossTabs.Single(tab => tab.Key == "meta").IsSelected = true;
        var meta = planner.MetaPlan!;
        Check(planner.ShowsMetaBody && planner.SelectedBoss?.Id == 24 && meta.UsesGuide,
            "the META tab opens on the rotation boss with its guide");
        Check(meta.MainSlots[0]?.ShipUid == "2" && meta.MainSlots[1]?.Note == "1순위 미보유",
            "guide choices fill the fleet in order");
        Check(planner.SelectedMetaShipUid == "4" && planner.MetaGearPanel.Rows.Count == 5 && planner.MetaGearPanel.Rows[0].ActionKind == "bag",
            "the gear panel opens on the first stand-in, after the flagship took the only best gun");
        planner.SelectMetaShipCommand.Execute(meta.MainSlots[0]);
        Check(planner.SelectedMetaShipUid == "2" && planner.MetaGearPanel.Rows[0] is { ActionKind: "move", ActionText: "다른 함선에서 옮기기" },
            "choosing a tile shows that ship's gear, moved from outside the fleet");
        render(shell, 1440, 900, Path.Combine(output, "fleet-planner-meta.png"));

        // 실전 기록: the latest fight opens first; the overlap chip reads this boss's last fleet.
        var log = planner.MetaFightLog;
        Check(planner.HasMetaFights && log.Fights.Count == 3 && planner.SelectedMetaFight?.Id == 30,
            "the fight log lists this boss's fights and opens the latest");
        Check(planner.MetaOverlapText.StartsWith("이 보스 마지막 출격과", StringComparison.Ordinal),
            "with a captured fight, the fleet chip compares against this boss's last sortie, not a game fleet");
        Check(planner.SelectedMetaFight!.Findings[0].Title == "처치까지 14,284 부족" &&
              planner.SelectedMetaFight.Findings.Any(finding => finding.Title.StartsWith("선두", StringComparison.Ordinal)),
            "the latest fight leads with what was short and who sank");
        RenderMetaFights(shell, render, Path.Combine(output, "fleet-planner-meta-fights.png"), 1440, 2000);
        planner.SelectMetaFightCommand.Execute(log.Fights[1]);
        Check(planner.SelectedMetaFight?.Id == 20 && planner.SelectedMetaFight.ResultText.StartsWith("마무리", StringComparison.Ordinal),
            "choosing a fight opens it; a mop-up says so");
        RenderMetaFights(shell, render, Path.Combine(output, "fleet-planner-meta-fights-mopup.png"), 1440, 2000);
        planner.SelectMetaFightCommand.Execute(log.Fights[0]);
        RenderMetaFights(shell, render, Path.Combine(output, "fleet-planner-meta-fights-stacked.png"), 860, 2600);

        planner.SetGearModeCommand.Execute("bag");
        Check(planner.GearModeKey == "bag" && planner.MetaGearPanel.Rows.All(row => row.ActionKind is not ("move" or "move-sortie")),
            "warehouse-only mode never moves gear");
        planner.SelectedBoss = planner.BossChoices.Single(choice => choice.Id == 12);
        Check(!planner.MetaPlan!.UsesGuide && planner.MetaPlan.GearUsecase == "L" && planner.SavedBossId == 12,
            "an archive boss without a guide falls back to armor scoring and is remembered");
        Check(planner.HasNoMetaFights && planner.MetaFightEmptyText.Contains("진츠·META") &&
              planner.MetaOverlapText.StartsWith("게임 편성과", StringComparison.Ordinal),
            "another boss shows only its own fights and fleet; the rotation boss's fleet is not reused");
        render(shell, 1440, 900, Path.Combine(output, "fleet-planner-meta-archive.png"));
        planner.SelectedBoss = planner.BossChoices.Single(choice => choice.Id == 24);
        planner.SetGearModeCommand.Execute("move");
        planner.ChapterTabs[0].IsSelected = true;
    }

    private static FleetPlanDashboard FleetPlanFixture()
    {
        string[] names = ["엔터프라이즈", "벨파스트", "뉴저지", "시마카제", "유니콘", "아드미랄 그라프 슈페"];
        var ships = Enumerable.Range(1, 14).Select(index => new AzurLaneDashboard.Core.Services.FleetPlannerShip
        {
            Uid = index.ToString(),
            GroupKey = index,
            RegalUid = $"u{index}",
            RegalGearId = 1,
            Name = names[(index - 1) % names.Length] + (index > 6 ? $" {index}" : ""),
            Hull = index % 2 == 0 ? "BB" : "DD",
            Position = index % 2 == 0 ? "Main" : "Vanguard",
            Level = index == 13 ? 108 : 125,
            Ratings = new Dictionary<string, string>
            {
                ["W15/16 Boss"] = index switch { 1 or 2 => "SS", 13 => "S", 5 => "A*", _ => "S" },
                ["W15/16 Mob"] = index == 13 ? "B" : "S"
            },
            Metrics = new Dictionary<string, double>
            {
                ["Heavy Dmg"] = index == 13 ? 2400 : 1000 + index * 40, ["Light Dmg"] = 900 + index * 30,
                ["Offense Buff"] = index % 3 == 0 ? 1 : 0, ["VG Defense"] = index % 4 == 1 ? 1 : 0, ["AA"] = index % 2
            },
            ExperienceToReady = index == 13 ? 1_480_000 : 0,
            SampleExp = index == 13 ? 913 : 0,
            SkillGap = index == 9 ? 2 : 0
        }).ToList();
        IReadOnlyList<IReadOnlySet<string>> gameFleets = [new HashSet<string> { "1", "2", "3", "4" }];
        var plan = AzurLaneDashboard.Core.Services.FleetPlanBuilder.Build(ships, gameFleets,
        [
            new() { DateText = "09-18", ShipText = "발파라이소", OriginalText = "Valparaíso: Chapter 14 Boss A → S, Chapter 15/16 Mob S → A.",
                Parts = [new() { Label = "14 해역 보스", From = "A", To = "S" }, new() { Label = "15·16 해역 잡몹", From = "S", To = "A" }] },
            new() { DateText = "10-10", ShipText = "로열 포춘", Parts = [new() { Label = "고유 증강 평가 추가" }] }
        ], "Regal · 2026-10-10");
        return plan.WithInputs(PlannerInputsFixture(ships, gameFleets));
    }

    // Leveling, META boss and gear inputs over the same ships: Lv.125 drivers plus three leeches.
    private static FleetPlannerInputs PlannerInputsFixture(
        IReadOnlyList<AzurLaneDashboard.Core.Services.FleetPlannerShip> ships, IReadOnlyList<IReadOnlySet<string>> gameFleets)
    {
        var leeches = new[]
        {
            new LevelingShip { Uid = "21", Name = "쩔 주력", Position = "Main", Hull = "CV", Rarity = "UR", Level = 100, Morale = 134, IsLocked = true, ContentReach = 2 },
            new LevelingShip { Uid = "22", Name = "쩔 전위", Position = "Vanguard", Hull = "CL", Rarity = "SSR", Level = 108, Morale = 150, IsLocked = true },
            new LevelingShip { Uid = "23", Name = "낮은 전위", Position = "Vanguard", Hull = "DD", Rarity = "SR", Level = 1, Morale = 150, IsLocked = true }
        };
        var drivers = ships.Where(ship => ship.Level >= 120).Select(ship => new LevelingShip
        {
            Uid = ship.Uid, Name = ship.Name, Position = ship.Position, Hull = ship.Hull, Level = ship.Level, Morale = 119,
            IsLocked = true, MobRatingScore = 44
        });
        var bosses = AzurLaneDashboard.Core.Services.MetaBossCatalog.Parse(JsonDocument.Parse("""
            {
              "24": { "id": 24, "name": "새러토가·META", "availability": "timed", "armor": "Medium", "hull": "CV", "battle_seconds": 80,
                      "windows": [ { "start": "2000-01-01T00:00:00", "end": "2099-12-31T12:00:00" } ],
                      "levels": [ { "level": 15, "hp": 1820000, "enemy_id": 1, "expedition_id": 2 } ],
                      "skills": [ { "name": "체인지 배틀 뮤직", "grade": 1, "text": "장갑이 25초마다 바뀐다." } ] },
              "12": { "id": 12, "name": "진츠·META", "availability": "always", "armor": "Light", "hull": "CL", "battle_seconds": 80,
                      "levels": [ { "level": 15, "hp": 1420000, "enemy_id": 3, "expedition_id": 4 } ], "skills": [] }
            }
            """).RootElement.Clone());
        var (guides, rules) = AzurLaneDashboard.Core.Services.MetaBossCatalog.ParseGuides(JsonDocument.Parse("""
            {
              "bosses": { "24": { "verified": true, "lastReviewed": "2026-10-17", "armorAlternates": ["Medium", "Heavy"], "armorPeriodSeconds": 25,
                                  "mechanics": [ { "title": "선두 록온", "text": "선두가 받는 피해 증가", "response": "선두에 탱커" } ],
                                  "fleet": { "main": [ ["u2"], ["u99", "u4"] ], "vanguard": [ ["u1"] ], "flagship": 0 },
                                  "sources": [ { "title": "Guide", "url": "https://example.invalid", "sourceUpdated": "2026-09-16" } ] } },
              "regalStarRules": []
            }
            """).RootElement.Clone());
        var gearMap = new AzurLaneDashboard.Core.Services.RegalGearMap(JsonDocument.Parse("""
            {
              "ship": { "1": { "M": 10, "H": 11 } },
              "usecase": { "10": [100, 101, 102, 103, 104], "11": [110, 101, 102, 103, 104] },
              "slot": { "100": [1, 2], "101": [3], "102": [4], "103": [5], "104": [5], "110": [2] },
              "row": { "1": [1], "2": [2], "3": [3], "4": [4], "5": [5] },
              "equip": { "1": { "id": 500 }, "2": { "id": 600 }, "3": { "id": 700 }, "4": { "id": 800 }, "5": { "id": 900 } }
            }
            """).RootElement.Clone());
        var items = new Dictionary<int, GearItemInfo>
        {
            [500] = new(500, "시제형 주포", "", "UR", "gun"), [600] = new(600, "연장 주포", "", "SSR", "gun"),
            [700] = new(700, "자기 어뢰", "", "SSR", "torpedo"), [800] = new(800, "연장 고각포", "", "SSR", "antiair"),
            [900] = new(900, "조타장치", "", "SSR", "auxiliary")
        };
        return new FleetPlannerInputs
        {
            Ships = ships,
            LevelingShips = leeches.Concat(drivers).ToList(),
            Stages = [new LevelingStageOption(new LevelingStage
            {
                Key = "1204", Label = "12-4", Title = "12-4 태스크포스", Note = "커뮤니티 기준 해역", EnemyLevel = 113, LuaExp = 845
            }, [])],
            MetaBosses = bosses,
            MetaBossGuides = guides,
            StarRules = rules,
            GameFleets = gameFleets,
            MetaFights = MetaFightFixture(),
            MetaFightShips = ships.ToDictionary(ship => ship.Uid, ship => new MetaFightShipInfo(ship.Uid, ship.Name, "", "SSR",
                ship.Hull, ship.Position, ship.Level, ship.RegalUid)),
            GearMap = gearMap,
            GearInventory = new GearInventory
            {
                Spare = new Dictionary<int, IReadOnlyList<int>> { [600] = [10], [900] = [0, 0] },
                Equipped = [new GearHolder("30", "다른 함선", 1, 500, 13, 90, InSortieFleet: false)]
            },
            GearShips = ships.Select(ship => (ship.Uid, ship.Name)).Concat(leeches.Select(ship => (ship.Uid, ship.Name)))
                .ToDictionary(ship => ship.Uid, ship => new GearFleetShip(ship.Uid, ship.Name, 1, false,
                    new Dictionary<int, GearItemCopy> { [2] = new(700, 10) })),
            GearItems = items
        };
    }
    private static void CheckMeowfficers(MainWindowViewModel model, DashboardShellView shell, string output,
        Action<FrameworkElement, int, int, string> render)
    {
        var page = model.Meowfficers;
        model.NavigateCommand.Execute(MainWindowViewModel.MeowfficerPageIndex.ToString());
        Check(model.PageTitle == "지휘냥" && model.SelectedDestination.Group == "나의 함대", "the 지휘냥 page sits under 나의 함대");
        Check(page.HasCats && page.HasFleets && page.SelectedCat?.Uid == "drake",
            "the page opens on the first fleet's 지휘 cat");
        render(shell, 1440, 900, Path.Combine(output, "meowfficers.png"));

        var feed = page.Filters.Single(filter => filter.Key == "feed");
        page.SelectFilterCommand.Execute(feed);
        Check(page.Filters.Single(filter => filter.Key == "feed").IsSelected &&
              page.VisibleKinds.SelectMany(kind => kind.Copies).All(copy => copy.Verdict == MeowfficerVerdict.Feed) &&
              page.VisibleKinds.Count > 0, "the feed filter lists only material");
        var cat = page.VisibleKinds[0].Copies[0];
        page.SelectCatCommand.Execute(cat);
        Check(page.SelectedUid == cat.Uid && page.SelectedCat?.Verdict == MeowfficerVerdict.Feed, "choosing a cat opens its talents");
        var view = new MeowfficerView { DataContext = page, FontFamily = shell.FontFamily };
        render(view, 1440, 1600, Path.Combine(output, "meowfficers-feed.png"));
        page.SelectFilterCommand.Execute(page.Filters.Single(filter => filter.Key == "all"));
        render(view, 860, 2600, Path.Combine(output, "meowfficers-stacked.png"));
        page.ApplySnapshot(MeowfficerDashboard.Empty);
        Check(page.HasNoCats && page.SelectedCat is null, "no login list yet shows the empty state");
        render(view, 1080, 640, Path.Combine(output, "meowfficers-empty.png"));
        page.ApplySnapshot(MeowfficerFixture());
    }

    // A few owned cats over the seed maps: the leveling fleet's Tempesta pair, raised and fresh copies, an unrated cat.
    private static MeowfficerDashboard MeowfficerFixture()
    {
        var maps = ProjectLocator.FindMapsDirectory();
        using var commanders = JsonDocument.Parse(File.ReadAllText(Path.Combine(maps, "commanders.json")));
        using var guide = JsonDocument.Parse(File.ReadAllText(Path.Combine(maps, "meowfficer_guide.json")));
        var catalog = AzurLaneDashboard.Core.Services.MeowfficerCatalog.Parse(commanders.RootElement.Clone(), guide.RootElement.Clone());
        OwnedMeowfficer Cat(string uid, int template, int level, int[] talents, int[] origin, string nick = "") =>
            new(uid, template, level, level >= 30, nick, talents, origin, catalog.Kinds[template].SkillId + (level >= 30 ? 2 : 0));
        return AzurLaneDashboard.Core.Services.MeowfficerPlanBuilder.Build(catalog,
        [
            Cat("drake", 960011, 30, [5131, 132, 5101, 903, 5141], [5141, 901]),
            Cat("every", 960041, 30, [5131, 152, 5101, 833, 5161], [5161, 831]),
            Cat("antenna", 10021, 30, [123, 2023, 5051, 2113], [121, 2021, 2112], "미제냥"),
            Cat("antenna-feed", 10021, 1, [411, 121, 2112], [411, 121, 2112]),
            Cat("lime", 20011, 30, [5041, 212, 2023, 633, 5021], [211, 632, 5021], "전함냥"),
            Cat("lime-good", 20011, 1, [632, 811, 5021], [632, 811, 5021]),
            Cat("bisha", 30011, 30, [632, 643, 2033, 5101], [2031, 641, 5101], "항모냥"),
            Cat("steel", 40011, 30, [612, 903, 203, 5051], [901, 201, 5051], "잠수냥"),
            Cat("edel", 41021, 30, [511, 5131, 203, 2043], [2041, 201]),
            Cat("teach", 960061, 28, [322, 5101, 833, 5171], [5171, 831])
        ],
        [
            new MeowfficerFleetInput(1, ["CL", "CL", "DD", "CV", "BB", "BB"], "drake", "every"),
            new MeowfficerFleetInput(2, ["CB", "DD", "CB", "CV", "CV"], "", "bisha"),
            new MeowfficerFleetInput(11, ["SSV", "SS", "SSV"], "edel", "steel")
        ], farmFleetId: 1);
    }

    // The fight log sits below the fold of the page, so the META tab is rendered on its own at full height.
    private static void RenderMetaFights(DashboardShellView shell, Action<FrameworkElement, int, int, string> render, string path,
        int width, int height)
    {
        var view = new MetaBossPlanView
        {
            DataContext = ((MainWindowViewModel)shell.DataContext).FleetPlanner, FontFamily = shell.FontFamily
        };
        render(view, width, height, path);
    }

    // Two full fights and a mop-up against the rotation boss (enemy 1): the lead sinks, one main ship is swapped.
    private static IReadOnlyList<MetaBossFightRecord> MetaFightFixture()
    {
        MetaBossFightRecord Fight(long id, long damage, int left, int seconds, string[] sortie, (string Uid, long Damage, int Hp)[] stats,
            bool own = true) => new()
        {
            Id = id, OwnerUid = own ? "100" : "200", IsOwnBoss = own, FinishedAt = new DateTime(2026, 11, 5, 21, 0, 0).AddMinutes(id),
            BossEnemyId = 1, BossDamage = damage, BossTotalHp = 1820000, BossHpLeft = left, TotalSeconds = seconds, AutoAtStart = true,
            SortieUids = sortie,
            Stats = stats.Select(stat => new MetaFightShipStat(stat.Uid, stat.Damage, 4000, stat.Hp, 6000, 6000)).ToList(),
            Gear = id == 30
                ? sortie.ToDictionary(uid => uid, uid => (IReadOnlyList<MetaFightGear>)
                    [new MetaFightGear(1, 500, "시제형 주포", 13), new MetaFightGear(2, 700, "자기 어뢰", 10)])
                : null
        };
        return
        [
            Fight(10, 1469897, 1923, 80, ["1", "3", "5", "2", "8", "6"],
                [("2", 416151, 9422), ("8", 282507, 9329), ("6", 404301, 8528), ("1", 92847, 0), ("3", 198548, 4221), ("5", 124389, 1005)]),
            Fight(20, 193658, 0, 19, ["1", "3", "5", "2", "8", "6"], [("2", 78829, 9928), ("1", 27003, 9705)], own: false),
            Fight(30, 1805716, 78, 80, ["1", "3", "5", "2", "4", "6"],
                [("2", 505368, 9540), ("4", 416378, 9442), ("6", 385581, 9259), ("1", 117023, 0), ("3", 257068, 2413), ("5", 128256, 2158)])
        ];
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
