using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using AzurLaneDashboard.Automation.Device;
using AzurLaneDashboard.Automation.Engine;
using AzurLaneDashboard.Core.Models;
using Microsoft.Win32;

namespace AzurLaneDashboard.App.ViewModels;

/// <summary>What the macro page needs from the rest of the dashboard.</summary>
public sealed record MacroHost(
    Func<bool> IsCaptureRunning,
    Func<string?> ResolveAdbPath,
    Func<string> Serial,
    Func<IReadOnlyList<LevelingShip>> LevelingShips,
    Action StartCapture,
    Action SaveSettings);

/// <summary>
/// 앱 관리 › 매크로: runs ALAuto's JSON macros in C# (screen through ADB and OpenCV, results through captured
/// packets). Readiness checks come first, then one start/stop place, live progress, the last screen and a log.
/// </summary>
public sealed partial class MacroViewModel : ObservableObject, IDisposable
{
    public const double PreviewWidth = 304;
    public const double PreviewHeight = 171;
    private const int MaxLogRows = 400;

    private readonly MacroHost host;
    private readonly Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
    private readonly LiveGameState gameState = new();
    private readonly DispatcherTimer clock;
    private LiveStateDocument? liveState;
    private string alautoRoot;
    private MacroItem? selectedMacro;
    private string maxCyclesText = "20";
    private string logFilter = "전체";
    private DeviceProbe? deviceProbe;
    private bool isCheckingDevice;
    private bool notifyOnEnd;

    public MacroViewModel(MacroHost host, string alautoRoot, bool notifyOnEnd = true)
    {
        this.host = host;
        this.notifyOnEnd = notifyOnEnd;
        this.alautoRoot = string.IsNullOrWhiteSpace(alautoRoot) ? DetectAlautoRoot() : alautoRoot;
        LogView = CollectionViewSource.GetDefaultView(LogRows);
        LogView.Filter = item => item is MacroLogRow row && logFilter switch
        {
            "패킷" => row.Source == MacroLogSource.Packet,
            "화면" => row.Source == MacroLogSource.Screen,
            _ => true
        };
        clock = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        clock.Tick += (_, _) => RefreshClock();
        BrowseAlautoCommand = new RelayCommand(_ => BrowseAlauto());
        RefreshCommand = new RelayCommand(_ => Refresh(checkDevice: true), _ => !IsRunning);
        StartCommand = new RelayCommand(_ => Start(), _ => CanStart);
        StopCommand = new RelayCommand(_ => Stop(), _ => IsRunning && !IsStopping);
        StartCaptureCommand = new RelayCommand(_ => host.StartCapture(), _ => !host.IsCaptureRunning());
        SetLogFilterCommand = new RelayCommand(parameter =>
        {
            logFilter = parameter as string ?? "전체";
            OnPropertyChanged(nameof(LogFilter));
            OnPropertyChanged(nameof(IsLogAll));
            OnPropertyChanged(nameof(IsLogPacket));
            OnPropertyChanged(nameof(IsLogScreen));
            LogView.Refresh();
        });
        Refresh(checkDevice: false);
    }

    /// <summary>Raised when a run starts or ends, so the window can hold F8 only while a macro runs.</summary>
    public event EventHandler? RunningChanged;

    /// <summary>Raised when a run stops by itself while <see cref="NotifyOnEnd"/> is on.</summary>
    public event EventHandler<MacroEndNotice>? EndNotice;

    public RelayCommand BrowseAlautoCommand { get; }
    public RelayCommand RefreshCommand { get; }
    public RelayCommand StartCommand { get; }
    public RelayCommand StopCommand { get; }
    public RelayCommand StartCaptureCommand { get; }
    public RelayCommand SetLogFilterCommand { get; }

    public string AlautoRoot => alautoRoot;
    public bool HasAlautoRoot => Directory.Exists(Path.Combine(alautoRoot, "macros"));
    public string AlautoRootText => HasAlautoRoot ? alautoRoot : "ALAuto 폴더를 지정하세요 (macros, assets가 있는 폴더)";

