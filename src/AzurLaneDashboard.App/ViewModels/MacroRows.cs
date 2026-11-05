using System.Globalization;
using AzurLaneDashboard.Automation.Engine;

namespace AzurLaneDashboard.App.ViewModels;

/// <summary>A macro file in the list, with what it looks at and how its last run in this session ended.</summary>
public sealed class MacroItem(MacroInfo info) : ObservableObject
{
    private string lastRunText = "";

    public MacroInfo Info { get; } = info;
    public string Title => Info.Title;
    public string FileName => Info.FileName;
    public string Description => Info.Description;
    public bool HasDescription => Info.Description.Length > 0;
    public bool UsesPackets => Info.UsesPackets;
    public bool UsesScreen => Info.UsesScreen;
    public bool IsSupported => Info.IsSupported;
    public bool IsUnsupported => !Info.IsSupported;
    public string UnsupportedReason => Info.UnsupportedReason;

    public string LastRunText
    {
        get => lastRunText;
        set
        {
            if (SetProperty(ref lastRunText, value)) OnPropertyChanged(nameof(HasLastRun));
        }
    }

    public bool HasLastRun => LastRunText.Length > 0;
}

/// <summary>One of the four checks above the macro page: device, capture, assets, farming fleet.</summary>
public sealed class MacroReadiness(string title, string actionText = "") : ObservableObject
{
    private string detail = "확인 전";
    private bool isOk;
    private bool isChecked;

    public string Title { get; } = title;
    /// <summary>A fix the card offers while the check fails ("캡처 시작"); empty for none.</summary>
    public string ActionText { get; } = actionText;
    public bool ShowsAction => ActionText.Length > 0 && IsChecked && !IsOk;

    public string Detail
    {
        get => detail;
        private set => SetProperty(ref detail, value);
    }

    public bool IsOk
    {
        get => isOk;
        private set => SetProperty(ref isOk, value);
    }

    /// <summary>False until the first check finishes; the mark then shows ✓ or ✕.</summary>
    public bool IsChecked
    {
        get => isChecked;
        private set => SetProperty(ref isChecked, value);
    }

    public string MarkText => !IsChecked ? "…" : IsOk ? "✓" : "✕";

    public void Set(bool ok, string text)
    {
        IsOk = ok;
        Detail = text;
        IsChecked = true;
        OnPropertyChanged(nameof(MarkText));
        OnPropertyChanged(nameof(ShowsAction));
    }
}

public sealed record MacroFleetRow(string Name, string LevelText, bool IsWaiting, double ExpFraction, string IconUrl)
{
    public string ToolTip => IsWaiting ? $"{Name} · {LevelText} · 각성 대기(EXP 가득)" : $"{Name} · {LevelText}";
}

public sealed record MacroStopRow(string Text, string Detail)
{
    public bool HasDetail => Detail.Length > 0;
}

/// <summary>A run that stopped by itself, for the Windows notification; a warning when it did not finish.</summary>
public sealed record MacroEndNotice(string Title, string Detail, bool IsWarning);

public sealed record MacroLogRow(DateTime At, MacroLogSource Source, string Message)
{
    public string TimeText => At.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
    public string SourceText => Source switch
    {
        MacroLogSource.Packet => "패킷",
        MacroLogSource.Screen => "화면",
        _ => "매크로"
    };
}
