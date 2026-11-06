namespace AzurLaneDashboard.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    /// <summary>분석 및 운영 › 대형작전. Takes the next tab index so saved indices keep their pages.</summary>
    public const int OpsiPageIndex = 14;

    // Segoe Fluent "World" glyph for the Operation Siren sea map.
    private const string OpsiGlyph = "";

    public OpsiViewModel Opsi { get; } = new();
}