    public BulkObservableCollection<MacroItem> Macros { get; } = [];
    public bool HasMacros => Macros.Count > 0;

    public MacroItem? SelectedMacro
    {
        get => selectedMacro;
        set
        {
            if (IsRunning || !SetProperty(ref selectedMacro, value)) return;
            OnPropertyChanged(nameof(HasSelectedMacro));
            OnPropertyChanged(nameof(ShowMaxCycles));
            RebuildStops();
            RefreshStartState();
        }
    }

    public bool HasSelectedMacro => SelectedMacro is not null;

    public MacroReadiness DeviceCheck { get; } = new("기기 연결");
    public MacroReadiness CaptureCheck { get; } = new("패킷 캡처", "캡처 시작");
    public MacroReadiness AssetsCheck { get; } = new("화면 에셋");
    public MacroReadiness FleetCheck { get; } = new("쫄작 함대");

    public bool ShowMaxCycles => SelectedMacro?.Info.HasMaxCycles == true;

    /// <summary>Tell Windows when a run stops by itself; stopping it from the page or with F8 never notifies.</summary>
    public bool NotifyOnEnd
    {
        get => notifyOnEnd;
        set
        {
            if (SetProperty(ref notifyOnEnd, value)) host.SaveSettings();
        }
    }

    public string MaxCyclesText
    {
        get => maxCyclesText;
        set
        {
            if (SetProperty(ref maxCyclesText, value)) RefreshStartState();
        }
    }

    private int? MaxCycles =>
        int.TryParse(MaxCyclesText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var cycles) && cycles is > 0 and <= 9999
            ? cycles
            : null;

    public IReadOnlyList<string> StartBlockReasons { get; private set; } = [];
    public bool HasStartBlock => !IsRunning && StartBlockReasons.Count > 0;
    public bool CanStart => !IsRunning && SelectedMacro is not null && StartBlockReasons.Count == 0;

    public ObservableCollection<MacroLogRow> LogRows { get; } = [];
    public ICollectionView LogView { get; }
    public string LogFilter => logFilter;
    public bool IsLogAll => logFilter == "전체";
    public bool IsLogPacket => logFilter == "패킷";
    public bool IsLogScreen => logFilter == "화면";

    public BulkObservableCollection<MacroFleetRow> FleetRows { get; } = [];
    public BulkObservableCollection<MacroStopRow> StopRows { get; } = [];
    public string FleetTitle => liveState?.Leveling.FarmFleetId is > 0 and var fleet ? $"쫄작 함대 · 게임 {fleet}함대" : "쫄작 함대";
    public bool HasFleet => FleetRows.Count > 0;
    public bool HasStops => StopRows.Count > 0;

    public ImageSource? ScreenImage { get; private set; }
    public bool HasScreen => ScreenImage is not null;
    public double MatchLeft { get; private set; }
    public double MatchTop { get; private set; }
    public double MatchWidth { get; private set; }
    public double MatchHeight { get; private set; }
    public bool HasMatch { get; private set; }
    public string MatchLabel { get; private set; } = "";
    public string ScreenAgeText => lastScreenAt is { } at ? $"{Ago(at)} · 찾은 이미지와 누를 위치" : "실행하면 매크로가 본 화면이 여기에 보입니다.";

    /// <summary>The dashboard's live state after each load; also feeds "game" conditions of a running macro.</summary>
    public void UpdateGameState(LiveStateDocument document)
    {
        liveState = document;
        gameState.Update(document);
        CountBattle(document);
        RefreshFleet();
        RefreshStops();
        RefreshReadinessFromState();
    }

    /// <summary>Called when the page is opened: re-reads the macros folder and checks the device.</summary>
    public void OnPageShown()
    {
        if (!IsRunning) Refresh(checkDevice: true);
    }

