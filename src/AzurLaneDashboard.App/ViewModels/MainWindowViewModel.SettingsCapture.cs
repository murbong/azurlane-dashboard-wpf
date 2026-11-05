using AzurLaneDashboard.App.Services;
using AzurLaneDashboard.Core.Configuration;

namespace AzurLaneDashboard.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    public string CaptureBackend
    {
        get => captureBackend;
        set
        {
            if (SetProperty(ref captureBackend, value))
            {
                CaptureInterface = AppDefaults.DefaultCaptureInterface;
                RebuildCaptureTargets();
                SaveSettings();
                _ = RefreshCaptureInterfacesAsync();
            }
        }
    }

    public string CaptureInterface
    {
        get => captureInterface;
        set
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            var next = value.Trim();
            if (SetProperty(ref captureInterface, next))
            {
                EnsureCaptureInterfaceOption();
                SaveSettings();
            }
        }
    }

    public string CaptureDumpcapPath
    {
        get => captureDumpcapPath;
        set
        {
            if (SetProperty(ref captureDumpcapPath, value))
            {
                SaveSettings();
            }
        }
    }

    public int CaptureServerPort
    {
        get => captureServerPort;
        set
        {
            var next = value <= 0 ? AppDefaults.DefaultServerPort : value;
            if (SetProperty(ref captureServerPort, next))
            {
                SaveSettings();
            }
        }
    }

    public string CaptureServerHost
    {
        get => captureServerHost;
        set
        {
            if (SetProperty(ref captureServerHost, value))
            {
                SaveSettings();
            }
        }
    }

    public bool IsDarkMode
    {
        get => isDarkMode;
        set
        {
            if (SetProperty(ref isDarkMode, value))
            {
                ThemeService.Apply(isDarkMode);
                RefreshAllViews();
                OnPropertyChanged(nameof(ThemeModeLabel));
                SaveSettings();
            }
        }
    }

    public string ThemeModeLabel => IsDarkMode ? "다크" : "라이트";

    public bool IsAutoRefreshEnabled
    {
        get => isAutoRefreshEnabled;
        set
        {
            if (SetProperty(ref isAutoRefreshEnabled, value))
            {
                OnPropertyChanged(nameof(AutoRefreshLabel));
                SaveSettings();
            }
        }
    }

    public string AutoRefreshLabel => IsAutoRefreshEnabled ? "자동 갱신 켜짐" : "자동 갱신 꺼짐";
}
