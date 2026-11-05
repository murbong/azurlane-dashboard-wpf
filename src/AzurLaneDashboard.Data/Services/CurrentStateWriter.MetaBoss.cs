using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace AzurLaneDashboard.Data.Services;

/// <summary>
/// META boss fights kept apart from decoded_packets, which only holds the latest 20,000 packets.
/// The META fleet itself lives only on the device (WorldBossProxy saves it to PlayerPrefs), so the sortie
/// packet is the one place it can be read: CS 40001 names the ships, CS 40003 reports what each did.
/// Gear is not in either packet, so the ships' gear is copied when the sortie is seen.
/// </summary>
public sealed partial class CurrentStateWriter
{
    /// <summary>SYSTEM_WORLD_BOSS in const.lua (51 is an Operation Siren map fight).</summary>
    public const int MetaBossSystem = 50;

    private const int MetaBossFightLimit = 300;

    private void EnsureMetaBossFightTable()
    {
        bool existed;
        using (var exists = connection.CreateCommand())
        {
            exists.CommandText =
                "SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = 'current_meta_boss_fights'";
            existed = exists.ExecuteScalar() is not null;
        }

        if (existed) return;
        using (var create = connection.CreateCommand())
        {
            create.CommandText = """
                                 CREATE TABLE current_meta_boss_fights (
                                     fight_rowid INTEGER PRIMARY KEY,
                                     finish_rowid INTEGER,
                                     boss_owner TEXT,
                                     ship_uids_json TEXT,
                                     gear_json TEXT,
                                     finish_json TEXT,
                                     started_at TEXT,
                                     finished_at TEXT
                                 );
                                 CREATE INDEX idx_current_meta_boss_fights_finish ON current_meta_boss_fights(finish_rowid);
                                 """;
            create.ExecuteNonQuery();
        }

        BackfillMetaBossFights();
    }

    /// <summary>
    /// Copies the META fights still in decoded_packets into the new table once, so fights captured before
    /// this table existed survive the packet limit. Their gear was not recorded and stays unknown.
    /// </summary>
    private void BackfillMetaBossFights()
    {
        using (var exists = connection.CreateCommand())
        {
            exists.CommandText = "SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = 'decoded_packets'";
            if (exists.ExecuteScalar() is null) return;
        }

        var hasApplyLog = false;
        using (var exists = connection.CreateCommand())
        {
            exists.CommandText =
                "SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = 'current_packet_apply_log'";
            hasApplyLog = exists.ExecuteScalar() is not null;
        }

        var packets = new List<(long RowId, int PacketId, string Json, string AppliedAt)>();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = hasApplyLog
                ? """
                  SELECT p.rowid, p.packet_id, p.json, COALESCE(l.applied_at, '')
                  FROM decoded_packets p
                  LEFT JOIN current_packet_apply_log l ON l.decoded_rowid = p.rowid
                  WHERE p.direction = 'CS' AND p.packet_id IN (40001, 40003) AND p.json LIKE '%"system":50%'
                  ORDER BY p.rowid
                  """
                : """
                  SELECT rowid, packet_id, json, ''
                  FROM decoded_packets
                  WHERE direction = 'CS' AND packet_id IN (40001, 40003) AND json LIKE '%"system":50%'
                  ORDER BY rowid
                  """;
            using var reader = command.ExecuteReader();
            while (reader.Read())
                packets.Add((reader.GetInt64(0), reader.GetInt32(1), reader.GetString(2), reader.GetString(3)));
        }

        if (packets.Count == 0) return;
        using var tx = connection.BeginTransaction();
        foreach (var (rowId, packetId, json, appliedAt) in packets)
        {
            JsonElement root;
            try
            {
                using var document = JsonDocument.Parse(json);
                root = document.RootElement.Clone();
            }
            catch (JsonException)
            {
                continue;
            }

            var envelope = new DecodedPacketEnvelope(rowId, "backfill", "", "CS", packetId, 0);
            RecordMetaBossFight(tx, envelope, root, copyGear: false, at: appliedAt.Length > 0 ? appliedAt : null);
        }

