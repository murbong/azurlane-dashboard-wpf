using System.Windows.Threading;
using AzurLaneDashboard.Data.Services;


namespace AzurLaneDashboard.App.ViewModels;

/// <summary>
/// While the app captures, it reloads as soon as a packet changes the stored state (equipment, fleets, battles…)
/// rather than when the 2 s file poll next notices the database moved. Packets with no state effect — most of
/// the traffic — no longer trigger a full reload, so a real change is not stuck behind one.
/// </summary>
public sealed partial class MainWindowViewModel
{
    // Coalesces the request/response pair and bursts such as an equipment swap (unequip + equip) into one reload.
    private static readonly TimeSpan LiveReloadDelay = TimeSpan.FromMilliseconds(150);
    // A steady packet stream (a battle) would keep pushing the debounce back; reload at least this often.
    private static readonly TimeSpan LiveReloadMaxWait = TimeSpan.FromSeconds(1);
    // While capture runs the file poll is only a safety net; captured changes arrive through StateApplied.
    private static readonly TimeSpan CapturePollFallback = TimeSpan.FromSeconds(15);

    private DispatcherTimer liveReloadTimer = null!;
    private Dispatcher uiDispatcher = null!;
    private DateTime lastReloadStartedUtc;
    private DateTime liveReloadPendingSinceUtc;

    private void InitializeLiveReload()
    {
        uiDispatcher = Dispatcher.CurrentDispatcher;
        liveReloadTimer = new DispatcherTimer { Interval = LiveReloadDelay };
        liveReloadTimer.Tick += LiveReloadTimerTick;
    }

    private void StopLiveReload()
    {
        liveReloadTimer.Stop();
        liveReloadTimer.Tick -= LiveReloadTimerTick;
    }

    // Capture thread → UI thread; restarting the timer keeps a burst of changes to one reload. Only the account on
    // screen reloads: the other emulators' accounts are read when they are picked.
    private void OnCaptureStateApplied(GameAccount account, StateApplyResult result)
    {
        if (isDisposed) return;
        uiDispatcher.BeginInvoke(() =>
        {
            if (isDisposed || !IsAutoRefreshEnabled || !IsDisplayedAccount(account)) return;
            var now = DateTime.UtcNow;
            if (!liveReloadTimer.IsEnabled)
            {
                liveReloadPendingSinceUtc = now;
            }
            else if (now - liveReloadPendingSinceUtc >= LiveReloadMaxWait)
            {
                return;
            }

            liveReloadTimer.Stop();
            liveReloadTimer.Start();
        });
    }

    private void LiveReloadTimerTick(object? sender, EventArgs e)
    {
        liveReloadTimer.Stop();
        if (isDisposed || !IsAutoRefreshEnabled) return;
        _ = ReloadAsync(persistSettings: false);
    }

    private bool PollWaitsForCapture() =>
        IsCaptureRunning && DateTime.UtcNow - lastReloadStartedUtc < CapturePollFallback;
}
