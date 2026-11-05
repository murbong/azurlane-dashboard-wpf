using System.IO;
using System.Windows;
using AzurLaneDashboard.App.Services;
using AzurLaneDashboard.Data.Services;

namespace AzurLaneDashboard.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private async Task CompactDatabaseAsync()
    {
        if (IsCompactingDatabase || IsReloading || IsUpdating)
        {
            return;
        }

        IsCompactingDatabase = true;
        var autoRefreshWasEnabled = autoRefreshTimer.IsEnabled;
        autoRefreshTimer.Stop();
        Status = "DB 정리 중...";
        DatabaseMaintenanceText = "원본 payload와 오래된 실패 로그를 빠르게 정리 중입니다. 파일 축소 VACUUM은 실행하지 않습니다.";

        try
        {
            var path = DatabasePath;
            var options = new DatabaseMaintenanceOptions
            {
                ClearPacketPayloads = true,
                KeepDecodeFailuresPerPacket = 3,
                KeepRecentDecodedRows = 0,
                Vacuum = false
            };
            var result = await Task.Run(() => databaseMaintenanceService.Compact(path, options));
            var fileText = result.SizeAfterBytes < result.SizeBeforeBytes
                ? $"{FormatBytes(result.SizeBeforeBytes)} -> {FormatBytes(result.SizeAfterBytes)}"
                : $"{FormatBytes(result.SizeAfterBytes)} 유지";
            DatabaseMaintenanceText =
                $"빠른 정리 완료: DB 파일 {fileText} " +
                $"(payload {result.PacketPayloadRowsDeleted:N0}개, 실패 {result.DecodeFailureRowsDeleted:N0}개 삭제, 파일 축소는 별도 깊은 정리 필요)";
            Status = DatabaseMaintenanceText;
            UpdateDatabaseWriteInfo();
        }
        catch (Exception ex)
        {
            DatabaseMaintenanceText = $"DB 정리 실패: {ex.Message}";
            Status = DatabaseMaintenanceText;
        }
        finally
        {
            IsCompactingDatabase = false;
            if (autoRefreshWasEnabled && !isDisposed)
            {
                autoRefreshTimer.Start();
            }
        }
    }

    private async Task DeepCompactDatabaseAsync()
    {
        if (IsCompactingDatabase || IsReloading)
        {
            return;
        }

        if (IsUpdating)
        {
            DatabaseMaintenanceText = "업데이트가 실행 중이라 깊은 DB 정리를 막았습니다. 업데이트가 끝난 뒤 다시 실행하세요.";
            Status = DatabaseMaintenanceText;
            return;
        }

        if (IsCaptureRunning)
        {
            DatabaseMaintenanceText = "실시간 캡처가 켜져 있어 깊은 DB 정리를 막았습니다. 캡처를 중지한 뒤 다시 실행하세요.";
            Status = DatabaseMaintenanceText;
            return;
        }

        var confirm = AppDialogService.Show(
            "깊은 DB 정리는 payload 정리 후 새 SQLite DB를 만든 뒤 원본을 교체합니다.\n\n" +
            "진행 중에는 새로고침과 자동 갱신이 멈추며, 실시간 캡처는 꺼져 있어야 합니다.\n\n" +
            "계속할까요?",
            "깊은 DB 정리 확인",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        if (confirm != MessageBoxResult.Yes)
        {
            DatabaseMaintenanceText = "깊은 DB 정리를 취소했습니다.";
            return;
        }

        IsCompactingDatabase = true;
        var autoRefreshWasEnabled = autoRefreshTimer.IsEnabled;
        var reloadAfterSuccess = false;
        autoRefreshTimer.Stop();
        Status = "깊은 DB 정리 중...";
        DatabaseMaintenanceText = "원본 payload와 오래된 실패 로그를 정리하고, VACUUM INTO로 새 DB를 만드는 중입니다.";

        try
        {
            var path = DatabasePath;
            var options = new DatabaseMaintenanceOptions
            {
                ClearPacketPayloads = true,
                KeepDecodeFailuresPerPacket = 3,
                KeepRecentDecodedRows = 0,
                Vacuum = false
            };
            var result = await Task.Run(() => databaseMaintenanceService.DeepCompact(path, options));
            var fileText = result.SizeAfterBytes < result.SizeBeforeBytes
                ? $"{FormatBytes(result.SizeBeforeBytes)} -> {FormatBytes(result.SizeAfterBytes)}"
                : $"{FormatBytes(result.SizeAfterBytes)} 유지";
            DatabaseMaintenanceText =
                $"깊은 정리 완료: DB 파일 {fileText} " +
                $"(payload {result.PacketPayloadRowsDeleted:N0}개, 실패 {result.DecodeFailureRowsDeleted:N0}개 삭제)";
            Status = DatabaseMaintenanceText;
            UpdateDatabaseWriteInfo();
            reloadAfterSuccess = true;
        }
        catch (DatabaseMaintenanceFileReplaceException ex)
        {
            DatabaseMaintenanceText =
                "깊은 DB 정리 실패: 압축은 끝났지만 원본 DB 파일 교체가 막혔습니다. " +
                "캡처, 업데이트, 다른 앱의 DB 사용을 멈춘 뒤 다시 시도하세요. " +
                $"압축된 임시 DB: {ex.CompactedPath}";
            Status = DatabaseMaintenanceText;
        }
        catch (Exception ex)
        {
            DatabaseMaintenanceText = $"깊은 DB 정리 실패: {ex.Message}";
            Status = DatabaseMaintenanceText;
        }
        finally
        {
            IsCompactingDatabase = false;
            if (autoRefreshWasEnabled && !isDisposed)
            {
                autoRefreshTimer.Start();
            }
        }

        if (reloadAfterSuccess)
        {
            await ReloadAsync(persistSettings: false);
        }
    }

    private async Task CleanupRetiredStorageAsync()
    {
        if (IsCompactingDatabase || IsReloading || IsUpdating)
        {
            return;
        }

        if (IsCaptureRunning)
        {
            RetiredStorageText = "실시간 캡처가 켜져 있어 정리를 막았습니다. 캡처를 중지한 뒤 다시 실행하세요.";
            return;
        }

        var path = DatabasePath;
        RetiredStoragePlan plan;
        try
        {
            plan = await Task.Run(() => databaseMaintenanceService.InspectRetiredStorage(path));
        }
        catch (Exception ex)
        {
            RetiredStorageText = $"DB를 확인하지 못했습니다: {ex.Message}";
            return;
        }

        if (plan.IsEmpty)
        {
            RetiredStorageText = "정리할 항목이 없습니다. 이미 정리된 DB입니다.";
            return;
        }

        var confirm = AppDialogService.Show(
            $"다음 항목을 지웁니다: {plan.SummaryText}\n\n" +
            RetiredStorageDetail(plan) + "\n\n" +
            $"지우기 전에 DB 전체를 같은 폴더의 '{Path.GetFileNameWithoutExtension(path)}.backup-날짜시각" +
            $"{Path.GetExtension(path)}' 파일로 복사하고, 복사본을 검사한 뒤에만 지웁니다.\n" +
            "진행 중에는 새로고침과 자동 갱신이 멈춥니다.\n\n계속할까요?",
            "DB 정리 확인",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        if (confirm != MessageBoxResult.Yes)
        {
            RetiredStorageText = "정리를 취소했습니다. DB는 바뀌지 않았습니다.";
            return;
        }

        IsCompactingDatabase = true;
        var autoRefreshWasEnabled = autoRefreshTimer.IsEnabled;
        var reloadAfterSuccess = false;
        autoRefreshTimer.Stop();
        Status = "DB 백업 후 정리 중...";
        RetiredStorageText = "DB를 백업하고 검사하는 중입니다...";

        try
        {
            var result = await Task.Run(() => databaseMaintenanceService.CleanupRetiredStorage(path));
            var sizeText = result.Compacted
                ? $"DB {FormatBytes(result.SizeBeforeBytes)} → {FormatBytes(result.SizeAfterBytes)}"
                : "파일 크기는 다음 깊은 DB 정리 때 줄어듭니다";
            RetiredStorageText =
                $"정리 완료: {result.Removed.SummaryText} 삭제 · {sizeText} · 백업 {Path.GetFileName(result.BackupPath)}";
            Status = RetiredStorageText;
            UpdateDatabaseWriteInfo();
            reloadAfterSuccess = true;
        }
        catch (Exception ex)
        {
            RetiredStorageText = $"정리 실패: {ex.Message} 지우기 전에 멈췄으므로 DB는 바뀌지 않았습니다.";
            Status = RetiredStorageText;
        }
        finally
        {
            IsCompactingDatabase = false;
            if (autoRefreshWasEnabled && !isDisposed)
            {
                autoRefreshTimer.Start();
            }
        }

        if (reloadAfterSuccess)
        {
            await ReloadAsync(persistSettings: false);
        }
    }

    private static string RetiredStorageDetail(RetiredStoragePlan plan)
    {
        var lines = new List<string>();
        if (plan.Tables.Count > 0)
        {
            lines.Add($"· 테이블: {string.Join(", ", plan.Tables)}");
        }

        if (plan.Columns.Count > 0)
        {
            lines.Add("· 빈 열: " + string.Join(", ", plan.Columns
                .GroupBy(column => column.Table)
                .Select(group => $"{group.Key} {group.Count()}개")));
        }

        if (plan.Indexes.Count > 0)
        {
            lines.Add($"· 중복 인덱스: {string.Join(", ", plan.Indexes)}");
        }

        return string.Join("\n", lines);
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB"];
        var value = (double)bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return $"{value:N1} {units[unit]}";
    }
}
