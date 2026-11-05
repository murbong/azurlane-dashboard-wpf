using Microsoft.Data.Sqlite;

namespace AzurLaneDashboard.Data.Services;

public sealed partial class DatabaseMaintenanceService
{
    private static void CheckpointWal(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA wal_checkpoint(TRUNCATE)";
        using var reader = command.ExecuteReader();
        if (reader.Read() && reader.GetInt32(0) != 0)
        {
            throw new InvalidOperationException("다른 프로세스가 DB를 사용 중이라 WAL 체크포인트를 완료하지 못했습니다.");
        }
    }

    private static void ReplaceDatabaseFile(string databasePath, string compactedPath, string backupPath)
    {
        Exception? lastError = null;
        for (var attempt = 1; attempt <= ReplaceRetryCount; attempt++)
        {
            try
            {
                SqliteConnection.ClearAllPools();
                DeleteSqliteSidecars(databasePath);
                if (File.Exists(databasePath))
                {
                    File.Replace(compactedPath, databasePath, backupPath, true);
                }
                else
                {
                    File.Move(compactedPath, databasePath);
                }

                SqliteConnection.ClearAllPools();
                DeleteSqliteSidecars(databasePath);
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                lastError = ex;
                Thread.Sleep(ReplaceRetryDelayMilliseconds * attempt);
            }
        }

        throw new DatabaseMaintenanceFileReplaceException(
            databasePath,
            compactedPath,
            backupPath,
            lastError ?? new IOException("DB 파일 교체에 실패했습니다."));
    }

    private static void DeleteSqliteSidecars(string databasePath)
    {
        TryDeleteFile(databasePath + "-wal");
        TryDeleteFile(databasePath + "-shm");
    }

    private static string BuildMaintenancePath(string databasePath, string suffix)
    {
        var directory = Path.GetDirectoryName(databasePath);
        var fileName = Path.GetFileName(databasePath);
        return Path.Combine(
            string.IsNullOrWhiteSpace(directory) ? "." : directory,
            $".{fileName}.{Guid.NewGuid():N}.{suffix}");
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

}
