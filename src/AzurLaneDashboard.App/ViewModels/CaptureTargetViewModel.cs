using System.Globalization;
using AzurLaneDashboard.Capture;
using AzurLaneDashboard.Data.Services;

namespace AzurLaneDashboard.App.ViewModels;

/// <summary>
/// One emulator (ADB serial) or the PC's network interface, captured by its own service. Each capture sends its
/// packets to the database of the account logged in on it, so several emulators can run at once.
/// </summary>
public sealed class CaptureTargetViewModel : ObservableObject
{
    private bool isRunning;
    private bool isStopping;
    private string stateText = "꺼짐";
    private string accountText = "";
    private string detailText = "";
    private string kind = "";
    private long accountUid;

    public CaptureTargetViewModel(string key, string label, bool isAdb)
    {
        Key = key;
        Label = label;
        IsAdb = isAdb;
        Refresh(false, null);
    }

    /// <summary>The ADB serial, or <see cref="HostKey"/> for the PC capture. Stored with each login.</summary>
    public string Key { get; }

    public string Label { get; }
    public bool IsAdb { get; }
    public DumpcapCaptureService Service { get; } = new();
    public Task? RunTask { get; set; }

    public RelayCommand ToggleCommand { get; set; } = null!;
    public RelayCommand RemoveCommand { get; set; } = null!;

    public bool IsRunning
    {
        get => isRunning;
        private set
        {
            if (SetProperty(ref isRunning, value))
            {
                OnPropertyChanged(nameof(ToggleText));
                ToggleCommand?.RaiseCanExecuteChanged();
                RemoveCommand?.RaiseCanExecuteChanged();
            }
        }
    }

    public string ToggleText => IsRunning ? "중지" : "시작";

    public string StateText
    {
        get => stateText;
        private set => SetProperty(ref stateText, value);
    }

    /// <summary>Whose packets these are: the account name, or why it is not known yet.</summary>
    public string AccountText
    {
        get => accountText;
        private set => SetProperty(ref accountText, value);
    }

    public string DetailText
    {
        get => detailText;
        private set => SetProperty(ref detailText, value);
    }

    /// <summary>good, warn, bad or empty, for the row's state dot.</summary>
    public string Kind
    {
        get => kind;
        private set => SetProperty(ref kind, value);
    }

    /// <summary>The account this capture writes to while it runs; 0 when none.</summary>
    public long AccountUid
    {
        get => accountUid;
        private set => SetProperty(ref accountUid, value);
    }

    /// <summary>The account it wrote to last, kept after it stops.</summary>
    public GameAccount? Account { get; private set; }

    /// <summary>Why the last run failed to start or stopped, shown after its log lines.</summary>
    public string LastError { get; set; } = "";

    /// <summary>Another running capture writes to the same account: likely the same emulator under two serials.</summary>
    public bool IsDuplicate { get; set; }

    public const string HostKey = "host";

    public void MarkStopping()
    {
        isStopping = true;
        Refresh(RunTask is { IsCompleted: false }, Account);
    }

    /// <param name="lastKnown">The account this target's last login named, shown while it is stopped.</param>
    public void Refresh(bool taskRunning, GameAccount? lastKnown)
    {
        var status = Service.Status;
        var running = status.Running || taskRunning;
        if (!running) isStopping = false;
        IsRunning = running;
        Account = status.Account;
        AccountUid = running && !status.WaitingForLogin ? status.Account?.Uid ?? 0 : 0;

        StateText = !running ? "꺼짐" : isStopping ? "중지 중" : "수집 중";
        AccountText = !running
            ? (lastKnown ?? status.Account) is { } last ? $"마지막 계정 {AccountLabel(last)}" : "아직 로그인 기록 없음"
            : RunningAccountText(status);
        if (running && IsDuplicate) AccountText += " · 다른 캡처와 같은 계정";
        var pending = status.PendingPackets > 0
            ? $" · 대기 {status.PendingPackets.ToString("N0", CultureInfo.InvariantCulture)}"
            : "";
        DetailText = status.PacketsSeen == 0 && !running
            ? ""
            : $"패킷 {status.PacketsSeen.ToString("N0", CultureInfo.InvariantCulture)} · 해석 {status.PacketsDecoded.ToString("N0", CultureInfo.InvariantCulture)}" +
              (status.DecodeFailures > 0 ? $" · 실패 {status.DecodeFailures.ToString("N0", CultureInfo.InvariantCulture)}" : "") +
              pending;
        Kind = !running
            ? ""
            : status.LikelyDuplicate
                ? "bad"
                : IsDuplicate || status.WaitingForLogin
                    ? "warn"
                    : "good";
    }

    // Whose packets these are while it runs, and when they are not written yet, why and what makes it go on.
    private static string RunningAccountText(CaptureStatus status)
    {
        if (status.LikelyDuplicate) return "같은 에뮬레이터를 두 시리얼로 잡은 것 같습니다 · 하나를 목록에서 빼세요";
        if (status.LoggedInElsewhere.Length > 0)
        {
            return $"{AccountLabel(status.Account!)} · {status.LoggedInElsewhere}에서 로그인함 · 다음 로그인 대기";
        }

        if (status.ShipsUnmatched) return "함선이 아는 계정과 맞지 않습니다 · 새 계정이면 게임에서 다시 로그인하세요";
        if (status.Account is null) return "계정 확인 대기 · 게임에 로그인하세요";
        if (status.AccountAssumed)
        {
            return $"{AccountLabel(status.Account)} 추정 · 출격하거나 편성을 바꾸면 함선으로 확인합니다";
        }

        return status.WaitingForLogin ? "로그인 확인 중" : AccountLabel(status.Account);
    }

    public static string AccountLabel(GameAccount account) =>
        string.Join(" · ", new[]
        {
            account.DisplayName,
            account.Level > 0 ? $"Lv.{account.Level.ToString(CultureInfo.InvariantCulture)}" : "",
            account.ServerLabel
        }.Where(part => part.Length > 0));
}
