using System.Collections.ObjectModel;
using System.Windows;
using AzurLaneDashboard.Capture;
using AzurLaneDashboard.Core.Configuration;
using AzurLaneDashboard.Core.Services;
using AzurLaneDashboard.Data.Services;

namespace AzurLaneDashboard.App.ViewModels;

/// <summary>
/// Live capture of several emulators at once. With the adb backend each serial in <see cref="captureSerials"/> is
/// captured by its own service, which writes to the database of whichever account logs in on it; the PC-wide
/// backends (dumpcap, npcap) run one capture. The sidebar button starts or stops all of them.
/// </summary>
public sealed partial class MainWindowViewModel
{
    private readonly List<string> captureSerials = [];
    private string newCaptureSerial = "";
    private string captureTargetsStatus = "";
    private bool isFindingAdbDevices;
    private bool captureTargetsStale;
    private bool lastMacroCaptureRunning;

    public BulkObservableCollection<CaptureTargetViewModel> CaptureTargets { get; } = new();

    /// <summary>Serials adb listed on the last search that are not captured yet.</summary>
    public ObservableCollection<string> AvailableAdbSerials { get; } = new();

    public RelayCommand AddCaptureSerialCommand { get; private set; } = null!;
    public RelayCommand FindAdbDevicesCommand { get; private set; } = null!;

    public bool IsAdbCaptureBackend => NormalizeCaptureBackend(CaptureBackend) == AppDefaults.CaptureBackendAdb;

    public string NewCaptureSerial
    {
        get => newCaptureSerial;
        set
        {
            if (SetProperty(ref newCaptureSerial, value ?? "")) AddCaptureSerialCommand?.RaiseCanExecuteChanged();
        }
    }

    public string CaptureTargetsStatus
    {
        get => captureTargetsStatus;
        private set => SetProperty(ref captureTargetsStatus, value);
    }

    public string FindAdbDevicesButtonText => isFindingAdbDevices ? "찾는 중..." : "기기 찾기";

    public int RunningCaptureCount => CaptureTargets.Count(target => target.IsRunning);

    private void InitializeCaptureTargets(IReadOnlyList<string> savedSerials)
    {
        captureSerials.Clear();
        foreach (var value in savedSerials)
        {
            var trimmed = value?.Trim() ?? "";
            if (trimmed.Length > 0 && !captureSerials.Contains(trimmed, StringComparer.OrdinalIgnoreCase))
            {
                captureSerials.Add(trimmed);
            }
        }

        if (captureSerials.Count == 0) captureSerials.Add(Serial);
        // The parameter is a serial adb found; without one, the serial typed in the box.
        AddCaptureSerialCommand = new RelayCommand(parameter => AddCaptureSerial(parameter as string ?? NewCaptureSerial),
            parameter => CanAddCaptureSerial(parameter as string ?? NewCaptureSerial));
        FindAdbDevicesCommand = new RelayCommand(_unused => _ = FindAdbDevicesAsync(), _ => !isFindingAdbDevices);
        RebuildCaptureTargets();
    }

    // The targets follow the backend: one per serial for adb, one PC capture otherwise. Never rebuilt while one runs.
    private void RebuildCaptureTargets()
    {
        if (CaptureTargets.Any(target => target.IsRunning || target.RunTask is { IsCompleted: false }))
        {
            captureTargetsStale = true;
            return;
        }

        captureTargetsStale = false;
        foreach (var target in CaptureTargets) DetachCaptureTarget(target);
        var targets = IsAdbCaptureBackend
            ? captureSerials.Select(serial => CreateCaptureTarget(serial, serial, true)).ToList()
            : [CreateCaptureTarget(CaptureTargetViewModel.HostKey, $"PC 네트워크 ({NormalizeCaptureBackend(CaptureBackend)})", false)];
        Replace(CaptureTargets, targets);
        foreach (var target in targets) RefreshCaptureTarget(target);
        OnPropertyChanged(nameof(IsAdbCaptureBackend));
        RefreshCaptureTargetCommands();
    }

    private CaptureTargetViewModel CreateCaptureTarget(string key, string label, bool isAdb)
    {
        var target = new CaptureTargetViewModel(key, label, isAdb);
        target.ToggleCommand = new RelayCommand(_ => ToggleCaptureTarget(target));
        target.RemoveCommand = new RelayCommand(_ => RemoveCaptureTarget(target),
            _ => target.IsAdb && !target.IsRunning && CaptureTargets.Count > 1);
        target.Service.StateApplied += OnCaptureStateApplied;
        target.Service.PacketDecoded += OnPacketDecoded;
        target.Service.AccountChanged += OnCaptureAccountChanged;
        return target;
    }

    private void DetachCaptureTarget(CaptureTargetViewModel target)
    {
        target.Service.StateApplied -= OnCaptureStateApplied;
        target.Service.PacketDecoded -= OnPacketDecoded;
        target.Service.AccountChanged -= OnCaptureAccountChanged;
    }

