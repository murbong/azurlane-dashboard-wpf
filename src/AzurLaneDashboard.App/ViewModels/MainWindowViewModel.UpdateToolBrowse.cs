using AzurLaneDashboard.Core.Services;
using AzurLaneDashboard.Data.Services;
using Microsoft.Win32;

namespace AzurLaneDashboard.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private void BrowseAdbPath()
    {
        if (TryBrowseExecutable("adb.exe 선택", "ADB executable (adb.exe)|adb.exe|Executable (*.exe)|*.exe",
                out var path))
        {
            AdbPath = path;
        }
    }

    private void BrowseMuMuManagerPath()
    {
        if (TryBrowseExecutable(
                "MuMuManager.exe 선택",
                "MuMuManager executable (MuMuManager.exe)|MuMuManager.exe|Executable (*.exe)|*.exe",
                out var path))
        {
            MuMuManagerPath = path;
        }
    }

    private void AutoDetectAndroidTools()
    {
        var root = ProjectLocator.FindProjectRoot();
        var adb = ExternalToolLocator.ResolveAdbPath(AdbPath, root);
        var mumu = ExternalToolLocator.ResolveMuMuManagerPath(MuMuManagerPath, root);
        if (!string.IsNullOrWhiteSpace(adb))
        {
            AdbPath = adb;
        }

        if (!string.IsNullOrWhiteSpace(mumu))
        {
            MuMuManagerPath = mumu;
        }

        RefreshAndroidToolStatus();
        Status = AndroidToolStatus;
    }

    private bool BrowseAndroidBridgePath()
    {
        if (!TryBrowseExecutable(
                "adb.exe 또는 MuMuManager.exe 선택",
                "Android tools (adb.exe;MuMuManager.exe)|adb.exe;MuMuManager.exe|Executable (*.exe)|*.exe",
                out var path))
        {
            return false;
        }

        if (ExternalToolLocator.IsMuMuManagerPath(path))
        {
            MuMuManagerPath = path;
        }
        else
        {
            AdbPath = path;
        }

        return true;
    }

    private static bool TryBrowseExecutable(string title, string filter, out string path)
    {
        var dialog = new OpenFileDialog
        {
            Title = title,
            Filter = filter,
            CheckFileExists = true
        };
        if (dialog.ShowDialog() == true)
        {
            path = dialog.FileName;
            return true;
        }

        path = "";
        return false;
    }
}
