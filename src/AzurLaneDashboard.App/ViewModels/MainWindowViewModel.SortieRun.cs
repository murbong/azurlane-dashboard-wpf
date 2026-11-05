using System.Text.Json;
using AzurLaneDashboard.Capture;
using AzurLaneDashboard.Core.Services;

namespace AzurLaneDashboard.App.ViewModels;

/// <summary>
/// The game's 연속 작전 read from captured chapter packets, so the window can notify when a run ends or stalls
/// (see <see cref="SortieRunWatcher"/>). Each account has its own watcher, so two emulators' runs never mix. A
/// macro run reports its own end, so runs it drives do not notify twice.
/// </summary>
public sealed partial class MainWindowViewModel
{
    // A macro taps 재출격 itself; a run that ends this soon after the macro stopped was the macro's.
    private static readonly TimeSpan MacroSortieGrace = TimeSpan.FromMinutes(3);

    private readonly Dictionary<long, SortieRunWatcher> sortieRunWatchers = new();
    private bool notifySortieEnd = true;
    private DateTime macroStoppedAt = DateTime.MinValue;
    private long macroAccountUid;

    /// <summary>Raised on the UI thread when a 연속 작전 ends or stalls while <see cref="NotifySortieEnd"/> is on.</summary>
    public event EventHandler<SortieRunNotice>? SortieNotice;

    public bool NotifySortieEnd
    {
        get => notifySortieEnd;
        set
        {
            if (SetProperty(ref notifySortieEnd, value)) SaveSettings();
        }
    }

    private void InitializeSortieWatch()
    {
        Macros.RunningChanged += OnMacroRunningChanged;
    }

    private void StopSortieWatch()
    {
        Macros.RunningChanged -= OnMacroRunningChanged;
    }

    // Capture thread: only chapter and battle packets go on to the UI thread, stamped with when they arrived.
    private void OnPacketDecoded(CapturedPacket packet)
    {
        if (isDisposed || !SortieRunWatcher.IsRelevant(packet.PacketId)) return;
        var at = DateTime.Now;
        JsonElement body;
        try
        {
            using var document = JsonDocument.Parse(packet.Json);
            body = document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return;
        }

        uiDispatcher.BeginInvoke(() =>
        {
            if (isDisposed) return;
            if (!sortieRunWatchers.TryGetValue(packet.AccountUid, out var watcher))
            {
                watcher = new SortieRunWatcher();
                sortieRunWatchers[packet.AccountUid] = watcher;
            }

            watcher.Observe(packet.PacketId, packet.Direction, body, at);
        });
    }

    // The macro drives the emulator of the account on screen when it starts.
    private void OnMacroRunningChanged(object? sender, EventArgs e)
    {
        if (Macros.IsRunning)
        {
            macroAccountUid = DisplayedAccount?.Uid ?? 0;
        }
        else
        {
            macroStoppedAt = DateTime.Now;
        }

        RefreshAccountSwitchState();
    }

    // Every status tick. A watcher hands out each notice once, so one held back for a macro is not sent later.
    private void CheckSortieRun()
    {
        var now = DateTime.Now;
        var displayedUid = DisplayedAccount?.Uid ?? 0;
        foreach (var (uid, watcher) in sortieRunWatchers)
        {
            // The stage name comes from the loaded snapshot, which is the account on screen only.
            var stage = uid == displayedUid ? latestBattle?.StageLabel ?? "" : "";
            if (watcher.Check(now, stage) is not { } notice) continue;
            if (!NotifySortieEnd) continue;
            if (uid == macroAccountUid && (Macros.IsRunning || now - macroStoppedAt < MacroSortieGrace)) continue;
            SortieNotice?.Invoke(this, Accounts.Count > 1 && accountRegistry.Find(uid) is { } account
                ? notice with { Detail = $"{account.DisplayName} · {notice.Detail}" }
                : notice);
        }
    }
}
