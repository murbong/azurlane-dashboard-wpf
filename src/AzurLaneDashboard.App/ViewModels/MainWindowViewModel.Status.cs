namespace AzurLaneDashboard.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    public string AndroidToolStatus
    {
        get => androidToolStatus;
        private set => SetProperty(ref androidToolStatus, value);
    }

    public bool IsUpdating
    {
        get => isUpdating;
        private set
        {
            if (SetProperty(ref isUpdating, value))
            {
                RunUpdateCommand.RaiseCanExecuteChanged();
                CompactDatabaseCommand.RaiseCanExecuteChanged();
                DeepCompactDatabaseCommand.RaiseCanExecuteChanged();
                CleanupRetiredStorageCommand.RaiseCanExecuteChanged();
                OnPropertyChanged(nameof(UpdateButtonText));
            }
        }
    }

    public string UpdateButtonText => IsUpdating ? "업데이트 중..." : "업데이트 실행";

    public string UpdateStateLabel
    {
        get => updateStateLabel;
        private set => SetProperty(ref updateStateLabel, value);
    }

    public string UpdateProgressStage
    {
        get => updateProgressStage;
        private set => SetProperty(ref updateProgressStage, value);
    }

    public string UpdateProgressMessage
    {
        get => updateProgressMessage;
        private set => SetProperty(ref updateProgressMessage, value);
    }

    public double UpdateProgressValue
    {
        get => updateProgressValue;
        private set
        {
            if (SetProperty(ref updateProgressValue, value))
            {
                OnPropertyChanged(nameof(UpdateProgressText));
            }
        }
    }

    public bool UpdateProgressIsIndeterminate
    {
        get => updateProgressIsIndeterminate;
        private set
        {
            if (SetProperty(ref updateProgressIsIndeterminate, value))
            {
                OnPropertyChanged(nameof(UpdateProgressText));
            }
        }
    }

    public string UpdateProgressText => UpdateProgressIsIndeterminate
        ? "진행 중"
        : $"{UpdateProgressValue:0}%";

    public string UpdateLogCountText => $"{UpdateLogLines.Count:N0}줄";
}