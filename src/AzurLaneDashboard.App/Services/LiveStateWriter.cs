using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using AzurLaneDashboard.Core.Models;

namespace AzurLaneDashboard.App.Services;

/// <summary>
/// Writes live_state.json next to the database for macro tools to read. The file is replaced in one move, so a
/// reader sees the old or the new state, never half of one.
/// </summary>
public static class LiveStateWriter
{
    public const string FileName = "live_state.json";

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        // Not embedded in HTML, so Korean names and the '+' of the UTC offset stay readable.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = true
    };

    public static string PathFor(string databasePath) =>
        Path.Combine(Path.GetDirectoryName(Path.GetFullPath(databasePath)) ?? "", FileName);

    /// <summary>False when the file could not be replaced (a reader held it open); the caller retries later.</summary>
    public static bool TryWrite(string path, LiveStateDocument document)
    {
        var temporary = path + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(document, Options), new UTF8Encoding(false));
            File.Move(temporary, path, overwrite: true);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }
}
