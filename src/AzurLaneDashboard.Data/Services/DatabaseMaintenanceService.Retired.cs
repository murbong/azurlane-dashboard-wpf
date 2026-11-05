using System.Globalization;
using Microsoft.Data.Sqlite;

namespace AzurLaneDashboard.Data.Services;

public sealed partial class DatabaseMaintenanceService
{
    /// <summary>Lists the retired tables, columns and indexes present in the database, without changing it.</summary>
    public RetiredStoragePlan InspectRetiredStorage(string databasePath)
    {
        using var connection = OpenReadOnly(Path.GetFullPath(databasePath));
        return InspectRetiredStorage(connection, null);
    }

    /// <summary>
    /// Copies the whole database to a timestamped backup next to it, checks the copy, and only then drops the
    /// retired storage and compacts the file. Capture must be stopped: the drop needs the database to itself.
    /// </summary>
    public RetiredStorageCleanupResult CleanupRetiredStorage(string databasePath)
    {
        databasePath = Path.GetFullPath(databasePath);
        SqliteConnection.ClearAllPools();
        using var connection = Open(databasePath, pooling: false);
        var plan = InspectRetiredStorage(connection, null);
        var sizeBefore = DatabaseFileBytes(databasePath);
        if (plan.IsEmpty)
        {
            return new RetiredStorageCleanupResult("", sizeBefore, sizeBefore, plan, Compacted: false);
        }

        var backupPath = BuildBackupPath(databasePath);
        using (var backup = connection.CreateCommand())
        {
            backup.CommandText = "VACUUM INTO $target";
            backup.Parameters.AddWithValue("$target", backupPath);
            backup.ExecuteNonQuery();
        }

        VerifyBackup(connection, backupPath);

        using (var transaction = connection.BeginTransaction())
        {
            foreach (var (table, columns) in plan.Columns.GroupBy(column => column.Table)
                         .Select(group => (group.Key, group.Select(column => column.Column))))
            {
                foreach (var column in columns)
                {
                    Execute(connection, transaction, $"ALTER TABLE \"{table}\" DROP COLUMN \"{column}\"");
                }
            }

            foreach (var index in plan.Indexes)
            {
                Execute(connection, transaction, $"DROP INDEX IF EXISTS \"{index}\"");
            }

            foreach (var table in plan.Tables)
            {
                Execute(connection, transaction, $"DROP TABLE IF EXISTS \"{table}\"");
            }

            transaction.Commit();
        }

        var compacted = true;
        try
        {
            Execute(connection, null, "VACUUM");
            // In WAL mode the rebuilt pages land in the -wal file; fold them back so the main file shrinks.
            Execute(connection, null, "PRAGMA wal_checkpoint(TRUNCATE)");
        }
        catch (SqliteException)
        {
            // Another connection still holds the file. The cleanup is committed; the free pages are reused by
            // later writes, or the file shrinks on the next deep compaction.
            compacted = false;
        }

        return new RetiredStorageCleanupResult(backupPath, sizeBefore, DatabaseFileBytes(databasePath), plan, compacted);
    }

    private static RetiredStoragePlan InspectRetiredStorage(SqliteConnection connection, SqliteTransaction? transaction)
    {
        var tables = RetiredStorage.Tables
            .Where(table => TableExists(connection, table, transaction))
            .ToList();
        var indexes = RetiredStorage.Indexes
            .Where(index => IndexExists(connection, index, transaction))
            .ToList();
        var columns = new List<RetiredColumn>();
        foreach (var (table, retired) in RetiredStorage.Columns)
        {
            if (!TableExists(connection, table, transaction))
            {
                continue;
            }

            var present = TableColumns(connection, table, transaction);
            columns.AddRange(retired.Where(present.Contains).Select(column => new RetiredColumn(table, column)));
        }

        return new RetiredStoragePlan(tables, columns, indexes);
    }

    /// <summary>The backup must open cleanly and hold every row the database is about to keep.</summary>
    private static void VerifyBackup(SqliteConnection source, string backupPath)
    {
        if (!File.Exists(backupPath))
        {
            throw new InvalidOperationException($"백업 파일을 만들지 못했습니다: {backupPath}");
        }

        using var backup = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = backupPath,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false
        }.ToString());
        backup.Open();
        using (var check = backup.CreateCommand())
        {
            check.CommandText = "PRAGMA quick_check";
            var result = Convert.ToString(check.ExecuteScalar(), CultureInfo.InvariantCulture);
            if (!string.Equals(result, "ok", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"백업 파일 검사에 실패했습니다({result}). 원본 DB는 바꾸지 않았습니다.");
            }
        }

        foreach (var table in UserTables(source))
        {
            var expected = ScalarLong(source, $"SELECT COUNT(*) FROM \"{table}\"");
            var actual = TableExists(backup, table, null) ? ScalarLong(backup, $"SELECT COUNT(*) FROM \"{table}\"") : -1;
            if (actual != expected)
            {
                throw new InvalidOperationException(
                    $"백업 파일의 {table} 행 수가 원본과 다릅니다({actual:N0}/{expected:N0}). 원본 DB는 바꾸지 않았습니다.");
            }
        }
    }

    private static string BuildBackupPath(string databasePath)
    {
        var directory = Path.GetDirectoryName(databasePath);
        var name = Path.GetFileNameWithoutExtension(databasePath);
        var extension = Path.GetExtension(databasePath);
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        var path = Path.Combine(string.IsNullOrWhiteSpace(directory) ? "." : directory, $"{name}.backup-{stamp}{extension}");
        for (var suffix = 2; File.Exists(path); suffix++)
        {
            path = Path.Combine(string.IsNullOrWhiteSpace(directory) ? "." : directory,
                $"{name}.backup-{stamp}-{suffix.ToString(CultureInfo.InvariantCulture)}{extension}");
        }

        return path;
    }

    private static long DatabaseFileBytes(string databasePath)
    {
        return new[] { databasePath, databasePath + "-wal" }
            .Where(File.Exists)
            .Sum(path => new FileInfo(path).Length);
    }

    private static List<string> UserTables(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%' ORDER BY name";
        using var reader = command.ExecuteReader();
        var tables = new List<string>();
        while (reader.Read())
        {
            tables.Add(reader.GetString(0));
        }

        return tables;
    }

    private static HashSet<string> TableColumns(SqliteConnection connection, string table, SqliteTransaction? transaction)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"PRAGMA table_info(\"{table}\")";
        using var reader = command.ExecuteReader();
        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (reader.Read())
        {
            columns.Add(reader.GetString(1));
        }

        return columns;
    }

    private static bool IndexExists(SqliteConnection connection, string indexName, SqliteTransaction? transaction)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT 1 FROM sqlite_master WHERE type = 'index' AND name = $name LIMIT 1";
        command.Parameters.AddWithValue("$name", indexName);
        return command.ExecuteScalar() is not null;
    }

    private static void Execute(SqliteConnection connection, SqliteTransaction? transaction, string sql)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
