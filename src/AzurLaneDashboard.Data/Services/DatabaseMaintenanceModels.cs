namespace AzurLaneDashboard.Data.Services;

public sealed class DatabaseMaintenanceOptions
{
    public int KeepRecentPacketPayloadRows { get; init; } = 1000;
    public int KeepDecodeFailuresPerPacket { get; init; } = 3;
    public int KeepRecentDecodedRows { get; init; } = 0;
    public bool ClearPacketPayloads { get; init; } = true;
    public bool ClearDecodeFailurePayloads { get; init; } = true;
    public bool Vacuum { get; init; }
}

public sealed record DatabaseTableStat(string Name, long Rows, long TextBytes);

public sealed record DatabaseMaintenanceResult(
    long SizeBeforeBytes,
    long SizeAfterBytes,
    int PacketPayloadRowsDeleted,
    int DecodeFailureRowsDeleted,
    int DecodedRowsDeleted,
    IReadOnlyList<DatabaseTableStat> BeforeStats,
    IReadOnlyList<DatabaseTableStat> AfterStats);

public sealed record RetiredColumn(string Table, string Column);

public sealed record RetiredStoragePlan(
    IReadOnlyList<string> Tables,
    IReadOnlyList<RetiredColumn> Columns,
    IReadOnlyList<string> Indexes)
{
    public bool IsEmpty => Tables.Count == 0 && Columns.Count == 0 && Indexes.Count == 0;

    public string SummaryText => IsEmpty
        ? "정리할 항목 없음"
        : string.Join(" · ", new[]
        {
            Tables.Count > 0 ? $"테이블 {Tables.Count:N0}개" : "",
            Columns.Count > 0 ? $"빈 열 {Columns.Count:N0}개" : "",
            Indexes.Count > 0 ? $"중복 인덱스 {Indexes.Count:N0}개" : ""
        }.Where(part => part.Length > 0));
}

public sealed record RetiredStorageCleanupResult(
    string BackupPath,
    long SizeBeforeBytes,
    long SizeAfterBytes,
    RetiredStoragePlan Removed,
    bool Compacted);

public sealed class DatabaseMaintenanceFileReplaceException : IOException
{
    public DatabaseMaintenanceFileReplaceException(
        string databasePath,
        string compactedPath,
        string backupPath,
        Exception innerException)
        : base(
            $"DB 파일 교체에 실패했습니다. 다른 프로세스가 DB를 사용 중일 수 있습니다. 압축된 임시 DB: {compactedPath}",
            innerException)
    {
        DatabasePath = databasePath;
        CompactedPath = compactedPath;
        BackupPath = backupPath;
    }

    public string DatabasePath { get; }
    public string CompactedPath { get; }
    public string BackupPath { get; }
}
