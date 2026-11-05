using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Windows.Threading;
using AzurLaneDashboard.App.ViewModels;

namespace AzurLaneDashboard.App.Services;

// Opt-in local diagnostics for command-line launches; never exports packet contents.
internal sealed class OperationStatusWriter : IDisposable
{
    private readonly MainWindowViewModel model;
    private readonly string path;
    private readonly DispatcherTimer timer;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public OperationStatusWriter(MainWindowViewModel model, string path)
    {
        this.model = model;
        this.path = Path.GetFullPath(path);
        timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        timer.Tick += Tick;
        Write(isClosed: false);
        timer.Start();
    }

    private void Tick(object? sender, EventArgs args) => Write(isClosed: false);

    private void Write(bool isClosed)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var json = JsonSerializer.Serialize(new
            {
                ProcessId = Environment.ProcessId, UpdatedAt = DateTimeOffset.Now, IsClosed = isClosed,
                model.Status, model.DatabasePath, model.ShipCount, model.EquipmentCount,
                model.IsUpdating, model.UpdateStateLabel, model.UpdateProgressStage,
                model.UpdateProgressMessage, model.UpdateProgressValue,
                IsCaptureRunning = !isClosed && model.IsCaptureRunning,
                model.CaptureStateLabel, model.CaptureStatusDetail,
                CaptureSummary = model.CaptureStatusRows.Select(row => new { row.Label, row.Value }).ToArray(),
                UpdateLog = model.UpdateLogLines.TakeLast(12).Select(line => line.Text).ToArray(),
                CaptureLog = model.CaptureLogLines.TakeLast(12).Select(line => line.Text).ToArray()
            }, JsonOptions);
            File.WriteAllText(path + ".tmp", json);
            File.Move(path + ".tmp", path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Diagnostic output must not interrupt capture or the UI.
            System.Diagnostics.Trace.WriteLine($"Operation status write failed: {ex.Message}");
        }
    }

    public void Dispose()
    {
        timer.Stop();
        timer.Tick -= Tick;
        Write(isClosed: true);
    }
}
