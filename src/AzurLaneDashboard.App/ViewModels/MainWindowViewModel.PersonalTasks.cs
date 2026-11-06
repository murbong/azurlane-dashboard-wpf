namespace AzurLaneDashboard.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    /// <summary>나의 함대 › 캐릭터 임무. Takes the next tab index so saved indices keep their pages.</summary>
    public const int PersonalTaskPageIndex = 15;

    // Segoe Fluent "Dictionary" glyph: an open book for the characters' story chains.
    private const string PersonalTaskGlyph = "\uE82D";

    public PersonalTaskViewModel PersonalTasks { get; } = new();
}
