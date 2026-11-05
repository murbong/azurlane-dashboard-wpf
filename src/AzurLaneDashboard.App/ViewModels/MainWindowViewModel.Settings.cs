using AzurLaneDashboard.Core.Configuration;

namespace AzurLaneDashboard.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    public string DatabasePath
    {
        get => databasePath;
        set
        {
            // A file opened from 설정 (DB 선택) moves the sidebar picker too; null while the constructor runs.
            if (SetProperty(ref databasePath, value) && accountRegistry is not null) SyncSelectedAccount();
        }
    }

    public string Region
    {
        get => region;
        set
        {
            if (SetProperty(ref region, value))
            {
                SaveSettings();
            }
        }
    }

    public string Serial
    {
        get => serial;
        set
        {
            var previous = serial;
            if (SetProperty(ref serial, value))
            {
                FollowSerialChange(previous, value);
                SaveSettings();
            }
        }
    }

    public string AdbPath
    {
        get => adbPath;
        set
        {
            if (SetProperty(ref adbPath, value))
            {
                SaveSettings();
                RefreshAndroidToolStatus();
            }
        }
    }

    public string MuMuManagerPath
    {
        get => mumuManagerPath;
        set
        {
            if (SetProperty(ref mumuManagerPath, value))
            {
                SaveSettings();
                RefreshAndroidToolStatus();
            }
        }
    }

    public string DataDirectory
    {
        get => dataDirectory;
        set
        {
            if (SetProperty(ref dataDirectory, value))
            {
                SaveSettings();
            }
        }
    }

    public int CaptureStatusInterval
    {
        get => captureStatusInterval;
        set
        {
            var next = value <= 0 ? AppDefaults.DefaultCaptureStatusInterval : value;
            if (SetProperty(ref captureStatusInterval, next))
            {
                SaveSettings();
            }
        }
    }

    public bool CaptureVerbose
    {
        get => captureVerbose;
        set
        {
            if (SetProperty(ref captureVerbose, value))
            {
                SaveSettings();
            }
        }
    }

    public int GuildDockCapacityBonus
    {
        get => guildDockCapacityBonus;
        set
        {
            if (SetProperty(ref guildDockCapacityBonus, Math.Max(0, value)))
            {
                SaveSettings();
            }
        }
    }
}
