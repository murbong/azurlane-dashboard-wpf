using System.Text.Json;
using AzurLaneDashboard.Core.Models;

namespace AzurLaneDashboard.Automation.Engine;

/// <summary>
/// The dashboard's live state for "game" conditions, read in-process: the same keys as live_state.json without
/// going through the file. Updated from the UI thread after each load and read by the macro thread.
/// </summary>
public sealed class LiveGameState : IGameState
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
    private static readonly TimeSpan StaleAfter = TimeSpan.FromSeconds(30);

    private volatile Snapshot? current;

    public void Update(LiveStateDocument document)
    {
        current = new Snapshot(JsonSerializer.SerializeToElement(document, Options), document.Capture.Running,
            DateTimeOffset.FromUnixTimeMilliseconds((long)(document.WrittenUnix * 1000)));
    }

    public object? Get(string key)
    {
        var snapshot = current;
        if (key == "live")
            return snapshot is not null && snapshot.Running && DateTimeOffset.Now - snapshot.WrittenAt <= StaleAfter;
        if (snapshot is null) return null;
        var element = snapshot.Document;
        foreach (var part in key.Split('.'))
        {
            if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(part, out element)) return null;
        }

        return MacroValues.FromJsonElement(element);
    }

    private sealed record Snapshot(JsonElement Document, bool Running, DateTimeOffset WrittenAt);
}
