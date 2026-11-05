using System.Globalization;
using System.IO;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Windows.Media.Imaging;
using AzurLaneDashboard.Automation.Device;
using AzurLaneDashboard.Automation.Engine;
using AzurLaneDashboard.Core.Models;

namespace AzurLaneDashboard.App.ViewModels;

public sealed partial class MacroViewModel
{
    // Steps that only shuffle values or branch; "지금 하는 일" keeps showing the last step that takes time.
    private static readonly HashSet<string> QuietActions = new(StringComparer.Ordinal)
    {
        "set", "increment", "if", "log", "break", "continue", "return", "while", "repeat", "stats_increment",
        "print_stats", "update_screen", "call"
    };

    private readonly Dictionary<string, string> lastRuns = new(StringComparer.OrdinalIgnoreCase);
    private CancellationTokenSource? cancellation;
    private bool isRunning;
    private bool isStopping;
    private DateTime runStartedAt;
    private IReadOnlyDictionary<string, object?> variables = new Dictionary<string, object?>();
    private long lastBattleId;
    private int confirmedBattles;
    private int newShips;
    private bool allSRank = true;
    private DateTime? lastBattleAt;
    private DateTime? lastScreenAt;
    private DateTime lastScreenDecodedAt;
    private string lastMacroMessage = "";
    private string nowText = "";
    private string nowTrail = "";
    private bool isEnded;
    private bool endedIsError;
    private string endedTitle = "";
    private string endedDetail = "";

