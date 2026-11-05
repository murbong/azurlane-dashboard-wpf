using System.Text.Json;
using AzurLaneDashboard.Data.Services;
using Microsoft.Data.Sqlite;

/// <summary>A temp database fed packet by packet through the current-state writer, as capture does.</summary>
internal sealed class PacketDatabase : IDisposable
{
    private readonly SqliteConnection connection;
    private readonly CurrentStateWriter writer;
    private int index;

    public PacketDatabase()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"azurlane-packet-check-{Guid.NewGuid():N}.sqlite");
        connection = new SqliteConnection($"Data Source={Path}");
        connection.Open();
        Execute("""
                CREATE TABLE decoded_packets (
                    source TEXT, stream_id TEXT, direction TEXT, packet_id INTEGER,
                    packet_name TEXT, "index" INTEGER, json TEXT
                );
                """);
        writer = new CurrentStateWriter(connection);
    }

    public string Path { get; }

    /// <summary>Stores and applies one packet; returns the writer's status (Applied, Ignored, Deferred, Failed).</summary>
    public string Apply(string direction, int packetId, string json)
    {
        using (var insert = connection.CreateCommand())
        {
            insert.CommandText = """
                                 INSERT INTO decoded_packets (source, stream_id, direction, packet_id, packet_name, "index", json)
                                 VALUES ('check', '1', $direction, $packet, $name, 0, $json)
                                 """;
            insert.Parameters.AddWithValue("$direction", direction);
            insert.Parameters.AddWithValue("$packet", packetId);
            insert.Parameters.AddWithValue("$name", $"{direction}_{packetId}");
            insert.Parameters.AddWithValue("$json", json);
            insert.ExecuteNonQuery();
        }

        using var document = JsonDocument.Parse(json);
        using var transaction = connection.BeginTransaction();
        var result = writer.ApplyDecodedPacket(transaction,
            new DecodedPacketEnvelope(++index, "check", "1", direction, packetId, 0), document.RootElement);
        transaction.Commit();
        return result.Status;
    }

    public void Execute(string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    public long Scalar(string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(command.ExecuteScalar() ?? -1L);
    }

    public string Text(string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToString(command.ExecuteScalar()) ?? "";
    }

    public List<long> Column(string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        using var reader = command.ExecuteReader();
        var values = new List<long>();
        while (reader.Read()) values.Add(reader.GetInt64(0));
        return values;
    }

    public void Dispose()
    {
        connection.Dispose();
        SqliteConnection.ClearAllPools();
        try { File.Delete(Path); } catch (IOException) { }
    }
}
