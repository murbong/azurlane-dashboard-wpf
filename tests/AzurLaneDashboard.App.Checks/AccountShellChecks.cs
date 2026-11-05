using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AzurLaneDashboard.App.ViewModels;
using AzurLaneDashboard.App.Views;
using AzurLaneDashboard.Core.Models;
using AzurLaneDashboard.Data.Services;
using Microsoft.Data.Sqlite;

/// <summary>
/// Two accounts on two emulators: the sidebar picks which one is on screen, the settings page lists one capture
/// per emulator, and the macro follows the shown account to its emulator. Everything lives in a temp folder.
/// </summary>
internal static class AccountShellChecks
{
    private const long MainUid = 100000001;
    private const long AltUid = 300000001;

    public static void Run(string output, Action<FrameworkElement, int, int, string> render)
    {
        RunSingleAccount(output, render);
        var folder = Path.Combine(Path.GetTempPath(), $"azurlane-account-shell-{Guid.NewGuid():N}");
        var settingsPath = Path.Combine(folder, "settings.json");
        Directory.CreateDirectory(folder);
        try
        {
            var registry = AccountRegistry.ForDataDirectory(folder);
            var main = registry.Observe(MainUid, "emulator-5554", 3, "TestAdmiral", 143, "한산도");
            var alt = registry.Observe(AltUid, "emulator-5556", 1, "부계정", 52);
            var settings = new AppSettingsService(settingsPath);
            settings.Save(new AppSettings
            {
                IsAutoRefreshEnabled = false, IsDarkMode = true, DataDirectory = folder, DatabasePath = main.DatabasePath,
                CaptureBackend = "adb", Serial = "emulator-5554", CaptureSerials = ["emulator-5554", "emulator-5556"]
            });

            using var model = new MainWindowViewModel(settings, startBackgroundServices: false);
            Check(model.Accounts.Select(account => account.Uid).SequenceEqual([MainUid, AltUid]) &&
                  model.SelectedAccount?.Uid == MainUid,
                "the picker lists both accounts and selects the one whose database is open");
            Check(model is { HasMultipleAccounts: true, ShowsSingleAccount: false } &&
                  model.Accounts[0] is { ServerText: "한산도", Detail: "Lv.143 · 한산도 · UID 100000001" } &&
                  model.Accounts[1].ServerText == "서버 1",
                "two accounts get the picker, each with its server; an unnamed server shows its number");
            Check(model.CaptureTargets.Select(target => target.Key).SequenceEqual(["emulator-5554", "emulator-5556"]),
                "each saved emulator gets its own capture row");
            Check(model.CaptureStartButtonText == "모두 시작" && model.StartCaptureCommand.CanExecute(null),
                "with several emulators the main button starts all of them");
            Check(MacroSerial(model) == "emulator-5554", "the macro drives the shown account's emulator");

            var shell = new DashboardShellView
            {
                DataContext = model, FontFamily = new FontFamily("Segoe UI, Malgun Gothic")
            };
            model.SelectedMainTabIndex = 0;
            render(shell, 1320, 800, Path.Combine(output, "accounts-sidebar.png"));

            // The capture section, with the sections above it folded so it fits in one render.
            model.SelectedMainTabIndex = 9;
            render(shell, 1320, 900, Path.Combine(output, "accounts-settings-folded.png"));
            foreach (var expander in Descendants(shell).OfType<Expander>())
            {
                expander.IsExpanded = Equals(expander.Header, "실시간 데이터 수집");
            }

            render(shell, 1320, 900, Path.Combine(output, "accounts-settings-capture.png"));

            model.SelectedAccount = model.Accounts.Single(account => account.Uid == AltUid);
            Check(string.Equals(model.DatabasePath, alt.DatabasePath, StringComparison.OrdinalIgnoreCase) &&
                  model.SelectedAccount?.Uid == AltUid,
                "picking the other account opens its own database");
            Check(MacroSerial(model) == "emulator-5556", "and the macro moves to that account's emulator");

            model.NewCaptureSerial = "emulator-5556";
            Check(!model.AddCaptureSerialCommand.CanExecute(null), "an emulator already in the list is not added twice");
            model.NewCaptureSerial = "127.0.0.1:16448";
            model.AddCaptureSerialCommand.Execute(null);
            Check(model.CaptureTargets.Count == 3 && model.CaptureTargets[2].Key == "127.0.0.1:16448",
                "a typed serial adds a capture row");
            model.CaptureTargets[2].RemoveCommand.Execute(null);
            Check(model.CaptureTargets.Count == 2, "a stopped row can be taken out again");
            Console.WriteLine("Multi-account sidebar, capture rows and account switch checks passed.");
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            try
            {
                Directory.Delete(folder, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }

    // One account: no picker, just whose data this is under the title.
    private static void RunSingleAccount(string output, Action<FrameworkElement, int, int, string> render)
    {
        var folder = Path.Combine(Path.GetTempPath(), $"azurlane-single-account-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        try
        {
            var main = AccountRegistry.ForDataDirectory(folder).Observe(MainUid, "emulator-5554", 3, "TestAdmiral", 143, "한산도");
            var settings = new AppSettingsService(Path.Combine(folder, "settings.json"));
            settings.Save(new AppSettings
            {
                IsAutoRefreshEnabled = false, IsDarkMode = true, DataDirectory = folder, DatabasePath = main.DatabasePath,
                CaptureBackend = "adb", Serial = "emulator-5554"
            });
            using var model = new MainWindowViewModel(settings, startBackgroundServices: false);
            Check(model is { ShowsSingleAccount: true, HasMultipleAccounts: false } &&
                  model.SelectedAccount?.Summary == "TestAdmiral · Lv.143 · 한산도",
                "a single account is named with its level and server instead of a picker");
            var shell = new DashboardShellView
            {
                DataContext = model, FontFamily = new FontFamily("Segoe UI, Malgun Gothic")
            };
            model.SelectedMainTabIndex = 0;
            render(shell, 1320, 800, Path.Combine(output, "accounts-single.png"));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            try
            {
                Directory.Delete(folder, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }

    private static string MacroSerial(MainWindowViewModel model) =>
        (string)typeof(MainWindowViewModel).GetProperty("MacroSerial", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(model)!;

    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }

    private static void Check(bool condition, string description)
    {
        if (!condition) throw new InvalidOperationException(description);
    }
}
