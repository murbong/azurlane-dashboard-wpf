using System.IO;
using System.Windows;
using AzurLaneDashboard.App.Services;
using AzurLaneDashboard.Core.Models;
using AzurLaneDashboard.Core.Services;
using AzurLaneDashboard.Data.Services;
using AzurLaneDashboard.Resources;

namespace AzurLaneDashboard.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private bool reloadRequested;
    public async void StartInitialLoad(bool updateData = false, bool startCapture = false)
    {
        if (isDisposed)
        {
            return;
        }

        if (!autoRefreshTimer.IsEnabled)
        {
            autoRefreshTimer.Start();
        }

        await ReloadAsync(persistSettings: false).ConfigureAwait(true);
        if (isDisposed) return;
        if (updateData)
        {
            SelectedMainTabIndex = 9;
            try
            {
                await RunUpdateAsync().ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                Status = $"업데이트 실패: {ex.Message}";
                UpdateStateLabel = "실패";
                UpdateProgressMessage = ex.Message;
                AddUpdateLog(Status);
            }
        }
        else
        {
            PromptForInitialUpdateIfNeeded();
        }
        if (!isDisposed && startCapture) StartCapture();
    }

    private async Task ReloadAsync(bool persistSettings = true)
    {
        if (isDisposed || IsCompactingDatabase)
        {
            return;
        }
        if (IsReloading)
        {
            reloadRequested = true;
            return;
        }

        IsReloading = true;
        lastReloadStartedUtc = DateTime.UtcNow;
        Status = "로드 중...";
        reloadCancellation?.Dispose();
        reloadCancellation = new CancellationTokenSource();
        var cancellation = reloadCancellation;
        var token = cancellation.Token;

        try
        {
            var path = DatabasePath;
            var observedStamp = GetDatabaseChangeStamp();
            var snapshot = await Task.Run(() => inventoryService.Load(path, token), token);
            token.ThrowIfCancellationRequested();
            if (isDisposed)
            {
                return;
            }
            if (!string.Equals(path, DatabasePath, StringComparison.OrdinalIgnoreCase))
            {
                // A newly selected database must not be overwritten by the previous load.
                reloadRequested = true;
                return;
            }

            using (ShipsView.DeferRefresh())
            using (GearGapsView.DeferRefresh())
            using (EquipmentView.DeferRefresh())
            using (EquipmentRecommendationsView.DeferRefresh())
            using (SpecialEquipmentView.DeferRefresh())
            using (EquipmentSkinsView.DeferRefresh())
            using (CommunityGearShortagesView.DeferRefresh())
            {
                ApplySnapshot(snapshot);
            }

            WriteLiveState();

            LastLoadedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            UpdateDatabaseWriteInfo(observedStamp);
            RefreshShipViews();
            RefreshEquipmentViews();
            NotifyCounts();
            if (persistSettings)
            {
                SaveSettings();
            }
        }
        catch (OperationCanceledException)
        {
            Status = "로드 취소됨";
        }
        catch (Exception ex)
        {
            Status = $"로드 실패: {ex.Message}";
        }
        finally
        {
            if (ReferenceEquals(reloadCancellation, cancellation))
            {
                reloadCancellation = null;
            }

            cancellation.Dispose();
            IsReloading = false;
            if (reloadRequested && !isDisposed)
            {
                reloadRequested = false;
                await ReloadAsync();
            }
        }
    }

    private void PromptForInitialUpdateIfNeeded()
    {
        if (initialUpdatePromptShown || IsUpdating || !ResourcesLookEmpty())
        {
            return;
        }

        initialUpdatePromptShown = true;
        var answer = AppDialogService.Show(
            "게임 리소스와 이미지 캐시가 아직 없습니다.\n\n지금 업데이트를 실행하시겠습니까?",
            "초기 리소스 업데이트",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question,
            MessageBoxResult.Yes);
        if (answer != MessageBoxResult.Yes)
        {
            return;
        }

        SelectedMainTabIndex = 9;
        _ = RunUpdateAsync();
    }

    private bool ResourcesLookEmpty()
    {
        var staticAssetsRoot = ProjectLocator.ResolveContentPath(StaticAssetsDirectory);

        return !HasLocalAssetBundles() &&
               !DirectoryHasFiles(staticAssetsRoot, "*.png");
    }

    private bool HasLocalAssetBundles()
    {
        var gameResourceRoot = ProjectLocator.ResolveContentPath(GameResourcesDirectory);
        var assetBundleDirectory = Path.Combine(gameResourceRoot, Region, "AssetBundles");
        return DirectoryHasFiles(assetBundleDirectory, "*");
    }

    private bool HasAndroidBridge()
    {
        return ResolveAndroidBridge().IsAvailable;
    }

    private AndroidBridge ResolveAndroidBridge()
    {
        return ExternalToolLocator.ResolveAndroidBridge(AdbPath, MuMuManagerPath, ProjectLocator.FindProjectRoot());
    }

    private void RefreshAndroidToolStatus()
    {
        var bridge = ResolveAndroidBridge();
        AndroidToolStatus = bridge.IsAvailable
            ? bridge.DisplayName
            : "ADB/MuMuManager 없음. tools/platform-tools/adb.exe 또는 MuMuManager 경로를 설정하세요.";
    }

    private static bool DirectoryHasFiles(string directory, string searchPattern)
    {
        try
        {
            return Directory.Exists(directory) &&
                   Directory.EnumerateFiles(directory, searchPattern, SearchOption.AllDirectories).Any();
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private void ApplySnapshot(DashboardSnapshot snapshot)
    {
        GrowthPlanner.ApplySnapshot(snapshot);
        FleetPlanner.ApplySnapshot(snapshot);
        RememberLiveProgress(snapshot);
        var selectedFleetId = SelectedFleetCompositionGroup?.FleetId;
        DatabasePath = snapshot.DatabasePath;
        Auction = snapshot.Auction;
        MonthlyShop = snapshot.MonthlyShop;
        Meowfficers.ApplySnapshot(snapshot.Meowfficers);
        Opsi.ApplySnapshot(snapshot.Opsi);
        Replace(Kpis, snapshot.Kpis);
        Replace(Ships, snapshot.Ships);
        Replace(Equipment, snapshot.Equipment);
        Replace(EquipmentRecommendations, snapshot.EquipmentRecommendations);
        Replace(SpecialEquipment, snapshot.SpecialEquipment);
        Replace(EquipmentSkins, snapshot.EquipmentSkins);
        Replace(LevelPriorities, snapshot.LevelPriorities);
        Replace(GearGaps, snapshot.GearGaps);
        Replace(BattleRuns, snapshot.BattleRuns);
        Replace(OverviewBattleRuns, snapshot.BattleRuns.Take(12));
        Replace(FleetDamageRows, snapshot.FleetDamageRows);
        Replace(FleetCompositionGroups, snapshot.FleetCompositionGroups);
        RestoreSelectedFleetCompositionGroup(selectedFleetId);
        Replace(SkillPriorities, snapshot.SkillPriorities);
        Replace(SystemStatuses, snapshot.SystemStatuses);
        Replace(AcademyRooms, snapshot.AcademyRooms);
        Replace(ResearchQueue, snapshot.ResearchQueue);
        Replace(Commissions, snapshot.Commissions);
        Replace(Builds, snapshot.Builds);
        Replace(Resources, snapshot.Resources);
        Replace(CommunityGearShortages, snapshot.CommunityGearShortages);
        Replace(CleanupCandidates, snapshot.CleanupCandidates);
        Replace(PacketStats, snapshot.PacketStats);
        Replace(PacketInterpretationGaps, snapshot.PacketInterpretationGaps);
        Replace(DecodeFailures, snapshot.DecodeFailures);
        RebuildShipLookup();
        // The queue was rebuilt before the lookup; filter it again against this snapshot's ships.
        FleetPlanner.RefreshQueueFilter();
        RefreshShipFilterOptions();
        ApplyShipSort();
        ApplyEquipmentSort();
        RestoreSelections();

        PacketsSeen = snapshot.PacketsSeen;
        KnownPacketsSeen = snapshot.KnownPacketsSeen;
        KnownPacketsDecoded = snapshot.KnownPacketsDecoded;
        PacketPayloadsSaved = snapshot.PacketPayloadsSaved;
        DecodeFailuresSaved = snapshot.DecodeFailuresSaved;
        LiveUpdatedAt = string.IsNullOrWhiteSpace(snapshot.LiveUpdatedAt) ? "-" : snapshot.LiveUpdatedAt;
        GeneratedAt = string.IsNullOrWhiteSpace(snapshot.GeneratedAt) ? "-" : snapshot.GeneratedAt;
        CaptureStatusLabel = string.IsNullOrWhiteSpace(snapshot.CaptureStatusLabel) ? "-" : snapshot.CaptureStatusLabel;
        CaptureStatusDetail =
            string.IsNullOrWhiteSpace(snapshot.CaptureStatusDetail) ? "-" : snapshot.CaptureStatusDetail;
        // Recent records describe past traffic, not ownership of a currently running capture.
        // Treating a snapshot as live blocks StartCapture after restarting the application.
        IsCaptureRunning = CaptureTargets.Any(target => target.IsRunning);
        DockUsed = snapshot.DockUsed;
        DockMax = snapshot.DockMax;
        DockExemptCount = snapshot.DockExemptCount;
        EquipmentBagCount = snapshot.EquipmentBagCount;
        EquipmentBagMax = snapshot.EquipmentBagMax;
        EquipmentEquippedCount = snapshot.EquipmentEquippedCount;
        BattleSummaryText = string.IsNullOrWhiteSpace(snapshot.BattleSummaryText)
            ? "전투 결과 패킷 대기"
            : snapshot.BattleSummaryText;
        BattleSubText = snapshot.BattleSubText;
        FleetTechSummaryText = snapshot.FleetTechSummaryText;
        CommunitySummaryText = snapshot.CommunitySummaryText;
        SnapshotSource = snapshot.SnapshotSource;
        SnapshotWarning = snapshot.SnapshotWarning;
        CurrentStateMode = snapshot.CurrentStateMode;
        CurrentStateLastDecodedRowId = snapshot.CurrentStateLastDecodedRowId;
        CurrentStateLastStatus = snapshot.CurrentStateLastStatus;
        CurrentStateLastArea = snapshot.CurrentStateLastArea;
        CurrentStateLastDetail = snapshot.CurrentStateLastDetail;
        CurrentStateLastError = snapshot.CurrentStateLastError;
        OnPropertyChanged(nameof(CurrentStateStatusText));
        OnPropertyChanged(nameof(HasFleetCompositionGroups));
        RefreshCaptureStatusRows();
        Status = string.IsNullOrWhiteSpace(snapshot.SnapshotWarning)
            ? snapshot.Status
            : $"{snapshot.Status} · 경고: {snapshot.SnapshotWarning}";
    }

    private void RestoreSelectedFleetCompositionGroup(int? selectedFleetId)
    {
        SelectedFleetCompositionGroup = selectedFleetId.HasValue
            ? FleetCompositionGroups.FirstOrDefault(group => group.FleetId == selectedFleetId.Value) ??
              FleetCompositionGroups.FirstOrDefault()
            : FleetCompositionGroups.FirstOrDefault();
    }

}
