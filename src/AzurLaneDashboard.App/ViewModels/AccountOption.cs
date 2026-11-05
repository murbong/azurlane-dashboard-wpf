using System.Globalization;
using AzurLaneDashboard.Data.Services;

namespace AzurLaneDashboard.App.ViewModels;

/// <summary>One account in the sidebar picker: who it is, and the emulator capturing it right now, if any.</summary>
public sealed class AccountOption : ObservableObject
{
    private string title = "";
    private string detail = "";
    private string serverText = "";
    private string summary = "";
    private string databasePath = "";
    private string liveText = "";

    public AccountOption(long uid)
    {
        Uid = uid;
    }

    public long Uid { get; }

    public string Title
    {
        get => title;
        private set => SetProperty(ref title, value);
    }

    /// <summary>Level, server and uid, for telling two accounts with similar names apart.</summary>
    public string Detail
    {
        get => detail;
        private set => SetProperty(ref detail, value);
    }

    /// <summary>The server's name, or its number while the name is unknown.</summary>
    public string ServerText
    {
        get => serverText;
        private set => SetProperty(ref serverText, value);
    }

    /// <summary>Name, level and server on one line, for the sidebar when there is a single account.</summary>
    public string Summary
    {
        get => summary;
        private set => SetProperty(ref summary, value);
    }

    public string DatabasePath
    {
        get => databasePath;
        private set => SetProperty(ref databasePath, value);
    }

    /// <summary>The emulator capturing this account now; empty when none is.</summary>
    public string LiveText
    {
        get => liveText;
        private set
        {
            if (SetProperty(ref liveText, value)) OnPropertyChanged(nameof(IsLive));
        }
    }

    public bool IsLive => LiveText.Length > 0;

    public void Update(GameAccount account, string liveTarget)
    {
        Title = account.DisplayName;
        ServerText = account.ServerLabel;
        var level = account.Level > 0 ? $"Lv.{account.Level.ToString(CultureInfo.InvariantCulture)}" : "";
        var uid = $"UID {account.Uid.ToString(CultureInfo.InvariantCulture)}";
        Detail = string.Join(" · ", new[] { level, ServerText, uid }.Where(part => part.Length > 0));
        Summary = string.Join(" · ", new[] { Title, level, ServerText }.Where(part => part.Length > 0));
        DatabasePath = account.DatabasePath;
        SetLive(liveTarget);
    }

    public void SetLive(string liveTarget) => LiveText = liveTarget.Length > 0 ? $"{liveTarget} 수집 중" : "";
}
