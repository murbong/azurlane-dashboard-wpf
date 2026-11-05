using AzurLaneDashboard.Core.Models;
using AzurLaneDashboard.Core.Services;
using AzurLaneDashboard.Data.Services;
using AzurLaneDashboard.Resources;

namespace AzurLaneDashboard.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private async Task RunUpdateAsync()
    {
        if (isDisposed || IsUpdating)
        {
            return;
        }

        var toolPreparation = await PrepareAndroidToolsForUpdateAsync().ConfigureAwait(true);
        if (isDisposed || !toolPreparation.ShouldContinue)
        {
            return;
        }

        SaveSettings();
        IsUpdating = true;
        UpdateStateLabel = "실행 중";
        UpdateProgressStage = "준비";
        UpdateProgressMessage = "업데이트 준비 중";
        UpdateProgressValue = 0;
        UpdateProgressIsIndeterminate = true;
        Status = "업데이트 실행 중...";
        Replace(UpdateLogLines, [new OperationLogLine { Text = "업데이트를 시작합니다." }]);
        if (!string.IsNullOrWhiteSpace(toolPreparation.StartupLogMessage))
        {
            AddUpdateLog(toolPreparation.StartupLogMessage);
        }

        RefreshUpdateStatusRows(null);
        updateCancellation?.Dispose();
        updateCancellation = new CancellationTokenSource();
        var cancellation = updateCancellation;
        var progress = new Progress<UpdateProgress>(OnUpdateProgress);
        var updateOptions = BuildUpdateOptions(progress, toolPreparation.SkipGameResourcesThisRun);

        try
        {
            var result = await Task.Run(
                    async () => await dataUpdateService.RunAsync(updateOptions, cancellation.Token)
                        .ConfigureAwait(false),
                    cancellation.Token)
                .ConfigureAwait(true);
            if (isDisposed)
            {
                return;
            }

            UpdateStateLabel = result.Success ? "완료" : "실패";
            Status = result.Success ? "업데이트 완료" : $"업데이트 실패: {result.Error}";
            UpdateProgressStage = result.Success ? "완료" : "실패";
            UpdateProgressMessage = result.Success ? "업데이트 완료" : result.Error;
            UpdateProgressIsIndeterminate = false;
            UpdateProgressValue = result.Success ? 100 : UpdateProgressValue;
            RefreshUpdateStatusRows(result);
            FlushPendingUpdateLogs();
            if (result.Success)
            {
                await ReloadAsync(persistSettings: false);
            }
        }
        catch (OperationCanceledException)
        {
            UpdateStateLabel = "취소됨";
            Status = "업데이트 취소됨";
            UpdateProgressStage = "취소됨";
            UpdateProgressMessage = "업데이트가 취소되었습니다.";
            UpdateProgressIsIndeterminate = false;
            FlushPendingUpdateLogs();
            AddUpdateLog("업데이트가 취소되었습니다.");
            RefreshUpdateStatusRows(null);
        }
        finally
        {
            FlushPendingUpdateLogs();
            if (ReferenceEquals(updateCancellation, cancellation))
            {
                updateCancellation = null;
            }

            cancellation.Dispose();
            IsUpdating = false;
        }
    }

    private void OnUpdateProgress(UpdateProgress progress)
    {
        if (isDisposed)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(progress.Stage))
        {
            UpdateProgressStage = progress.Stage;
        }

        if (!string.IsNullOrWhiteSpace(progress.Message))
        {
            UpdateProgressMessage = progress.Message;
        }

        if (!progress.AppendLog)
        {
            UpdateProgressIsIndeterminate = progress.IsIndeterminate;
            if (progress.Percent.HasValue)
            {
                UpdateProgressValue = progress.Percent.Value;
                UpdateProgressIsIndeterminate = false;
            }
        }

        if (progress.AppendLog && !string.IsNullOrWhiteSpace(progress.Message))
        {
            QueueUpdateLog(progress.Message);
        }

        RefreshUpdateStatusRows(null);
    }

    private UpdateOptions BuildUpdateOptions(
        IProgress<UpdateProgress>? progress = null,
        bool skipGameResourcesThisRun = false)
    {
        return new UpdateOptions
        {
            ProjectRoot = ProjectLocator.FindProjectRoot(),
            DatabasePath = DatabasePath,
            Region = Region,
            Serial = Serial,
            AdbPath = AdbPath,
            MuMuManagerPath = MuMuManagerPath,
            DataDirectory = DataDirectory,
            LuaRepoDirectory = LuaRepoDirectory,
            LuaBranch = LuaBranch,
            MapsDirectory = MapsDirectory,
            GameResourcesDirectory = GameResourcesDirectory,
            StaticAssetsDirectory = StaticAssetsDirectory,
            LocalAssetManifest = LocalAssetManifest,
            UpdateStatePath = UpdateStatePath,
            Categories = ParseCategories(CategoriesText),
            ProgressEvery = ProgressEvery,
            SkipGit = SkipGit,
            SkipRegal = SkipRegal,
            SkipGameResources = SkipGameResources || skipGameResourcesThisRun,
            RequireGameResources = RequireGameResources,
            MetadataOnly = MetadataOnly,
            SkipScripts = SkipScripts,
            ForceGameResources = ForceGameResources,
            SkipIconExtract = SkipIconExtract,
            ForceIconExtract = ForceIconExtract,
            Progress = progress
        };
    }
}