    /// <summary>Whether the window got F8 for this run; another program may already hold it.</summary>
    public void ReportStopKey(bool registered)
    {
        AddLog(new MacroLogRow(DateTime.Now, MacroLogSource.Macro, registered
            ? "F8을 누르면 어느 창에서든 멈춥니다."
            : "다른 프로그램이 F8을 쓰고 있어 정지 단축키를 쓸 수 없습니다. 정지 버튼을 쓰세요."));
    }

    public void OnCaptureChanged()
    {
        RefreshReadinessFromState();
        StartCaptureCommand.RaiseCanExecuteChanged();
    }

    /// <summary>The emulator to drive changed (another account is on screen): check the new one.</summary>
    public void OnDeviceChanged()
    {
        if (IsRunning) return;
        deviceProbe = null;
        RefreshReadinessFromState();
        _ = CheckDeviceAsync();
    }

    private void Refresh(bool checkDevice)
    {
        var selected = SelectedMacro?.FileName;
        var items = HasAlautoRoot ? MacroCatalog.Scan(alautoRoot).Select(info => new MacroItem(info)).ToList() : [];
        foreach (var item in items)
            item.LastRunText = lastRuns.GetValueOrDefault(item.FileName, "");
        Macros.ReplaceRange(items);
        OnPropertyChanged(nameof(HasMacros));
        OnPropertyChanged(nameof(AlautoRootText));
        OnPropertyChanged(nameof(HasAlautoRoot));
        selectedMacro = items.FirstOrDefault(item => item.FileName == selected) ?? items.FirstOrDefault(item => item.IsSupported);
        OnPropertyChanged(nameof(SelectedMacro));
        OnPropertyChanged(nameof(HasSelectedMacro));
        OnPropertyChanged(nameof(ShowMaxCycles));
        RebuildStops();
        CheckAssets();
        RefreshReadinessFromState();
        if (checkDevice) _ = CheckDeviceAsync();
        RefreshStartState();
    }

    private void CheckAssets()
    {
        var root = AssetsRoot();
        var count = Directory.Exists(root) ? Directory.EnumerateFiles(root, "*.png", SearchOption.AllDirectories).Count() : 0;
        AssetsCheck.Set(count > 0, count > 0
            ? $"{Path.GetFileName(root)} {count:N0}개 · {root}"
            : HasAlautoRoot ? $"에셋이 없습니다 · {root}" : "ALAuto 폴더를 지정하세요");
    }

