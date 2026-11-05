using System.IO;
using System.Reflection;
using AzurLaneDashboard.App.ViewModels;
using AzurLaneDashboard.Automation.Engine;
using AzurLaneDashboard.Core.Models;
using AzurLaneDashboard.Data.Services;

/// <summary>When a finished macro run asks for a Windows notification, and that the toggle is saved.</summary>
internal static class MacroNoticeChecks
{
    public static void Run()
    {
        var saves = 0;
        var host = new MacroHost(() => false, () => null, () => "127.0.0.1:16384", () => [], () => { }, () => saves++);
        using var macros = new MacroViewModel(host, Path.Combine(Path.GetTempPath(), $"no-alauto-{Guid.NewGuid():N}"));
        var notices = new List<MacroEndNotice>();
        macros.EndNotice += (_, notice) => notices.Add(notice);
        var item = new MacroItem(new MacroInfo("leveling_loop.json", "leveling_loop.json", "쫄작 반복", "", true, true, true, "", []));
        var finish = typeof(MacroViewModel).GetMethod("Finish", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var lastMessage = typeof(MacroViewModel).GetField("lastMacroMessage", BindingFlags.Instance | BindingFlags.NonPublic)!;
        MacroEndNotice? Finish(Task<object?> task, string message = "")
        {
            notices.Clear();
            lastMessage.SetValue(macros, message);
            finish.Invoke(macros, [item, task]);
            return notices.SingleOrDefault();
        }

        Check(macros.NotifyOnEnd, "notifications are on unless turned off");
        var done = Finish(Task.FromResult<object?>(true), "정한 횟수(20회)를 채워 멈춥니다.");
        Check(done is { IsWarning: false } && done.Detail.StartsWith("정한 횟수(20회)를 채워 멈춥니다.\n확인된 전투", StringComparison.Ordinal) &&
              done.Title == macros.EndedTitle, "a finished run notifies with why it stopped, then the packet summary");
        Check(Finish(Task.FromResult<object?>(false), "전의 부족 또는 출격 불가 화면이라 멈춥니다.") is { IsWarning: true },
            "a run that stopped short notifies as a warning");
        Check(Finish(Task.FromCanceled<object?>(new CancellationToken(true))) is null, "stopping it yourself never notifies");
        Check(Finish(Task.FromException<object?>(new MacroUnsupportedException("벽람항로 화면을 찾지 못했습니다."))) is null,
            "a run refused at start does not notify");
        Check(Finish(Task.FromException<object?>(new InvalidOperationException("adb 연결이 끊겼습니다."))) is
                  { IsWarning: true } failed && failed.Detail.StartsWith("adb 연결이 끊겼습니다.", StringComparison.Ordinal),
            "a run that failed along the way notifies with the error");
        macros.NotifyOnEnd = false;
        Check(saves == 1, "turning notifications off asks to save settings");
        Check(Finish(Task.FromResult<object?>(true), "끝") is null, "no notification while turned off");

        var settingsPath = Path.Combine(Path.GetTempPath(), $"azurlane-notice-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(settingsPath, """{ "IsAutoRefreshEnabled": false }""");
            var service = new AppSettingsService(settingsPath);
            Check(service.Load() is { NotifyMacroEnd: true, NotifySortieEnd: true },
                "settings saved before the toggles existed keep notifications on");
            using (var model = new MainWindowViewModel(service, startBackgroundServices: false))
            {
                Check(model.Macros.NotifyOnEnd, "the macro page starts from the saved setting");
                Check(model.NotifySortieEnd, "the 연속 작전 notice starts from the saved setting");
                model.Macros.NotifyOnEnd = false;
                model.NotifySortieEnd = false;
            }

            Check(new AppSettingsService(settingsPath).Load() is { NotifyMacroEnd: false, NotifySortieEnd: false },
                "turning them off is saved");
            using (var model = new MainWindowViewModel(new AppSettingsService(settingsPath), startBackgroundServices: false))
                Check(!model.Macros.NotifyOnEnd && !model.NotifySortieEnd, "and read back on the next start");
        }
        finally
        {
            if (File.Exists(settingsPath)) File.Delete(settingsPath);
        }

        Console.WriteLine("Macro end notification checks passed.");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
