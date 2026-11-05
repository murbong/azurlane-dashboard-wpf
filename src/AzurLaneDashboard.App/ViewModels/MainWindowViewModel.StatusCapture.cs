namespace AzurLaneDashboard.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    public string CaptureStateLabel
    {
        get => captureStateLabel;
        private set => SetProperty(ref captureStateLabel, value);
    }

    public string CaptureStartButtonText => CaptureTargets.Count > 1
        ? "모두 시작"
        : IsCaptureRunning ? "캡처 중" : "캡처 시작";

    public string CaptureStopButtonText => CaptureTargets.Count > 1 ? "모두 중지" : "캡처 중지";

    public string CaptureToggleButtonText => IsCaptureRunning ? "캡처 중지" : "캡처 시작";

    public string CaptureToggleToolTip => CaptureTargets.Count > 1
        ? IsCaptureRunning ? "모든 에뮬레이터 캡처 중지" : "모든 에뮬레이터 캡처 시작"
        : IsCaptureRunning ? "실시간 캡처 중지" : "실시간 캡처 시작";

    public string CaptureLogCountText => $"{CaptureLogLines.Count:N0}줄";

    public string CaptureStatusLabel
    {
        get => captureStatusLabel;
        private set => SetProperty(ref captureStatusLabel, value);
    }

    public string CaptureStatusDetail
    {
        get => captureStatusDetail;
        private set => SetProperty(ref captureStatusDetail, value);
    }

    public bool IsRefreshingCaptureInterfaces
    {
        get => isRefreshingCaptureInterfaces;
        private set
        {
            if (SetProperty(ref isRefreshingCaptureInterfaces, value))
            {
                RefreshCaptureInterfacesCommand.RaiseCanExecuteChanged();
                OnPropertyChanged(nameof(RefreshCaptureInterfacesButtonText));
            }
        }
    }

    public string CaptureInterfaceStatus
    {
        get => captureInterfaceStatus;
        private set => SetProperty(ref captureInterfaceStatus, value);
    }

    public string RefreshCaptureInterfacesButtonText => IsRefreshingCaptureInterfaces ? "검색 중..." : "새로고침";

    public bool IsCaptureRunning
    {
        get => isCaptureRunning;
        private set
        {
            if (SetProperty(ref isCaptureRunning, value))
            {
                OnPropertyChanged(nameof(CaptureStateText));
                OnPropertyChanged(nameof(CaptureStartButtonText));
                OnPropertyChanged(nameof(CaptureStopButtonText));
                OnPropertyChanged(nameof(CaptureToggleButtonText));
                OnPropertyChanged(nameof(CaptureToggleToolTip));
                StartCaptureCommand.RaiseCanExecuteChanged();
                StopCaptureCommand.RaiseCanExecuteChanged();
                DeepCompactDatabaseCommand.RaiseCanExecuteChanged();
                CleanupRetiredStorageCommand.RaiseCanExecuteChanged();
                OnPropertyChanged(nameof(RetiredStorageBlockedText));
                WriteLiveState();
                Macros?.OnCaptureChanged();
            }
        }
    }

    public string CaptureStateText => IsCaptureRunning ? "실시간 캡처 ON" : "실시간 캡처 OFF";
}