namespace AzurLaneDashboard.Data.Services;

public sealed partial class LivePacketStore
{
    private void EnsureSchema()
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
                              CREATE TABLE IF NOT EXISTS meta (
                                  key TEXT PRIMARY KEY,
                                  value TEXT
                              );
                              CREATE TABLE IF NOT EXISTS decoded_packets (
                                  source TEXT,
                                  stream_id TEXT,
                                  direction TEXT,
                                  packet_id INTEGER,
                                  packet_name TEXT,
                                  "index" INTEGER,
                                  json TEXT
                              );
                              CREATE TABLE IF NOT EXISTS packet_payloads (
                                  source TEXT,
                                  stream_id TEXT,
                                  direction TEXT,
                                  packet_id INTEGER,
                                  packet_name TEXT,
                                  "index" INTEGER,
                                  payload_hex TEXT,
                                  seen_at TEXT
                              );
                              CREATE TABLE IF NOT EXISTS packet_decode_failures (
                                  source TEXT,
                                  stream_id TEXT,
                                  direction TEXT,
                                  packet_id INTEGER,
                                  packet_name TEXT,
                                  "index" INTEGER,
                                  reason TEXT,
                                  error TEXT,
                                  payload_hex TEXT,
                                  seen_at TEXT
                              );
                              CREATE INDEX IF NOT EXISTS idx_packet_payloads_id ON packet_payloads(packet_id);
                              CREATE INDEX IF NOT EXISTS idx_decoded_packets_battle ON decoded_packets(packet_id, direction, source);
                              CREATE INDEX IF NOT EXISTS idx_packet_decode_failures_retention
                                  ON packet_decode_failures(packet_id, packet_name, reason);
                              """;
        command.ExecuteNonQuery();
    }

    private void ConfigureConnection()
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
                              PRAGMA busy_timeout=5000;
                              PRAGMA journal_mode=WAL;
                              PRAGMA synchronous=NORMAL;
                              PRAGMA temp_store=MEMORY;
                              PRAGMA cache_size=-32768;
                              PRAGMA wal_autocheckpoint=1000;
                              PRAGMA journal_size_limit=67108864;
                              """;
        command.ExecuteNonQuery();
    }
}
