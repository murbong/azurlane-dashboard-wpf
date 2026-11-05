using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using AzurLaneDashboard.App.ViewModels;
using AzurLaneDashboard.App.Views;
using AzurLaneDashboard.Core.Models;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        var application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        foreach (var resource in new[]
        {
            "pack://application:,,,/PresentationFramework.Fluent;component/Themes/Fluent.xaml",
            "pack://application:,,,/AzurLaneDashboard.App;component/Themes/Dark.xaml",
            "pack://application:,,,/AzurLaneDashboard.App;component/Themes/Controls.xaml"
        }) application.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri(resource) });
        application.Resources["BoolToVisibilityConverter"] = new BooleanToVisibilityConverter();
        application.Resources["AssetImageConverter"] = new AzurLaneDashboard.App.Converters.AssetImageConverter();
        application.Resources["AppFontFamily"] = new FontFamily("Segoe UI, Malgun Gothic");
        var bindingErrors = new BindingErrors();
        CheckBulkCollection();
        PresentationTraceSources.DataBindingSource.Listeners.Add(bindingErrors);
        PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Error;
        var saveCount = 0;
        var planner = new GrowthPlannerViewModel([], () => saveCount++, _ => { });
        planner.ApplySnapshot(Snapshot("A.sqlite"));
        planner.SelectedShip = planner.AvailableShips[0];
        planner.TargetLevelText = "122";
        planner.TargetSkillLevelText = "8";
        Check(planner.SaveGoalCommand.CanExecute(null), "valid editor can save");
        planner.SaveGoalCommand.Execute(null);
        Check(planner.Plans.Count == 1 && saveCount == 1, "goal added and persistence requested");
        planner.TargetLevelText = "123";
        planner.ApplySnapshot(Snapshot("A.sqlite"));
        Check(planner.TargetLevelText == "123" && planner.SelectedShip?.ShipUid == "1", "refresh retains draft and selection");
        planner.TargetLevelText = "abc";
        Check(!planner.SaveGoalCommand.CanExecute(null), "invalid numeric text disables saving");
        planner.TargetLevelText = "126";
        Check(!planner.SaveGoalCommand.CanExecute(null), "out-of-range target disables saving");
        planner.TargetLevelText = "122";
        planner.SaveGoalCommand.Execute(null);
        Check(planner.Plans.Count == 1, "saving same ship updates instead of duplicating");
        planner.ApplySnapshot(Snapshot("B.sqlite"));
        Check(planner.Plans.Count == 0 && planner.SelectedShip is null, "source switch does not reuse ship UIDs");
        planner.SelectedShip = planner.AvailableShips[0];
        planner.SaveGoalCommand.Execute(null);
        Check(planner.SavedGoals.Count == 2, "goals for another database retained");
        planner.ApplySnapshot(Snapshot("A.sqlite"));
        Check(planner.Plans.Count == 1 && planner.SavedGoals.Count == 2, "switch back restores scoped goals");
        planner.EditGoalCommand.Execute(planner.Plans[0]);
        planner.Search = "없는이름";
        Check(!planner.ShipChoices.Cast<object>().Any(), "local ship search filters candidates");
        planner.Search = "";

        // Exercise actual XAML bindings and rendering without starting capture or opening the main app.
        var output = args.Length > 0 ? Path.GetFullPath(args[0]) : Path.Combine(Path.GetTempPath(), "azurlane-growth-ui-checks");
        Directory.CreateDirectory(output);
        var view = new GrowthPlannerView
        {
            DataContext = planner, FontFamily = (FontFamily)application.FindResource("AppFontFamily")
        };
        Render(view, 900, 700, Path.Combine(output, "growth-dark.png"));
        planner.TargetLevelText = "124";
        planner.ApplySnapshot(Snapshot("A.sqlite"));
        DrainDispatcher();
        Check(planner.TargetLevelText == "124" && planner.SelectedShip?.ShipUid == "1", "bound ComboBox refresh preserves draft");
        Render(view, 650, 480, Path.Combine(output, "growth-dark-compact.png"));
        Render(view, 650, 480, Path.Combine(output, "growth-dark-compact-scrolled.png"), scrollToEnd: true);
        var dictionaries = application.Resources.MergedDictionaries;
        dictionaries[1] = new ResourceDictionary
        {
            Source = new Uri("pack://application:,,,/AzurLaneDashboard.App;component/Themes/Light.xaml")
        };
        Render(view, 900, 700, Path.Combine(output, "growth-light.png"));
        planner.RemoveGoalCommand.Execute(planner.Plans[0]);
        Check(planner.Plans.Count == 0 && planner.SavedGoals.Count == 1, "delete affects only selected goal");
        planner.ApplySnapshot(new DashboardSnapshot { DatabasePath = "A.sqlite" });
        Render(view, 650, 480, Path.Combine(output, "growth-empty.png"));
        Check(!planner.SaveGoalCommand.CanExecute(null), "empty snapshot cannot save goals");
        ShellChecks.Run(output, (content, width, height, path) => Render(content, width, height, path));
        AccountShellChecks.Run(output, (content, width, height, path) => Render(content, width, height, path));
        MacroNoticeChecks.Run();
        MonthlyShopPageChecks.Run(output, (content, width, height, path) => Render(content, width, height, path));
        Check(bindingErrors.Messages.Count == 0, "XAML binding errors: " + string.Join(Environment.NewLine, bindingErrors.Messages));
        Console.WriteLine($"Growth UI state, source isolation and XAML checks passed. Renders: {output}");
        application.Shutdown();
    }

    private static DashboardSnapshot Snapshot(string path)
    {
        using var table = JsonDocument.Parse("""
            {"118":{"exp":40000},"119":{"exp":50000},"120":{"exp":60000},"121":{"exp":70000}}
            """);
        return new()
        {
            DatabasePath = path, ShipLevelTable = table.RootElement.Clone(),
            Ships = [new ShipRow
            {
                ShipUid = "1", Name = "엔터프라이즈", Rarity = "SSR", Level = 118, Exp = 12000,
                HasFullState = true, RecentBattleExpAvg = 2200, EmptySlots = 1,
                SkillDetails = [new() { Index = 1, Name = "Lucky E", Level = 6, MaxLevel = 10 }]
            }]
        };
    }

    private static void Render(FrameworkElement content, int width, int height, string path, bool scrollToEnd = false)
    {
        var root = new Border
        {
            Background = (Brush)Application.Current.FindResource("BgBrush"),
            Padding = new Thickness(12), Child = content, Width = width, Height = height
        };
        // Supply the same Window ancestor used by item commands in the real shell, without showing it.
        var host = new Window { Content = root, DataContext = content.DataContext };
        root.Measure(new Size(width, height));
        root.Arrange(new Rect(0, 0, width, height));
        root.UpdateLayout();
        DrainDispatcher();
        // A hidden Window does not propagate visibility changes through cached parent measurements.
        // Re-measure the full visual tree so headless snapshots match a displayed window's layout.
        foreach (var element in Descendants(root).OfType<UIElement>().Reverse()) element.InvalidateMeasure();
        root.InvalidateMeasure();
        root.Measure(new Size(width, height));
        root.Arrange(new Rect(0, 0, width, height));
        root.UpdateLayout();
        if (scrollToEnd)
        {
            var list = Descendants(root).OfType<ListBox>().Single();
            var scroll = Descendants(list).OfType<ScrollViewer>().First();
            scroll.ScrollToEnd();
            DrainDispatcher();
            root.UpdateLayout();
            Check(scroll.VerticalOffset > 0, "compact goal card scrolls to its action buttons");
        }
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(root);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
        root.Child = null;
        host.Content = null;
        host.Close();
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }

    private static void DrainDispatcher() =>
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
    private static void CheckBulkCollection()
    {
        var values = new BulkObservableCollection<int> { 1, 2, 3 };
        var changes = 0;
        values.CollectionChanged += (_, _) => changes++;
        values.ReplaceRange(values.Where(value => value > 1));
        Check(values.SequenceEqual([2, 3]) && changes == 1, "lazy self replacement keeps values and sends one reset");
        values.ReplaceRange([2, 3]);
        Check(changes == 1, "unchanged snapshot does not reset the view");
        values.AddRangeAndTrimStart(values, 3);
        Check(values.SequenceEqual([3, 2, 3]) && changes == 2, "self append trims oldest values in one reset");
        static IEnumerable<int> FailingSource()
        {
            yield return 4;
            throw new IOException("test enumeration failure");
        }
        try { values.ReplaceRange(FailingSource()); }
        catch (IOException) { }
        Check(values.SequenceEqual([3, 2, 3]) && changes == 2, "failed enumeration does not lose visible data");
        values.RemoveFromStart(99);
        Check(values.Count == 0 && changes == 3, "bounded removal emits one reset");
    }
    private static void Check(bool condition, string description)
    {
        if (!condition) throw new InvalidOperationException(description);
    }
    private sealed class BindingErrors : TraceListener
    {
        public List<string> Messages { get; } = [];
        public override void Write(string? message) { if (message is not null) Messages.Add(message); }
        public override void WriteLine(string? message) => Write(message);
    }
}