        tx.Commit();
    }

    /// <summary>Opens a fight on CS 40001 and closes it with CS 40003; other systems are left alone.</summary>
    private void RecordMetaBossFight(SqliteTransaction tx, DecodedPacketEnvelope packet, JsonElement json,
        bool copyGear = true, string? at = null)
    {
        if (packet.Direction != "CS" || Int(json, "system") != MetaBossSystem) return;
        var owner = Text(json, "data");
        var time = at ?? NowText();
        if (packet.PacketId == 40001)
        {
            var uids = Items(json, "ship_id_list").Select(ValueText).Where(uid => uid.Length > 0 && uid != "0").ToList();
            Execute(
                tx,
                """
                INSERT INTO current_meta_boss_fights (fight_rowid, boss_owner, ship_uids_json, gear_json, started_at)
                VALUES ($rowid, $owner, $ships, $gear, $time)
                ON CONFLICT(fight_rowid) DO UPDATE SET
                    boss_owner = excluded.boss_owner,
                    ship_uids_json = excluded.ship_uids_json,
                    gear_json = COALESCE(current_meta_boss_fights.gear_json, excluded.gear_json)
                """,
                ("$rowid", packet.DecodedRowId),
                ("$owner", owner),
                ("$ships", Json(uids)),
                ("$gear", copyGear ? Json(ReadShipGear(tx, uids)) : null),
                ("$time", time));
            return;
        }

        if (packet.PacketId != 40003) return;
        var statUids = Items(json, "statistics").Select(item => Text(item, "ship_id"))
            .Where(uid => uid.Length > 0 && uid != "0").ToList();
        var fightRowId = FindMetaBossFight(tx, packet.DecodedRowId, owner, statUids);
        if (fightRowId is null)
        {
            // The sortie was not captured: the finish opens and closes the fight on its own.
            fightRowId = packet.DecodedRowId;
            Execute(
                tx,
                """
                INSERT INTO current_meta_boss_fights (fight_rowid, boss_owner, ship_uids_json, gear_json, started_at)
                VALUES ($rowid, $owner, '[]', $gear, $time)
                ON CONFLICT(fight_rowid) DO NOTHING
                """,
                ("$rowid", fightRowId),
                ("$owner", owner),
                ("$gear", copyGear && statUids.Count > 0 ? Json(ReadShipGear(tx, statUids)) : null),
                ("$time", time));
        }

        Execute(
            tx,
            """
            UPDATE current_meta_boss_fights
            SET finish_rowid = $finish, finish_json = $json, finished_at = $time
            WHERE fight_rowid = $rowid
            """,
            ("$finish", packet.DecodedRowId),
            ("$json", Json(json)),
            ("$time", time),
            ("$rowid", fightRowId));
        Execute(
            tx,
            """
            DELETE FROM current_meta_boss_fights
            WHERE fight_rowid NOT IN (
                SELECT fight_rowid FROM current_meta_boss_fights ORDER BY fight_rowid DESC LIMIT $limit
            )
            """,
            ("$limit", MetaBossFightLimit));
    }

    // The fight this finish closes: one already closed by it (a replay), else the latest open sortie on the same
    // boss that sent every ship the finish names.
    private long? FindMetaBossFight(SqliteTransaction tx, long finishRowId, string owner, IReadOnlyList<string> statUids)
    {
        using (var closed = connection.CreateCommand())
        {
            closed.Transaction = tx;
            closed.CommandText = "SELECT fight_rowid FROM current_meta_boss_fights WHERE finish_rowid = $finish LIMIT 1";
            closed.Parameters.AddWithValue("$finish", finishRowId);
            if (closed.ExecuteScalar() is long replayed) return replayed;
        }

        using var open = connection.CreateCommand();
        open.Transaction = tx;
        open.CommandText = """
                           SELECT fight_rowid, ship_uids_json FROM current_meta_boss_fights
                           WHERE finish_rowid IS NULL AND boss_owner = $owner AND fight_rowid < $finish
                           ORDER BY fight_rowid DESC LIMIT 1
                           """;
        open.Parameters.AddWithValue("$owner", owner);
        open.Parameters.AddWithValue("$finish", finishRowId);
        using var reader = open.ExecuteReader();
        if (!reader.Read()) return null;
        var rowId = reader.GetInt64(0);
        if (statUids.Count == 0 || reader.IsDBNull(1)) return rowId;
        var sortie = (JsonSerializer.Deserialize<List<string>>(reader.GetString(1)) ?? []).ToHashSet(StringComparer.Ordinal);
        return sortie.Count == 0 || statUids.All(sortie.Contains) ? rowId : null;
    }

    // Slot → equipment template id for each ship, as equipped now; 0 marks an empty slot.
    private Dictionary<string, Dictionary<string, int>> ReadShipGear(SqliteTransaction tx, IReadOnlyList<string> uids)
    {
        var gear = uids.Distinct(StringComparer.Ordinal)
            .ToDictionary(uid => uid, _ => new Dictionary<string, int>(), StringComparer.Ordinal);
        if (gear.Count == 0) return gear;
        using var command = connection.CreateCommand();
        command.Transaction = tx;
        var names = gear.Keys.Select((_, index) => $"$u{index.ToString(CultureInfo.InvariantCulture)}").ToList();
        command.CommandText =
            $"SELECT ship_uid, slot, equipment_template_id FROM current_ship_equipment_slots WHERE ship_uid IN ({string.Join(", ", names)})";
        foreach (var (uid, index) in gear.Keys.Select((uid, index) => (uid, index)))
            command.Parameters.AddWithValue(names[index], uid);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var uid = reader.GetString(0);
            var slot = reader.IsDBNull(1) ? 0 : reader.GetInt32(1);
            if (slot <= 0 || !gear.TryGetValue(uid, out var slots)) continue;
            slots[slot.ToString(CultureInfo.InvariantCulture)] = reader.IsDBNull(2) ? 0 : reader.GetInt32(2);
        }

        return gear;
    }
}