    private string AssetsRoot()
    {
        var server = "KR";
        try
        {
            if (System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(Path.Combine(alautoRoot, "config.json")))?["assets"]?["server"]
                is { } value)
            {
                server = MacroSchema.Text(value).Trim().ToUpperInvariant();
            }
        }
        catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException or UnauthorizedAccessException)
        {
        }

        return Path.Combine(alautoRoot, "assets", server.Length > 0 ? server : "KR");
    }

    private async Task CheckDeviceAsync()
    {
        if (isCheckingDevice) return;
        isCheckingDevice = true;
        var adb = host.ResolveAdbPath();
        var serial = host.Serial();
        if (string.IsNullOrWhiteSpace(adb))
        {
            deviceProbe = null;
            DeviceCheck.Set(false, "ADB를 찾지 못했습니다 · 설정 › 기기 연결에서 adb.exe를 지정하세요");
        }
        else
        {
            DeviceCheck.Set(false, $"{serial} 확인 중…");
            deviceProbe = await Task.Run(() => new AdbDevice(adb, serial).Probe());
            DeviceCheck.Set(deviceProbe is { Connected: true, IsExpectedSize: true, GameFound: true }, !deviceProbe.Connected
                ? $"{serial} 연결 안 됨 · {deviceProbe.Error}"
                : !deviceProbe.GameFound
                    ? $"{serial} · 벽람항로 화면을 찾지 못했습니다"
                    : deviceProbe.IsExpectedSize
                        ? $"{serial} · {deviceProbe.SizeText} · 게임: {deviceProbe.Display!.Text}"
                        : $"{serial} · 해상도 {deviceProbe.SizeText} (에셋은 1920×1080 기준)");
        }

        isCheckingDevice = false;
        RefreshStartState();
    }

    private void RefreshReadinessFromState()
    {
        CaptureCheck.Set(host.IsCaptureRunning(), host.IsCaptureRunning()
            ? "캡처 중 · 전투 결과와 쫄작 함대를 패킷으로 확인"
            : "꺼짐 · 패킷 조건을 쓰는 매크로는 캡처가 필요합니다");
        var leveling = liveState?.Leveling;
        FleetCheck.Set(leveling is { FarmFleetId: > 0 }, leveling is { FarmFleetId: > 0 }
            ? $"게임 {leveling.FarmFleetId}함대 · 성장 중 {leveling.Growing}척 · 각성 대기 {leveling.Ships.Count(ship => ship.Waiting)}척"
            : "최근 전투에서 쫄작 함대를 찾지 못했습니다");
        OnPropertyChanged(nameof(FleetTitle));
        RefreshStartState();
    }

    private void RefreshStartState()
    {
        var reasons = new List<string>();
        if (!HasAlautoRoot) reasons.Add("ALAuto 폴더를 지정하세요");
        if (SelectedMacro is { IsSupported: false } unsupported) reasons.Add(unsupported.UnsupportedReason);
        if (!AssetsCheck.IsOk && HasAlautoRoot) reasons.Add("화면 에셋을 찾지 못했습니다");
        if (deviceProbe is null || !deviceProbe.Connected) reasons.Add("기기가 연결되어 있지 않습니다 · 에뮬레이터를 켜고 다시 확인하세요");
        else if (!deviceProbe.GameFound)
            reasons.Add("벽람항로 화면을 찾지 못했습니다 · 게임을 켠 뒤 다시 확인하세요");
        else if (!deviceProbe.IsExpectedSize)
            reasons.Add($"기기 해상도가 {deviceProbe.SizeText}입니다 · 에셋은 1920×1080 기준입니다");
        if (SelectedMacro?.UsesPackets == true && !host.IsCaptureRunning())
            reasons.Add("패킷 캡처가 꺼져 있습니다 · 이 매크로는 전투 확인에 패킷을 씁니다");
        if (ShowMaxCycles && MaxCycles is null) reasons.Add("최대 반복은 1~9999 사이 숫자로 적어 주세요");
        StartBlockReasons = reasons;
        OnPropertyChanged(nameof(StartBlockReasons));
        OnPropertyChanged(nameof(HasStartBlock));
        OnPropertyChanged(nameof(CanStart));
        StartCommand.RaiseCanExecuteChanged();
        RefreshCommand.RaiseCanExecuteChanged();
    }

    private void BrowseAlauto()
    {
        var dialog = new OpenFolderDialog
        {
            Title = "ALAuto 폴더 선택 (macros, assets가 있는 폴더)",
            InitialDirectory = Directory.Exists(alautoRoot) ? alautoRoot : ""
        };
        if (dialog.ShowDialog() != true) return;
        alautoRoot = dialog.FolderName;
        OnPropertyChanged(nameof(AlautoRoot));
        host.SaveSettings();
        Refresh(checkDevice: false);
    }

    // The clone next to this repository or in the user folder, so the page works before anything is set.
    private static string DetectAlautoRoot()
    {
        var candidates = new List<string>();
        var drive = Path.GetPathRoot(AppContext.BaseDirectory);
        if (!string.IsNullOrEmpty(drive)) candidates.Add(Path.Combine(drive, "ALAuto-Murbong"));
        candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "ALAuto-Murbong"));
        return candidates.FirstOrDefault(path => Directory.Exists(Path.Combine(path, "macros"))) ?? "";
    }

    private static string Ago(DateTime at)
    {
        var seconds = Math.Max(0, (int)(DateTime.Now - at).TotalSeconds);
        return seconds < 60 ? $"{seconds}초 전" : seconds < 3600 ? $"{seconds / 60}분 전" : $"{seconds / 3600}시간 전";
    }

    public void Dispose()
    {
        cancellation?.Cancel();
        clock.Stop();
    }
}
