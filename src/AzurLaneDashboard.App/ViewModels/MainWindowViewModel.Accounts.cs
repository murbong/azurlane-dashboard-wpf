using System.IO;
using AzurLaneDashboard.Core.Configuration;
using AzurLaneDashboard.Data.Services;

namespace AzurLaneDashboard.App.ViewModels;

/// <summary>
/// Which account the dashboard shows. Every account keeps its own database (see <see cref="AccountRegistry"/>);
/// picking one in the sidebar points <see cref="DatabasePath"/> at it and reloads, so every page, the growth
/// goals and live_state.json follow. The macro drives the emulator that account last logged in on.
/// </summary>
public sealed partial class MainWindowViewModel
{
    // Off for checks that build the window model: they never write accounts.json beside a real database or
    // probe an emulator.
    private readonly bool backgroundServicesEnabled;
    private AccountRegistry accountRegistry = null!;
    private AccountOption? selectedAccount;
    // Between an account switch and its first load the fleet and battle state still belong to the old account.
    private bool liveStatePaused;

    public BulkObservableCollection<AccountOption> Accounts { get; } = new();

    public bool HasAccounts => Accounts.Count > 0;

    /// <summary>The sidebar picker shows once there is a second account to switch to.</summary>
    public bool HasMultipleAccounts => Accounts.Count > 1;

    /// <summary>With a single account the sidebar names it on one line instead of a picker.</summary>
    public bool ShowsSingleAccount => Accounts.Count == 1 && SelectedAccount is not null;

    public bool CanSwitchAccount => Macros?.IsRunning != true;

    public string AccountPickerToolTip => CanSwitchAccount
        ? "볼 계정 선택 · 계정마다 데이터를 따로 저장합니다"
        : "매크로 실행 중에는 계정을 바꿀 수 없습니다";

    public AccountOption? SelectedAccount
    {
        get => selectedAccount;
        set => SwitchAccount(value);
    }

    /// <summary>The account whose database is on screen; null for a database no login was seen in.</summary>
    public GameAccount? DisplayedAccount => accountRegistry?.FindByDatabasePath(DatabasePath);

    private void InitializeAccounts()
    {
        accountRegistry = AccountRegistry.ForDataDirectory(ResolveAccountDataDirectory());
        accountRegistry.Changed += OnAccountsChanged;
        if (backgroundServicesEnabled)
        {
            RegisterShownDatabase();
            // Accounts registered before servers were kept get theirs from the logins their databases recorded.
            accountRegistry.FillMissingServers();
        }
        // Nothing to show yet (a fresh install, or a file with no profile): open the account seen last.
        if (DisplayedAccount is null && accountRegistry.MostRecent() is { } recent)
        {
            DatabasePath = recent.DatabasePath;
        }

        RefreshAccounts();
        foreach (var target in CaptureTargets) RefreshCaptureTarget(target);
    }

    // A database from before accounts were told apart stays where it is, under the account its profile names, and
    // the emulator in 기기 연결 is where that account was played.
    private void RegisterShownDatabase() =>
        accountRegistry.RegisterDatabase(DatabasePath, CaptureTargets.FirstOrDefault()?.Key ?? "");

    private string ResolveAccountDataDirectory() =>
        Path.GetFullPath(ProjectLocator.ResolveContentPath(string.IsNullOrWhiteSpace(DataDirectory)
            ? AppDefaults.DefaultDataDirectory
            : DataDirectory));

    // 데이터 저장 경로 may have changed since startup; a capture starting now and the picker follow it.
    private void EnsureAccountRegistry()
    {
        var directory = ResolveAccountDataDirectory();
        if (string.Equals(directory, accountRegistry.DataDirectory, StringComparison.OrdinalIgnoreCase)) return;
        accountRegistry.Changed -= OnAccountsChanged;
        accountRegistry = AccountRegistry.ForDataDirectory(directory);
        accountRegistry.Changed += OnAccountsChanged;
        RegisterShownDatabase();
        RefreshAccounts();
    }

    private bool IsDisplayedAccount(GameAccount account) => SamePath(account.DatabasePath, DatabasePath);

