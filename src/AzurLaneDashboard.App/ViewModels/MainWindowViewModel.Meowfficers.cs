namespace AzurLaneDashboard.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    /// <summary>나의 함대 › 지휘냥. Takes the next tab index so saved indices keep their pages.</summary>
    public const int MeowfficerPageIndex = 13;

    // Segoe Fluent "Contact" glyph: a person badge for the cats who command fleets.
    private const string MeowfficerGlyph = "";

    public MeowfficerViewModel Meowfficers { get; } = new();
}
