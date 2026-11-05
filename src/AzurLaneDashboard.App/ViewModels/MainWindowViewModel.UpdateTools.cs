using System.Windows;
using AzurLaneDashboard.App.Services;
using AzurLaneDashboard.Core.Services;

namespace AzurLaneDashboard.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private readonly record struct UpdateToolPreparation(
        bool ShouldContinue,
        bool SkipGameResourcesThisRun,
        string StartupLogMessage);

    private sealed record AndroidDeviceCheckResult(bool IsReady, string Message);

    private sealed record AndroidBridgeProcessResult(int ExitCode, string StandardOutput, string StandardError);


    private async Task<UpdateToolPreparation> PrepareAndroidToolsForUpdateAsync()
    {
        RefreshAndroidToolStatus();
        if (!NeedsAndroidBridgeForUpdate())
        {
            return new UpdateToolPreparation(true, false, "");
        }

        while (true)
        {
            var bridge = ResolveAndroidBridge();
            if (!bridge.IsAvailable)
            {
                var missingToolAction = PromptForMissingAndroidBridge();
                if (!missingToolAction.ShouldContinue || missingToolAction.SkipGameResourcesThisRun)
                {
                    return missingToolAction;
                }

                RefreshAndroidToolStatus();
                continue;
            }

            Status = "Android 장치 확인 중...";
            var device = await CheckAndroidDeviceReadyAsync(bridge, CancellationToken.None).ConfigureAwait(true);
            if (device.IsReady)
            {
                Status = "Android 장치 준비됨";
                return new UpdateToolPreparation(true, false, "");
            }

            var deviceAction = PromptForAndroidDeviceNotReady(device);
            if (!deviceAction.ShouldContinue || deviceAction.SkipGameResourcesThisRun)
            {
                return deviceAction;
            }

            RefreshAndroidToolStatus();
        }
    }

    private bool NeedsAndroidBridgeForUpdate()
    {
        if (SkipGameResources)
        {
            return false;
        }

        if (ForceGameResources || RequireGameResources)
        {
            return true;
        }

        return !HasLocalAssetBundles();
    }

    private UpdateToolPreparation PromptForMissingAndroidBridge()
    {
        if (RequireGameResources)
        {
            var requiredAnswer = AppDialogService.Show(
                "게임 리소스 동기화가 필수로 설정되어 있어 adb.exe 또는 MuMuManager.exe가 필요합니다.\n\n" +
                "확인: ADB/MuMuManager 경로 선택\n" +
                "취소: 업데이트 취소",
                "Android 도구 필요",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Information,
                MessageBoxResult.OK);
            if (requiredAnswer != MessageBoxResult.OK)
            {
                return new UpdateToolPreparation(false, false, "");
            }
        }
        else
        {
            var answer = AppDialogService.Show(
                "게임 리소스를 처음 가져오려면 adb.exe 또는 MuMuManager.exe가 필요합니다.\n\n" +
                "예: ADB/MuMuManager 경로 선택\n" +
                "아니오: 이번 업데이트에서 게임 리소스 동기화를 건너뛰고 기존 메타데이터만 갱신\n" +
                "취소: 업데이트 취소",
                "Android 도구 필요",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Information,
                MessageBoxResult.Yes);
            if (answer == MessageBoxResult.Cancel)
            {
                return new UpdateToolPreparation(false, false, "");
            }

            if (answer == MessageBoxResult.No)
            {
                return new UpdateToolPreparation(
                    true,
                    true,
                    "Android 도구가 없어 이번 업데이트에서는 게임 리소스 동기화를 건너뜁니다.");
            }
        }

        if (!BrowseAndroidBridgePath())
        {
            return new UpdateToolPreparation(false, false, "");
        }

        RefreshAndroidToolStatus();
        if (HasAndroidBridge())
        {
            return new UpdateToolPreparation(true, false, "");
        }

        AppDialogService.Show(
            "선택한 경로에서 adb.exe 또는 MuMuManager.exe를 사용할 수 없습니다.",
            "Android 도구 확인 실패",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
        return new UpdateToolPreparation(false, false, "");
    }

    private UpdateToolPreparation PromptForAndroidDeviceNotReady(AndroidDeviceCheckResult device)
    {
        if (RequireGameResources)
        {
            var requiredAnswer = AppDialogService.Show(
                $"{device.Message}\n\n" +
                "MuMu Player가 꺼져 있거나 ADB 시리얼이 offline 상태일 수 있습니다.\n" +
                "MuMu를 켠 뒤 확인을 누르면 다시 검사합니다. 취소하면 업데이트를 중단합니다.",
                "Android 장치 확인",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Warning,
                MessageBoxResult.OK);
            return requiredAnswer == MessageBoxResult.OK
                ? new UpdateToolPreparation(true, false, "")
                : new UpdateToolPreparation(false, false, "");
        }

        var answer = AppDialogService.Show(
            $"{device.Message}\n\n" +
            "MuMu Player가 꺼져 있거나 ADB 시리얼이 offline 상태일 수 있습니다.\n\n" +
            "예: MuMu를 켠 뒤 다시 확인\n" +
            "아니오: 이번 업데이트에서 게임 리소스 동기화 건너뛰기\n" +
            "취소: 업데이트 취소",
            "Android 장치 확인",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Warning,
            MessageBoxResult.Yes);
        return answer switch
        {
            MessageBoxResult.Yes => new UpdateToolPreparation(true, false, ""),
            MessageBoxResult.No => new UpdateToolPreparation(
                true,
                true,
                "Android 장치가 준비되지 않아 이번 업데이트에서는 게임 리소스 동기화를 건너뜁니다."),
            _ => new UpdateToolPreparation(false, false, "")
        };
    }
}