    private static bool SamePath(string left, string right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right)) return false;
        try
        {
            return string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    // Any thread: a capture saw a login, or a database was registered.
    private void OnAccountsChanged()
    {
        if (isDisposed) return;
        uiDispatcher.BeginInvoke(() =>
        {
            if (isDisposed) return;
            RefreshAccounts();
            // The first login on a fresh install: nothing was on screen yet. A file opened on purpose (a backup
            // with a profile) stays on screen whatever logs in elsewhere.
            if (DisplayedAccount is null && CanSwitchAccount && !HasShownProfile() &&
                accountRegistry.MostRecent() is { } recent)
            {
                ShowAccount(recent.DatabasePath, recent.DisplayName);
            }
        });
    }

    private bool HasShownProfile() => File.Exists(DatabasePath) && AccountRegistry.ReadProfile(DatabasePath) is not null;

    private void RefreshAccounts()
    {
        var live = LiveTargetsByAccount();
        var existing = Accounts.ToDictionary(option => option.Uid);
        var rows = accountRegistry.Accounts.Select(account =>
        {
            var row = existing.GetValueOrDefault(account.Uid) ?? new AccountOption(account.Uid);
            row.Update(account, live.GetValueOrDefault(account.Uid, ""));
            return row;
        }).ToList();
        if (!rows.SequenceEqual(Accounts)) Replace(Accounts, rows);
        SyncSelectedAccount();
        OnPropertyChanged(nameof(HasAccounts));
        OnPropertyChanged(nameof(HasMultipleAccounts));
    }

    // Every status tick: which accounts an emulator is capturing right now.
    private void RefreshAccountLiveState()
    {
        if (accountRegistry is null) return;
        var live = LiveTargetsByAccount();
        foreach (var row in Accounts) row.SetLive(live.GetValueOrDefault(row.Uid, ""));
    }

    private Dictionary<long, string> LiveTargetsByAccount() =>
        CaptureTargets.Where(target => target.IsRunning && target.AccountUid > 0)
            .GroupBy(target => target.AccountUid)
            .ToDictionary(group => group.Key, group => string.Join(", ", group.Select(target => target.Label)));

    private void SyncSelectedAccount()
    {
        selectedAccount = Accounts.FirstOrDefault(option => SamePath(option.DatabasePath, DatabasePath));
        OnPropertyChanged(nameof(SelectedAccount));
        OnPropertyChanged(nameof(ShowsSingleAccount));
    }

    private void SwitchAccount(AccountOption? option)
    {
        // The picker clears its selection while its list is replaced; that is not a choice.
        if (option is null || isDisposed) return;
        if (SamePath(option.DatabasePath, DatabasePath))
        {
            selectedAccount = option;
            return;
        }

        if (!CanSwitchAccount)
        {
            Status = "매크로 실행 중에는 계정을 바꿀 수 없습니다.";
            // Put the picker back once it finishes its own selection change.
            uiDispatcher.BeginInvoke(() => OnPropertyChanged(nameof(SelectedAccount)));
            return;
        }

        ShowAccount(option.DatabasePath, option.Title);
    }

    private void ShowAccount(string databasePath, string title)
    {
        var previousMacroSerial = MacroSerial;
        // Macro tools reading the old account's live_state.json learn the dashboard has left it.
        WriteLiveState(captureRunning: false);
        liveStatePaused = true;
        DatabasePath = databasePath;
        SyncSelectedAccount();
        Status = $"{title} 계정을 불러오는 중...";
        SaveSettings();
        _ = ReloadAsync(persistSettings: false);
        if (backgroundServicesEnabled &&
            !string.Equals(previousMacroSerial, MacroSerial, StringComparison.OrdinalIgnoreCase))
        {
            Macros.OnDeviceChanged();
        }
    }

    private void RefreshAccountSwitchState()
    {
        OnPropertyChanged(nameof(CanSwitchAccount));
        OnPropertyChanged(nameof(AccountPickerToolTip));
    }

    /// <summary>
    /// The emulator the macro drives: where the account on screen last logged in, when that emulator is in the
    /// capture list; otherwise the serial in 기기 연결.
    /// </summary>
    private string MacroSerial
    {
        get
        {
            if (IsAdbCaptureBackend && DisplayedAccount is { LastCaptureTarget.Length: > 0 } account &&
                CaptureTargets.Any(target => target.IsAdb &&
                                             string.Equals(target.Key, account.LastCaptureTarget,
                                                 StringComparison.OrdinalIgnoreCase)))
            {
                return account.LastCaptureTarget;
            }

            return Serial;
        }
    }

    private CaptureTargetViewModel? MacroCaptureTarget => IsAdbCaptureBackend
        ? CaptureTargets.FirstOrDefault(target =>
            target.IsAdb && string.Equals(target.Key, MacroSerial, StringComparison.OrdinalIgnoreCase))
        : CaptureTargets.FirstOrDefault();

    private bool IsMacroCaptureRunning() => MacroCaptureTarget?.IsRunning == true;

    private void StartMacroCapture()
    {
        if (MacroCaptureTarget is { } target) StartCaptureTarget(target);
        else StartCapture();
    }
}
