using System.IO;
using System.Text.Json;
using AzurLaneDashboard.Core.Configuration;
using AzurLaneDashboard.Core.Models;

namespace AzurLaneDashboard.Data.Services;

public sealed class AppSettingsService
{
    private static readonly Lazy<string> CachedSettingsPath = new(ResolveSettingsPath);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };
    private string lastSerializedSettings = "";

    public AppSettingsService(string? settingsPath = null)
    {
        SettingsPath = settingsPath ?? CachedSettingsPath.Value;
    }

    public string SettingsPath { get; }

    private static string ResolveSettingsPath() =>
        ProjectLocator.IsPortableDistribution()
            ? Path.Combine(ProjectLocator.FindProjectRoot(), AppDefaults.SettingsFileName)
            : Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "AzurLaneDashboard",
                AppDefaults.SettingsFileName);

    public AppSettings Load()
    {
        if (!File.Exists(SettingsPath))
        {
            return new AppSettings();
        }

        try
        {
            var json = File.ReadAllText(SettingsPath);
            lastSerializedSettings = json;
            return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        if (string.Equals(json, lastSerializedSettings, StringComparison.Ordinal) && File.Exists(SettingsPath))
        {
            return;
        }

        var directory = Path.GetDirectoryName(SettingsPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        // Write beside the destination so replacement is atomic on the same volume.
        var temporaryPath = SettingsPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporaryPath, json);
            File.Move(temporaryPath, SettingsPath, overwrite: true);
            lastSerializedSettings = json;
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }
}