    private void RefreshCaptureTargetCommands()
    {
        foreach (var target in CaptureTargets) target.RemoveCommand.RaiseCanExecuteChanged();
        AddCaptureSerialCommand.RaiseCanExecuteChanged();
        // Null while the constructor builds the first list.
        StartCaptureCommand?.RaiseCanExecuteChanged();
        OnPropertyChanged(nameof(CaptureStartButtonText));
        OnPropertyChanged(nameof(CaptureStopButtonText));
        OnPropertyChanged(nameof(CaptureToggleToolTip));
    }

    private void StartCapture()
    {
        foreach (var target in CaptureTargets.ToList())
        {
            StartCaptureTarget(target);
        }
    }

    private void StartCaptureTarget(CaptureTargetViewModel target)
    {
        if (target.IsRunning || target.RunTask is { IsCompleted: false })
        {
            return;
        }

        SaveSettings();
        EnsureAccountRegistry();
        captureLogKey = "";
        target.LastError = "";
        var options = BuildCaptureOptions(target);
        Status = CaptureTargets.Count > 1 ? $"실시간 캡처 시작 · {target.Label}" : "실시간 캡처 시작";
        target.RunTask = Task.Run(async () =>
        {
            try
            {
                await target.Service.StartAsync(options).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    target.LastError = ex.Message;
                    Status = CaptureTargets.Count > 1
                        ? $"캡처 실패 ({target.Label}): {ex.Message}"
                        : $"캡처 실패: {ex.Message}";
                });
            }
            finally
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    if (isDisposed) return;
                    SyncCaptureLogs();
                    RefreshCaptureState();
                });
            }
        });
        RefreshCaptureState();
    }

    private CaptureOptions BuildCaptureOptions(CaptureTargetViewModel target)
    {
        return new CaptureOptions
        {
            DataDirectory = accountRegistry.DataDirectory,
            CaptureTarget = target.Key,
            Backend = NormalizeCaptureBackend(CaptureBackend),
            AdbPath = AdbPath,
            Serial = target.IsAdb ? target.Key : Serial,
            // With two emulators a missing serial must not fall back to the other one, which is already captured.
            RequireExactSerial = target.IsAdb && CaptureTargets.Count(item => item.IsAdb) > 1,
            DumpcapPath = CaptureDumpcapPath,
            CaptureInterface = CaptureInterface,
            ServerPort = CaptureServerPort,
            ServerHost = CaptureServerHost,
            StatusIntervalSeconds = CaptureStatusInterval,
            Verbose = CaptureVerbose
        };
    }

    private void ToggleCapture()
    {
        if (IsCaptureRunning)
        {
            StopCapture();
            return;
        }

        StartCapture();
    }

    private void ToggleCaptureTarget(CaptureTargetViewModel target)
    {
        if (target.IsRunning)
        {
            StopCaptureTarget(target);
            return;
        }

        StartCaptureTarget(target);
    }

    private void StopCapture()
    {
        foreach (var target in CaptureTargets.Where(target => target.IsRunning).ToList())
        {
            StopCaptureTarget(target);
        }

        Status = "실시간 캡처 중지 요청";
    }

    private void StopCaptureTarget(CaptureTargetViewModel target)
    {
        target.Service.Stop();
        target.MarkStopping();
        RefreshCaptureState();
    }

    private void StopAllCaptures()
    {
        foreach (var target in CaptureTargets)
        {
            target.Service.Stop();
            DetachCaptureTarget(target);
        }
    }

    /// <summary>Running state, the account each capture writes to, and everything shown from them.</summary>
    private void RefreshCaptureState()
    {
        if (isDisposed) return;
        foreach (var target in CaptureTargets) RefreshCaptureTarget(target);
        MarkDuplicateCaptureAccounts();
        IsCaptureRunning = CaptureTargets.Any(target => target.IsRunning);
        if (captureTargetsStale && !IsCaptureRunning) RebuildCaptureTargets();
        RefreshCaptureTargetCommands();
        RefreshCaptureStatusRows();
        RefreshAccountLiveState();
        OnPropertyChanged(nameof(RunningCaptureCount));
        OnPropertyChanged(nameof(ConnectionSummary));
        var macroCaptureRunning = IsMacroCaptureRunning();
        if (macroCaptureRunning != lastMacroCaptureRunning)
        {
            lastMacroCaptureRunning = macroCaptureRunning;
            Macros?.OnCaptureChanged();
        }
    }

    // A stopped row still names the account its emulator saw last: the one a capture started now would assume.
    private void RefreshCaptureTarget(CaptureTargetViewModel target) =>
        target.Refresh(target.RunTask is { IsCompleted: false }, accountRegistry?.LastOn(target.Key));

    // The game lets one account play in one place at a time, so two captures on one account are one emulator
    // reached under two serials (MuMu lists emulator-5554 and 127.0.0.1:16384 for the same instance).
    private void MarkDuplicateCaptureAccounts()
    {
        var duplicates = CaptureTargets.Where(target => target.IsRunning && target.AccountUid > 0)
            .GroupBy(target => target.AccountUid)
            .Where(group => group.Count() > 1)
            .SelectMany(group => group)
            .ToHashSet();
        foreach (var target in CaptureTargets)
        {
            var duplicate = duplicates.Contains(target);
            if (target.IsDuplicate == duplicate) continue;
            target.IsDuplicate = duplicate;
            RefreshCaptureTarget(target);
            if (duplicate)
            {
                Status = $"{target.Label}: 다른 캡처와 같은 계정입니다. 같은 에뮬레이터를 두 시리얼로 잡았다면 하나를 지우세요.";
            }
        }
    }

    // Capture thread: a login named the account a capture writes to. Logging in to another account on the
    // emulator whose account is on screen takes the screen along; a login on any other emulator leaves it alone.
    private void OnCaptureAccountChanged(GameAccount? previous, GameAccount account)
    {
        if (isDisposed) return;
        uiDispatcher.BeginInvoke(() =>
        {
            if (isDisposed) return;
            if (previous is not null && IsDisplayedAccount(previous) && CanSwitchAccount)
            {
                ShowAccount(account.DatabasePath, account.DisplayName);
            }

            RefreshCaptureState();
            SyncCaptureLogs();
        });
    }

    private bool CanAddCaptureSerial(string? value)
    {
        var serial = value?.Trim() ?? "";
        return IsAdbCaptureBackend && serial.Length > 0 &&
               !captureSerials.Contains(serial, StringComparer.OrdinalIgnoreCase);
    }

    private void AddCaptureSerial(string? value)
    {
        if (!CanAddCaptureSerial(value)) return;
        var serial = value!.Trim();
        captureSerials.Add(serial);
        CaptureTargets.Add(CreateCaptureTarget(serial, serial, true));
        AvailableAdbSerials.Remove(serial);
        NewCaptureSerial = "";
        CaptureTargetsStatus = $"{serial}을 추가했습니다. 같은 에뮬레이터를 다른 이름으로 두 번 넣지 않도록 주의하세요.";
        SaveSettings();
        RefreshCaptureState();
    }

    private void RemoveCaptureTarget(CaptureTargetViewModel target)
    {
        if (!target.IsAdb || target.IsRunning || CaptureTargets.Count <= 1) return;
        captureSerials.RemoveAll(serial => string.Equals(serial, target.Key, StringComparison.OrdinalIgnoreCase));
        DetachCaptureTarget(target);
        CaptureTargets.Remove(target);
        CaptureTargetsStatus = $"{target.Key}을 목록에서 뺐습니다.";
        SaveSettings();
        RefreshCaptureState();
    }

    private async Task FindAdbDevicesAsync()
    {
        if (isFindingAdbDevices) return;
        isFindingAdbDevices = true;
        OnPropertyChanged(nameof(FindAdbDevicesButtonText));
        FindAdbDevicesCommand.RaiseCanExecuteChanged();
        try
        {
            var adb = ExternalToolLocator.ResolveAdbPath(AdbPath, ProjectLocator.FindProjectRoot()) ?? AdbPath;
            var devices = await DumpcapCaptureService.ListAdbDevicesAsync(adb);
            var fresh = devices.Where(serial => !captureSerials.Contains(serial, StringComparer.OrdinalIgnoreCase))
                .ToList();
            Replace(AvailableAdbSerials, fresh);
            if (fresh.Count > 0 && string.IsNullOrWhiteSpace(NewCaptureSerial)) NewCaptureSerial = fresh[0];
            CaptureTargetsStatus = devices.Count == 0
                ? "adb에 연결된 기기가 없습니다. 에뮬레이터를 켠 뒤 다시 찾아 주세요."
                : fresh.Count == 0
                    ? $"연결된 기기가 모두 목록에 있습니다: {string.Join(", ", devices)}"
                    : $"새 기기 {fresh.Count}대: {string.Join(", ", fresh)} · MuMu는 한 에뮬레이터를 두 이름으로 보여 줄 수 있으니 하나만 추가하세요.";
        }
        catch (Exception ex)
        {
            CaptureTargetsStatus = $"기기를 찾지 못했습니다: {ex.Message}";
        }
        finally
        {
            isFindingAdbDevices = false;
            OnPropertyChanged(nameof(FindAdbDevicesButtonText));
            FindAdbDevicesCommand.RaiseCanExecuteChanged();
        }
    }

    // A single-emulator setup keeps following the serial in 기기 연결, as it did before the list existed.
    private void FollowSerialChange(string previous, string next)
    {
        if (captureSerials.Count != 1 ||
            !string.Equals(captureSerials[0], previous, StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(next))
        {
            return;
        }

        captureSerials[0] = next.Trim();
        RebuildCaptureTargets();
    }

    private void OperationStatusTimerTick(object? sender, EventArgs e)
    {
        if (isDisposed)
        {
            return;
        }

        RefreshCaptureState();
        SyncCaptureLogs();
        CheckSortieRun();
    }
}
