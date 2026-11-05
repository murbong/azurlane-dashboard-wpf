using AzurLaneDashboard.Core.Models;

namespace AzurLaneDashboard.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    // Every capture's log in one list; with several emulators each line names the one it came from.
    private void SyncCaptureLogs()
    {
        var snapshots = CaptureTargets.Select(target => (Target: target, Log: target.Service.GetLogSnapshot())).ToList();
        var key = string.Join("|", snapshots.Select(item => $"{item.Target.Key}:{item.Log.Version}:{item.Target.LastError}"));
        if (captureLogKey == key)
        {
            return;
        }

        var prefixed = snapshots.Count > 1;
        var lines = snapshots.SelectMany(item =>
        {
            var text = item.Log.Lines.AsEnumerable();
            if (item.Target.LastError.Length > 0) text = text.Append($"ERROR: {item.Target.LastError}");
            return text.Select(line => prefixed ? $"[{item.Target.Label}] {line}" : line);
        }).ToList();
        if (lines.Count == 0 && IsCaptureRunning) lines.Add("캡처를 시작합니다.");
        Replace(CaptureLogLines, lines.TakeLast(1000).Select(line => new OperationLogLine { Text = line }));
        captureLogKey = key;
        OnPropertyChanged(nameof(CaptureLogCountText));
    }

    private void AddUpdateLog(string text)
    {
        if (isDisposed)
        {
            return;
        }

        UpdateLogLines.Add(new OperationLogLine { Text = text });
        if (UpdateLogLines.Count > 1000)
        {
            UpdateLogLines.RemoveAt(0);
        }

        OnPropertyChanged(nameof(UpdateLogCountText));
    }

    private void QueueUpdateLog(string text)
    {
        if (isDisposed)
        {
            return;
        }

        pendingUpdateLogLines.Add(new OperationLogLine { Text = text });
        if (!updateLogFlushTimer.IsEnabled)
        {
            updateLogFlushTimer.Start();
        }
    }

    private void UpdateLogFlushTimerTick(object? sender, EventArgs e)
    {
        FlushPendingUpdateLogs();
    }

    private void FlushPendingUpdateLogs()
    {
        if (isDisposed)
        {
            pendingUpdateLogLines.Clear();
            updateLogFlushTimer.Stop();
            return;
        }

        if (pendingUpdateLogLines.Count == 0)
        {
            updateLogFlushTimer.Stop();
            return;
        }

        var lines = pendingUpdateLogLines.ToArray();
        pendingUpdateLogLines.Clear();

        if (UpdateLogLines is BulkObservableCollection<OperationLogLine> bulk)
        {
            bulk.AddRangeAndTrimStart(lines, 1000);
        }
        else
        {
            foreach (var line in lines)
            {
                UpdateLogLines.Add(line);
            }

            while (UpdateLogLines.Count > 1000)
            {
                UpdateLogLines.RemoveAt(0);
            }
        }

        OnPropertyChanged(nameof(UpdateLogCountText));
        if (pendingUpdateLogLines.Count == 0)
        {
            updateLogFlushTimer.Stop();
        }
    }

}
