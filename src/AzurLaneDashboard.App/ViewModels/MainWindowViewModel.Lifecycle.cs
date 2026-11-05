using System.Collections;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Data;

namespace AzurLaneDashboard.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private void RefreshAllViews()
    {
        foreach (var collection in new IEnumerable[]
                 {
                     Ships, Equipment, EquipmentRecommendations, SpecialEquipment, EquipmentSkins, LevelPriorities,
                     GearGaps, BattleRuns, OverviewBattleRuns, FleetDamageRows, FleetCompositionGroups, SkillPriorities,
                     SystemStatuses, AcademyRooms, ResearchQueue, Commissions, Builds, Resources,
                     CommunityGearShortages, CleanupCandidates, PacketStats, PacketInterpretationGaps, DecodeFailures
                  })
        {
            CollectionViewSource.GetDefaultView(collection)?.Refresh();
        }
    }

    private async Task AutoRefreshTickAsync()
    {
        WriteLiveStateHeartbeat();
        if (!IsAutoRefreshEnabled || IsReloading || IsCompactingDatabase || PollWaitsForCapture())
        {
            return;
        }

        try
        {
            var stamp = GetDatabaseChangeStamp();
            if (stamp.Exists && stamp != lastObservedDatabaseChangeStamp)
            {
                await ReloadAsync(persistSettings: false);
            }
        }
        catch (Exception ex)
        {
            Status = $"자동 갱신 실패: {ex.Message}";
        }
    }

    private async void AutoRefreshTimerTick(object? sender, EventArgs e)
    {
        if (isDisposed)
        {
            return;
        }

        await AutoRefreshTickAsync();
    }

    private void UpdateDatabaseWriteInfo(DatabaseChangeStamp? observedStamp = null)
    {
        var stamp = GetDatabaseChangeStamp();
        // Writes that happen during a load must remain visible to the next refresh.
        lastObservedDatabaseChangeStamp = observedStamp ?? stamp;
        var writeUtc = stamp.LastWriteUtc;
        DatabaseLastWriteAt = writeUtc == DateTime.MinValue
            ? "-"
            : writeUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
    }

    private DatabaseChangeStamp GetDatabaseChangeStamp()
    {
        if (string.IsNullOrWhiteSpace(DatabasePath))
        {
            return default;
        }

        return new DatabaseChangeStamp(
            ReadDatabaseFileStamp(DatabasePath),
            ReadDatabaseFileStamp(DatabasePath + "-wal"),
            ReadDatabaseFileStamp(DatabasePath + "-shm"));
    }

    private static DatabaseFileStamp ReadDatabaseFileStamp(string path)
    {
        var info = new FileInfo(path);
        return info.Exists
            ? new DatabaseFileStamp(true, info.Length, info.LastWriteTimeUtc.Ticks)
            : default;
    }

    private readonly record struct DatabaseFileStamp(bool Exists, long Length, long LastWriteTicks);

    private readonly record struct DatabaseChangeStamp(
        DatabaseFileStamp Database,
        DatabaseFileStamp Wal,
        DatabaseFileStamp SharedMemory)
    {
        public bool Exists => Database.Exists || Wal.Exists || SharedMemory.Exists;

        public DateTime LastWriteUtc
        {
            get
            {
                var ticks = Math.Max(Database.LastWriteTicks, Math.Max(Wal.LastWriteTicks, SharedMemory.LastWriteTicks));
                return ticks > 0 ? new DateTime(ticks, DateTimeKind.Utc) : DateTime.MinValue;
            }
        }
    }

    private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> source)
    {
        if (target is BulkObservableCollection<T> bulk)
        {
            bulk.ReplaceRange(source);
            return;
        }

        target.Clear();
        foreach (var item in source)
        {
            target.Add(item);
        }
    }

    public void Dispose()
    {
        if (isDisposed)
        {
            return;
        }

        var saveSettingsPending = settingsSaveTimer.IsEnabled;
        settingsSaveTimer.Stop();
        settingsSaveTimer.Tick -= SettingsSaveTimerTick;
        if (saveSettingsPending)
        {
            SaveSettingsNow();
        }

        isDisposed = true;
        PropertyChanged -= NavigationStateChanged;
        reloadCancellation?.Cancel();
        reloadCancellation?.Dispose();
        updateCancellation?.Cancel();
        updateCancellation?.Dispose();
        Macros.Dispose();
        StopAllCaptures();
        accountRegistry.Changed -= OnAccountsChanged;
        // Tell macro tools the dashboard is gone rather than leaving a file that looks live until it goes stale.
        WriteLiveState(captureRunning: false);
        reloadCancellation = null;
        updateCancellation = null;
        pendingUpdateLogLines.Clear();
        autoRefreshTimer.Stop();
        autoRefreshTimer.Tick -= AutoRefreshTimerTick;
        StopLiveReload();
        StopSortieWatch();
        operationStatusTimer.Stop();
        operationStatusTimer.Tick -= OperationStatusTimerTick;
        updateLogFlushTimer.Stop();
        updateLogFlushTimer.Tick -= UpdateLogFlushTimerTick;
        searchRefreshTimer.Stop();
        searchRefreshTimer.Tick -= SearchRefreshTimerTick;
    }
}
