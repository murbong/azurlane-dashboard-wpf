using AzurLaneDashboard.Automation.Vision;

namespace AzurLaneDashboard.Automation.Engine;

/// <summary>Where a log line comes from: the macro itself, the screen (images, taps) or captured packets.</summary>
public enum MacroLogSource
{
    Macro,
    Screen,
    Packet
}

public abstract record MacroEvent(DateTime At);

public sealed record MacroLogEvent(DateTime At, MacroLogSource Source, string Message) : MacroEvent(At);

/// <summary>A step began; <see cref="Trail"/> is the macro chain ("leveling_loop › combat_auto_base").</summary>
public sealed record MacroStepEvent(DateTime At, string Trail, string Action, string Description) : MacroEvent(At);

/// <summary>A new screen, with the region last matched on it (null when nothing was found).</summary>
public sealed record MacroScreenEvent(DateTime At, byte[] Png, ScreenRegion? Match, string MatchLabel) : MacroEvent(At);

public sealed record MacroVariablesEvent(DateTime At, IReadOnlyDictionary<string, object?> Variables) : MacroEvent(At);

/// <summary>Read-only game state for "game" conditions; keys match live_state.json ("battle.last_id").</summary>
public interface IGameState
{
    object? Get(string key);
}

/// <summary>No dashboard state: every key is missing and "live" is false.</summary>
public sealed class NoGameState : IGameState
{
    public static readonly NoGameState Instance = new();

    public object? Get(string key) => key == "live" ? false : null;
}