    public bool IsRunning
    {
        get => isRunning;
        private set
        {
            if (!SetProperty(ref isRunning, value)) return;
            OnPropertyChanged(nameof(IsIdle));
            OnPropertyChanged(nameof(StateText));
            OnPropertyChanged(nameof(HasStartBlock));
            OnPropertyChanged(nameof(CanStart));
            StartCommand.RaiseCanExecuteChanged();
            StopCommand.RaiseCanExecuteChanged();
            RefreshCommand.RaiseCanExecuteChanged();
            RunningChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public bool IsIdle => !IsRunning;

    public bool IsStopping
    {
        get => isStopping;
        private set
        {
            if (!SetProperty(ref isStopping, value)) return;
            OnPropertyChanged(nameof(StateText));
            StopCommand.RaiseCanExecuteChanged();
        }
    }

    public string StateText => IsStopping ? "멈추는 중" : IsRunning ? "실행 중" : "대기";
    public string RunStartedText => IsRunning || IsEnded
        ? $"{runStartedAt:HH:mm} 시작 · {Elapsed(DateTime.Now - runStartedAt)}{(IsRunning ? " 경과" : "")}"
        : "";

    public bool HasCycles => ShowMaxCycles && variables.ContainsKey("cycles");
    public string CyclesText => MacroValues.Format(variables.GetValueOrDefault("cycles") ?? 0L);
    public string CyclesTotalText => $"/ {MacroValues.Format(variables.GetValueOrDefault("max_cycles") ?? (object?)MaxCycles)}회";
    public double CyclesFraction
    {
        get
        {
            var total = MacroValues.ToLong(variables.GetValueOrDefault("max_cycles") ?? (object?)MaxCycles);
            return total > 0 ? Math.Clamp(MacroValues.ToLong(variables.GetValueOrDefault("cycles")) / (double)total, 0, 1) : 0;
        }
    }

    public string ConfirmedBattlesText => confirmedBattles.ToString("N0", CultureInfo.CurrentCulture);
    public string ConfirmedBattlesNote => confirmedBattles > 0 && allSRank ? "전 · 모두 S" : "전 · 패킷 기준";
    public string NewShipsText => newShips.ToString("N0", CultureInfo.CurrentCulture);

    public string NowText => nowText.Length > 0 ? nowText : IsRunning ? "시작하는 중" : "실행 전";
    public string NowTrail => nowTrail;

    public bool HasLastBattle => liveState?.Battle.LastId > 0;
    public bool HasNoLastBattle => !HasLastBattle;
    public string LastBattleRank => liveState?.Battle.Rank is { Length: > 0 } rank ? rank : "?";
    public string LastBattleStage => liveState?.Battle.Stage is { Length: > 0 } stage ? stage : $"해역 {liveState?.Battle.StageId}";
    public string LastBattleDetail
    {
        get
        {
            if (liveState is null) return "";
            var battle = liveState.Battle;
            var parts = new List<string>();
            if (battle.MvpName.Length > 0) parts.Add($"MVP {battle.MvpName}");
            parts.Add($"함선 드롭 {battle.ShipDrops}");
            parts.Add($"결과 패킷 {battle.LastId}");
            return string.Join(" · ", parts);
        }
    }

    public string LastBattleAgo => lastBattleAt is { } at ? Ago(at) : "";

    public bool IsEnded
    {
        get => isEnded;
        private set => SetProperty(ref isEnded, value);
    }

    public bool EndedIsError
    {
        get => endedIsError;
        private set => SetProperty(ref endedIsError, value);
    }

    public string EndedTitle
    {
        get => endedTitle;
        private set => SetProperty(ref endedTitle, value);
    }

    public string EndedDetail
    {
        get => endedDetail;
        private set => SetProperty(ref endedDetail, value);
    }

    public void Stop()
    {
        if (!IsRunning || cancellation is null) return;
        IsStopping = true;
        cancellation.Cancel();
        AddLog(new MacroLogRow(DateTime.Now, MacroLogSource.Macro, "정지 요청 · 지금 단계가 끝나면 멈춥니다."));
    }

    private void Start()
    {
        RefreshStartState();
        if (!CanStart || SelectedMacro is null || host.ResolveAdbPath() is not { } adb) return;
        var macro = SelectedMacro;
        var runVariables = new Dictionary<string, object?>(StringComparer.Ordinal);
        if (ShowMaxCycles && MaxCycles is { } cycles) runVariables["max_cycles"] = (long)cycles;

        cancellation?.Dispose();
        cancellation = new CancellationTokenSource();
        var token = cancellation.Token;
        variables = runVariables;
        runStartedAt = DateTime.Now;
        lastBattleId = liveState?.Battle.LastId ?? 0;
        confirmedBattles = 0;
        newShips = 0;
        allSRank = true;
        lastMacroMessage = "";
        nowText = "";
        nowTrail = "";
        IsEnded = false;
        LogRows.Clear();
        IsRunning = true;
        IsStopping = false;
        clock.Start();
        RefreshRunTexts();

        var options = new MacroRunOptions
        {
            AlautoRoot = alautoRoot,
            BaseRuntime = ReadConfig(),
            Game = gameState,
            OnEvent = macroEvent => dispatcher.BeginInvoke(() => Apply(macroEvent))
        };
        var device = new AdbDevice(adb, host.Serial());
        Task.Run(() =>
        {
            // Again at start: the game may have moved to another display since the check (MuMu runs apps on their own).
            var display = device.ResolveGameDisplay() ??
                          throw new MacroUnsupportedException("벽람항로 화면을 찾지 못했습니다. 게임을 켠 뒤 다시 시작하세요.");
            dispatcher.BeginInvoke(() => AddLog(new MacroLogRow(DateTime.Now, MacroLogSource.Screen, $"게임 화면: {display.Text}")));
            using var runner = new MacroRunner(device, options, token);
            return runner.RunPath(macro.Info.Path, runVariables);
        }, token).ContinueWith(task => dispatcher.BeginInvoke(() => Finish(macro, task)), TaskScheduler.Default);
    }

    private void Finish(MacroItem macro, Task<object?> task)
    {
        clock.Stop();
        IsRunning = false;
        IsStopping = false;
        var cycles = MacroValues.ToLong(variables.GetValueOrDefault("cycles"));
        var summary = HasCycles ? $"{cycles}회" : Elapsed(DateTime.Now - runStartedAt);
        if (task.IsCanceled || task.Exception?.InnerException is OperationCanceledException)
        {
            EndedIsError = false;
            EndedTitle = $"{macro.Title}을 정지했습니다 · {summary}";
            EndedDetail = Summary();
            macro.LastRunText = $"최근: {DateTime.Now:HH:mm} · {summary} 후 직접 정지";
        }
        else if (task.Exception?.InnerException is { } error)
        {
            var couldNotStart = error is MacroUnsupportedException or MacroSchemaException;
            EndedIsError = true;
            EndedTitle = couldNotStart
                ? $"{macro.Title}을 실행할 수 없습니다"
                : $"{macro.Title}이 오류로 멈췄습니다 · {summary}";
            EndedDetail = error.Message;
            macro.LastRunText = $"최근: {DateTime.Now:HH:mm} · 오류로 멈춤";
            AddLog(new MacroLogRow(DateTime.Now, MacroLogSource.Macro, $"오류: {error.Message}"));
            // A run refused at start is seen on the page right away; only a run that failed along the way notifies.
            if (!couldNotStart) Notify(error.Message, warning: true);
        }
        else
        {
            var completed = task.Result is true;
            EndedIsError = !completed;
            EndedTitle = $"{macro.Title}이 {summary} 만에 멈췄습니다";
            EndedDetail = (lastMacroMessage.Length > 0 ? lastMacroMessage + " " : "") + Summary();
            macro.LastRunText = $"최근: {DateTime.Now:HH:mm} · {summary} 후 정지";
            Notify(lastMacroMessage, warning: !completed);
        }

        lastRuns[macro.FileName] = macro.LastRunText;
        nowText = "";
        nowTrail = "";
        IsEnded = true;
        RefreshRunTexts();
        RefreshStartState();
    }

    private string Summary() =>
        $"확인된 전투 {confirmedBattles:N0}전 · 새 함선 {newShips:N0}척 · {DateTime.Now:HH:mm} 종료";

    // Why it stopped on the first line, what the packets confirmed on the second.
    private void Notify(string reason, bool warning)
    {
        if (!NotifyOnEnd) return;
        EndNotice?.Invoke(this, new MacroEndNotice(EndedTitle, reason.Length > 0 ? $"{reason}\n{Summary()}" : Summary(), warning));
    }

    private void Apply(MacroEvent macroEvent)
    {
        switch (macroEvent)
        {
            case MacroLogEvent log:
                if (log.Source == MacroLogSource.Macro && !log.Message.StartsWith("매크로 시작:", StringComparison.Ordinal))
                    lastMacroMessage = log.Message;
                AddLog(new MacroLogRow(log.At, log.Source, log.Message));
                break;
            case MacroStepEvent step when !QuietActions.Contains(step.Action):
                nowText = step.Description;
                nowTrail = $"{step.Trail} › {step.Action}";
                OnPropertyChanged(nameof(NowText));
                OnPropertyChanged(nameof(NowTrail));
                break;
            case MacroVariablesEvent changed:
                variables = changed.Variables;
                RefreshRunTexts();
                RefreshStops();
                break;
            case MacroScreenEvent screen:
                ShowScreen(screen);
                break;
        }
    }

    private void AddLog(MacroLogRow row)
    {
        LogRows.Insert(0, row);
        while (LogRows.Count > MaxLogRows) LogRows.RemoveAt(LogRows.Count - 1);
    }

    // Decodes at most twice a second, but always for a screen with a match so the box shows where it tapped.
    private void ShowScreen(MacroScreenEvent screen)
    {
        lastScreenAt = screen.At;
        if (screen.Match is null && (DateTime.Now - lastScreenDecodedAt).TotalMilliseconds < 500)
        {
            OnPropertyChanged(nameof(ScreenAgeText));
            return;
        }

        lastScreenDecodedAt = DateTime.Now;
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.DecodePixelWidth = (int)PreviewWidth * 2;
        image.StreamSource = new MemoryStream(screen.Png);
        image.EndInit();
        image.Freeze();
        ScreenImage = image;
        if (screen.Match is { } match)
        {
            var scale = PreviewWidth / 1920.0;
            MatchLeft = match.X * scale;
            MatchTop = match.Y * scale;
            MatchWidth = Math.Max(4, match.W * scale);
            MatchHeight = Math.Max(4, match.H * scale);
            MatchLabel = screen.MatchLabel;
            HasMatch = true;
        }
        else
        {
            HasMatch = false;
        }

        foreach (var name in new[]
                 {
                     nameof(ScreenImage), nameof(HasScreen), nameof(MatchLeft), nameof(MatchTop), nameof(MatchWidth),
                     nameof(MatchHeight), nameof(MatchLabel), nameof(HasMatch), nameof(ScreenAgeText)
                 })
        {
            OnPropertyChanged(name);
        }
    }

    // A battle result the dashboard loaded while the macro runs; packets confirm what the screen only suggests.
    private void CountBattle(LiveStateDocument document)
    {
        var battle = document.Battle;
        if (battle.LastId <= 0) return;
        if (battle.LastId != lastBattleId)
        {
            if (IsRunning && battle.LastId > lastBattleId)
            {
                confirmedBattles++;
                newShips += battle.ShipDrops;
                allSRank &= battle.Rank == "S";
            }

            // The packets carry no time; only a result that arrives while the page watches gets "N초 전".
            lastBattleAt = lastBattleId > 0 ? DateTime.Now : null;
            lastBattleId = battle.LastId;
        }

        RefreshRunTexts();
    }

    private void RefreshFleet()
    {
        var ships = host.LevelingShips().ToDictionary(ship => ship.Uid, StringComparer.Ordinal);
        FleetRows.ReplaceRange((liveState?.Leveling.Ships ?? []).Select(ship =>
        {
            var info = ships.GetValueOrDefault(ship.Uid);
            return new MacroFleetRow(ship.Name, $"Lv.{ship.Level}", ship.Waiting,
                ship.Waiting ? 1 : Math.Clamp((info?.ExperiencePercent ?? 0) / 100, 0, 1), info?.IconUrl ?? "");
        }));
        OnPropertyChanged(nameof(HasFleet));
        OnPropertyChanged(nameof(FleetTitle));
    }

    private void RebuildStops()
    {
        RefreshStops();
    }

    // "지금 {{game:leveling.growing}}척 · 시작 {{growing_at_start}}척" filled from the live state and the run's variables.
    private void RefreshStops()
    {
        var stops = SelectedMacro?.Info.Stops ?? [];
        StopRows.ReplaceRange(stops.Select(stop => new MacroStopRow(stop.Text, FillDetail(stop.Detail))));
        OnPropertyChanged(nameof(HasStops));
    }

    // A detail with a value still unknown (before the run sets it) is left out rather than shown with gaps.
    private string FillDetail(string template)
    {
        var missing = false;
        var filled = Regex.Replace(template, @"\{\{(game:)?([A-Za-z0-9_.]+)\}\}", match =>
        {
            var value = match.Groups[1].Success
                ? gameState.Get(match.Groups[2].Value)
                : variables.GetValueOrDefault(match.Groups[2].Value) ??
                  (match.Groups[2].Value == "max_cycles" ? (object?)MaxCycles : null);
            missing |= value is null;
            return MacroValues.Format(value);
        });
        return missing ? "" : filled;
    }

    private void RefreshClock()
    {
        OnPropertyChanged(nameof(RunStartedText));
        OnPropertyChanged(nameof(LastBattleAgo));
        OnPropertyChanged(nameof(ScreenAgeText));
    }

    private void RefreshRunTexts()
    {
        foreach (var name in new[]
                 {
                     nameof(RunStartedText), nameof(HasCycles), nameof(CyclesText), nameof(CyclesTotalText),
                     nameof(CyclesFraction), nameof(ConfirmedBattlesText), nameof(ConfirmedBattlesNote),
                     nameof(NewShipsText), nameof(NowText), nameof(NowTrail), nameof(HasLastBattle), nameof(HasNoLastBattle),
                     nameof(LastBattleRank), nameof(LastBattleStage), nameof(LastBattleDetail), nameof(LastBattleAgo),
                     nameof(StateText)
                 })
        {
            OnPropertyChanged(name);
        }
    }

    private JsonObject ReadConfig()
    {
        try
        {
            return JsonNode.Parse(File.ReadAllText(Path.Combine(alautoRoot, "config.json"))) as JsonObject ?? new JsonObject();
        }
        catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException or UnauthorizedAccessException)
        {
            return new JsonObject();
        }
    }

    private static string Elapsed(TimeSpan span) =>
        span.TotalMinutes < 1 ? $"{(int)span.TotalSeconds}초" :
        span.TotalHours < 1 ? $"{(int)span.TotalMinutes}분" : $"{(int)span.TotalHours}시간 {span.Minutes}분";
}
