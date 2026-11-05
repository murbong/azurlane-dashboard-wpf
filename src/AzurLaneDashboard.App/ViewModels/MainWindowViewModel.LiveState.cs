using AzurLaneDashboard.App.Services;
using AzurLaneDashboard.Core.Models;

namespace AzurLaneDashboard.App.ViewModels;

/// <summary>
/// The packet state for macros: handed to the macro page in-process, and optionally written as live_state.json for
/// ALAuto's Python viewer. Built after each load, when capture starts or stops, and as a heartbeat every few
/// seconds so a reader can tell a live dashboard from a stale one.
/// </summary>
public sealed partial class MainWindowViewModel
{
    private static readonly TimeSpan LiveStateHeartbeat = TimeSpan.FromSeconds(10);

    private bool isLiveStateExportEnabled;
    private DateTime liveStateWrittenUtc;
    private bool liveStateWritePending;
    private LatestBattleInfo? latestBattle;
    private int currentChapterId;
    private OilEstimate? oilEstimate;
    private DormFoodState? dormFood;

    public bool IsLiveStateExportEnabled
    {
        get => isLiveStateExportEnabled;
        set
        {
            if (SetProperty(ref isLiveStateExportEnabled, value))
            {
                SaveSettings();
                WriteLiveState();
            }
        }
    }

    public string LiveStatePathText => LiveStateWriter.PathFor(DatabasePath);

    private void RememberLiveProgress(DashboardSnapshot snapshot)
    {
        liveStatePaused = false;
        latestBattle = snapshot.LatestBattle;
        currentChapterId = snapshot.CurrentChapterId;
        oilEstimate = snapshot.Oil;
        dormFood = snapshot.DormFood;
        OnPropertyChanged(nameof(LiveStatePathText));
    }

    private void WriteLiveState(bool? captureRunning = null)
    {
        // Macros is null while the constructor is still loading settings; after an account switch the state is the
        // old account's until the new one loads.
        if (Macros is null || liveStatePaused)
        {
            return;
        }

        var document = FleetPlanner.BuildLiveState(DateTimeOffset.Now, captureRunning ?? IsCaptureRunning,
            latestBattle, currentChapterId, oilEstimate, dormFood);
        Macros.UpdateGameState(document);
        liveStateWrittenUtc = DateTime.UtcNow;
        if (isLiveStateExportEnabled)
        {
            liveStateWritePending = !LiveStateWriter.TryWrite(LiveStateWriter.PathFor(DatabasePath), document);
        }
    }

    private void WriteLiveStateHeartbeat()
    {
        if (liveStateWritePending || DateTime.UtcNow - liveStateWrittenUtc >= LiveStateHeartbeat)
        {
            WriteLiveState();
        }
    }
}
