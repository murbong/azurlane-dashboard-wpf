using System.Globalization;
using AzurLaneDashboard.Core.Models;
using AzurLaneDashboard.Resources;

namespace AzurLaneDashboard.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private void RefreshOperationStatusRows()
    {
        RefreshUpdateStatusRows(null);
        RefreshCaptureStatusRows();
    }

    private void RefreshUpdateStatusRows(UpdateResult? result)
    {
        var rows = new List<UpdateStatusViewRow>
        {
            new()
            {
                Label = "상태", Value = UpdateStateLabel,
                Kind = IsUpdating ? "warn" : result?.Success == false ? "bad" : "good"
            },
            new() { Label = "단계", Value = UpdateProgressStage, Kind = IsUpdating ? "warn" : "" },
            new() { Label = "진행", Value = UpdateProgressText, Kind = IsUpdating ? "warn" : "" },
            new() { Label = "LuaScripts", Value = $"{LuaRepoDirectory} @ {LuaBranch}", Kind = "" },
            new() { Label = "맵", Value = MapsDirectory, Kind = "" },
            new() { Label = "리소스", Value = GameResourcesDirectory, Kind = "" }
        };

        if (result is not null)
        {
            rows.Add(new UpdateStatusViewRow
            {
                Label = "종료 코드",
                Value = result.ExitCode.ToString(CultureInfo.InvariantCulture),
                Kind = result.Success ? "good" : "bad"
            });
            rows.Add(new UpdateStatusViewRow
            {
                Label = "시간",
                Value = $"{result.StartedAt:HH:mm:ss} - {result.FinishedAt:HH:mm:ss}",
                Kind = ""
            });
        }

        Replace(UpdateStatusRows, rows);
    }

    private void RefreshCaptureStatusRows()
    {
        var statuses = CaptureTargets.Select(target => target.Service.Status).ToList();
        var runningCount = CaptureTargets.Count(target => target.IsRunning);
        var running = runningCount > 0;
        CaptureStateLabel = running ? "RUN" : "OFF";
        var packetsSeen = statuses.Sum(item => item.PacketsSeen);
        var packetsDecoded = statuses.Sum(item => item.PacketsDecoded);
        var decodeFailures = statuses.Sum(item => item.DecodeFailures);
        var message = statuses.Count == 1 ? statuses[0].LastMessage : "";
        Replace(CaptureStatusRows, new[]
        {
            new CaptureStatusViewRow
            {
                Label = "상태",
                Value = CaptureTargets.Count > 1 ? $"{CaptureStateLabel} {runningCount}/{CaptureTargets.Count}" : CaptureStateLabel,
                Kind = running ? "good" : "bad"
            },
            new CaptureStatusViewRow { Label = "백엔드", Value = CaptureBackend, Kind = "" },
            new CaptureStatusViewRow
                { Label = "패킷", Value = $"{packetsSeen:N0} / decoded {packetsDecoded:N0}", Kind = "" },
            new CaptureStatusViewRow
            {
                Label = "상태DB", Value = CurrentStateStatusText,
                Kind = string.IsNullOrWhiteSpace(CurrentStateLastError) ? "" : "warn"
            },
            new CaptureStatusViewRow
            {
                Label = "실패", Value = decodeFailures.ToString("N0", CultureInfo.InvariantCulture),
                Kind = decodeFailures > 0 ? "warn" : ""
            },
            new CaptureStatusViewRow
            {
                Label = "메시지",
                Value = string.IsNullOrWhiteSpace(message) ? CaptureStatusDetail : message,
                Kind = ""
            }
        });
        OnPropertyChanged(nameof(CaptureLogCountText));
    }
}
