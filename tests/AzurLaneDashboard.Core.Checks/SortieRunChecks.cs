using System.Text.Json;
using AzurLaneDashboard.Core.Services;

/// <summary>The 연속 작전 watcher on packet sequences shaped like the captured ones (12-4 cleared three times).</summary>
internal static class SortieRunChecks
{
    private static readonly DateTime Start = new(2026, 10, 30, 19, 28, 58);

    public static void Run()
    {
        CheckRunEnds();
        CheckSingleSortie();
        CheckStall();
        CheckRefusedEntry();
        CheckLastExitWithoutClear();
        Console.WriteLine("Sortie run notification checks passed.");
    }

    private static void CheckRunEnds()
    {
        var watcher = new SortieRunWatcher();
        var at = Start;
        for (var run = 0; run < 3; run++) at = Sortie(watcher, at, cleared: true) + TimeSpan.FromSeconds(1);
        var lastExit = at - TimeSpan.FromSeconds(1);
        Ensure(watcher.Check(lastExit + TimeSpan.FromSeconds(30), "12-4") is null, "no notice while the next entry may still come");
        var notice = watcher.Check(lastExit + SortieRunWatcher.ReentryWindow, "12-4");
        Ensure(notice is { IsWarning: false, Title: "연속 작전이 끝났습니다" } &&
               notice.Detail.StartsWith("12-4 3회 출격 · 3회 클리어 · 19:41 종료 (12분)", StringComparison.Ordinal),
            "a run that stops re-entering notifies once with its count: " + notice?.Detail);
        Ensure(watcher.Check(lastExit + TimeSpan.FromMinutes(10), "12-4") is null, "the end is notified once");
    }

    private static void CheckSingleSortie()
    {
        var watcher = new SortieRunWatcher();
        var exit = Sortie(watcher, Start, cleared: true);
        Ensure(watcher.Check(exit + TimeSpan.FromMinutes(2), "12-4") is null, "one sortie by hand is not a run");
        // Entering again after the window starts a new run rather than continuing the old one.
        exit = Sortie(watcher, exit + TimeSpan.FromMinutes(2), cleared: true);
        Ensure(watcher.Entries == 1 && watcher.Check(exit + TimeSpan.FromMinutes(2), "12-4") is null,
            "sorties minutes apart are separate");
    }

    private static void CheckStall()
    {
        var watcher = new SortieRunWatcher();
        var at = Sortie(watcher, Start, cleared: true) + TimeSpan.FromSeconds(1);
        Enter(watcher, at);
        var last = at + TimeSpan.FromSeconds(30);
        Observe(watcher, 13103, "CS", """{"act":8,"group_id":0}""", last);
        // A battle of another mode while the fleet still sits in the map is not progress there.
        Observe(watcher, 40004, "SC", """{"drop_info":[]}""", last + TimeSpan.FromMinutes(3));
        Ensure(watcher.Check(last + TimeSpan.FromMinutes(4), "12-4") is null, "a long battle is not a stall");
        var stall = watcher.Check(last + SortieRunWatcher.StallAfter, "12-4");
        Ensure(stall is { IsWarning: true, Title: "연속 작전이 멈춘 것 같습니다" } && stall.Detail.Contains("2번째 출격", StringComparison.Ordinal),
            "nothing moving inside the map for five minutes notifies a stall");
        Observe(watcher, 13103, "CS", """{"act":1,"group_id":1,"act_arg_1":3,"act_arg_2":4}""", last + TimeSpan.FromMinutes(7));
        Ensure(watcher.Check(last + TimeSpan.FromMinutes(13), "12-4") is null, "a stall is notified once per sortie");

        // Leaving the map later without another sortie is tidying up, not a second notice.
        var exit = last + TimeSpan.FromMinutes(14);
        Observe(watcher, 13103, "CS", """{"act":0,"group_id":0,"act_arg_1":1}""", exit);
        Ensure(watcher.Check(exit + SortieRunWatcher.ReentryWindow, "12-4") is null, "no end notice after a reported stall");

        // A run that resumed after the stall notifies its end again.
        watcher = new SortieRunWatcher();
        at = Sortie(watcher, Start, cleared: true) + TimeSpan.FromSeconds(1);
        Enter(watcher, at);
        Ensure(watcher.Check(at + SortieRunWatcher.StallAfter, "12-4") is { Title: "연속 작전이 멈춘 것 같습니다" }, "stalled");
        exit = at + TimeSpan.FromMinutes(20);
        Observe(watcher, 13103, "CS", """{"act":0,"group_id":0,"act_arg_1":1}""", exit);
        exit = Sortie(watcher, exit + TimeSpan.FromSeconds(1), cleared: true);
        Ensure(watcher.Check(exit + SortieRunWatcher.ReentryWindow, "12-4") is { Title: "연속 작전이 끝났습니다" } resumed &&
               resumed.Detail.StartsWith("12-4 3회 출격", StringComparison.Ordinal),
            "a run that resumed after a stall still notifies its end");

        // Another map entered right after is a different run.
        watcher = new SortieRunWatcher();
        exit = Sortie(watcher, Start, cleared: true);
        Observe(watcher, 13101, "CS", """{"id":11404}""", exit + TimeSpan.FromSeconds(30));
        Observe(watcher, 13102, "SC", """{"result":0,"current_chapter":{"id":11404}}""", exit + TimeSpan.FromSeconds(30));
        Ensure(watcher.Entries == 1 && watcher.ChapterId == 11404, "a run continues only on the same map");
    }

    private static void CheckRefusedEntry()
    {
        var watcher = new SortieRunWatcher();
        var at = Sortie(watcher, Start, cleared: true) + TimeSpan.FromSeconds(1);
        at = Sortie(watcher, at, cleared: true) + TimeSpan.FromSeconds(1);
        Observe(watcher, 13101, "CS", """{"id":1204}""", at);
        Observe(watcher, 13102, "SC", """{"result":3}""", at);
        var notice = watcher.Check(at + TimeSpan.FromSeconds(2), "12-4");
        Ensure(notice is { IsWarning: true } && notice.Detail.StartsWith("다음 출격이 거절되었습니다 (코드 3).", StringComparison.Ordinal),
            "a refused next entry notifies right away as a warning");
    }

    private static void CheckLastExitWithoutClear()
    {
        var watcher = new SortieRunWatcher();
        var at = Sortie(watcher, Start, cleared: true) + TimeSpan.FromSeconds(1);
        var exit = Sortie(watcher, at, cleared: false);
        Ensure(watcher.Check(exit + SortieRunWatcher.ReentryWindow, "") is
                   { IsWarning: true, Title: "연속 작전이 멈췄습니다" } notice &&
               notice.Detail.Contains("해역 1204 2회 출격 · 1회 클리어", StringComparison.Ordinal),
            "leaving the last map without a clear is a warning, and an unknown stage falls back to the chapter id");
    }

    // One sortie shaped like 12-4: entry, a move, a battle, then leaving the map four minutes later.
    private static DateTime Sortie(SortieRunWatcher watcher, DateTime at, bool cleared)
    {
        Enter(watcher, at);
        Observe(watcher, 13103, "CS", """{"act":1,"group_id":1,"act_arg_1":7,"act_arg_2":5}""", at + TimeSpan.FromSeconds(5));
        Observe(watcher, 40004, "SC", """{"drop_info":[]}""", at + TimeSpan.FromSeconds(40));
        var exit = at + TimeSpan.FromMinutes(4);
        Observe(watcher, 13103, "CS", cleared ? """{"act":0,"group_id":0,"act_arg_1":1}""" : """{"act":0,"group_id":0}""", exit);
        return exit;
    }

    private static void Enter(SortieRunWatcher watcher, DateTime at)
    {
        Observe(watcher, 13101, "CS", """{"id":1204,"operation_item":1}""", at);
        Observe(watcher, 13102, "SC", """{"result":0,"current_chapter":{"id":1204,"time":1785699085}}""", at);
    }

    private static void Observe(SortieRunWatcher watcher, int packetId, string direction, string json, DateTime at)
    {
        using var document = JsonDocument.Parse(json);
        watcher.Observe(packetId, direction, document.RootElement, at);
    }

    private static void Ensure(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
