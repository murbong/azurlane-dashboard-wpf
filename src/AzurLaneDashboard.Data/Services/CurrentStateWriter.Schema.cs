using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace AzurLaneDashboard.Data.Services;

public sealed partial class CurrentStateWriter
{
    private void EnsureSchema()
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
                              CREATE TABLE IF NOT EXISTS current_ships (
                                  ship_uid TEXT PRIMARY KEY,
                                  ship_template_id INTEGER,
                                  custom_name TEXT,
                                  display_name TEXT,
                                  level INTEGER,
                                  exp INTEGER,
                                  limit_break INTEGER,
                                  enhance_level INTEGER,
                                  skin_id INTEGER,
                                  locked INTEGER,
                                  intimacy INTEGER,
                                  create_time INTEGER,
                                  energy INTEGER,
                                  max_level INTEGER,
                                  special_equipment_json TEXT,
                                  state_json TEXT,
                                  snapshot_rowid INTEGER,
                                  raw_json TEXT
                              );
                              CREATE TABLE IF NOT EXISTS current_ship_skills (
                                  ship_uid TEXT NOT NULL,
                                  slot_index INTEGER NOT NULL,
                                  skill_id INTEGER,
                                  skill_lv INTEGER,
                                  skill_exp INTEGER,
                                  raw_json TEXT,
                                  PRIMARY KEY (ship_uid, slot_index)
                              );
                              CREATE TABLE IF NOT EXISTS current_ship_equipment_slots (
                                  ship_uid TEXT NOT NULL,
                                  slot INTEGER NOT NULL,
                                  empty INTEGER,
                                  equipment_template_id INTEGER,
                                  enhance_level INTEGER,
                                  skin_id INTEGER,
                                  raw_json TEXT,
                                  PRIMARY KEY (ship_uid, slot)
                              );
                              CREATE TABLE IF NOT EXISTS current_ship_special_equipment (
                                  ship_uid TEXT PRIMARY KEY,
                                  spweapon_uid INTEGER,
                                  template_id INTEGER,
                                  enhance_level INTEGER,
                                  attr_1 INTEGER,
                                  attr_2 INTEGER,
                                  effect INTEGER,
                                  pt INTEGER,
                                  raw_json TEXT
                              );
                              CREATE TABLE IF NOT EXISTS current_spweapon_inventory (
                                  spweapon_uid INTEGER PRIMARY KEY,
                                  template_id INTEGER,
                                  enhance_level INTEGER,
                                  attr_1 INTEGER,
                                  attr_2 INTEGER,
                                  attr_temp_1 INTEGER,
                                  attr_temp_2 INTEGER,
                                  effect INTEGER,
                                  pt INTEGER,
                                  raw_json TEXT
                              );
                              CREATE TABLE IF NOT EXISTS current_equipment_skins (
                                  skin_id INTEGER PRIMARY KEY,
                                  count INTEGER,
                                  raw_json TEXT
                              );
                              CREATE TABLE IF NOT EXISTS current_resources (
                                  resource_type INTEGER PRIMARY KEY,
                                  label TEXT,
                                  num INTEGER,
                                  raw_json TEXT
                              );
                               CREATE TABLE IF NOT EXISTS current_commander_profile (
                                   key TEXT PRIMARY KEY,
                                   value_json TEXT NOT NULL
                               );
                               CREATE TABLE IF NOT EXISTS current_fleets (
                                   fleet_id INTEGER PRIMARY KEY,
                                   name TEXT,
                                   commander_summary TEXT,
                                   raw_json TEXT,
                                   updated_rowid INTEGER,
                                   updated_at TEXT
                               );
                               CREATE TABLE IF NOT EXISTS current_fleet_ships (
                                   fleet_id INTEGER NOT NULL,
                                   ship_uid TEXT NOT NULL,
                                   slot_index INTEGER NOT NULL,
                                   raw_json TEXT,
                                   PRIMARY KEY (fleet_id, slot_index)
                               );
                               CREATE TABLE IF NOT EXISTS current_fleet_commanders (
                                   fleet_id INTEGER NOT NULL,
                                   pos INTEGER NOT NULL,
                                   commander_id INTEGER NOT NULL,
                                   raw_json TEXT,
                                   PRIMARY KEY (fleet_id, pos)
                               );
                               CREATE TABLE IF NOT EXISTS current_system_statuses (
                                   system_key TEXT PRIMARY KEY,
                                  status TEXT,
                                  status_label TEXT,
                                  status_kind TEXT,
                                  detail TEXT,
                                  raw_json TEXT
                              );
                              CREATE TABLE IF NOT EXISTS current_battle_runs (
                                  row_index INTEGER PRIMARY KEY,
                                  stage_id INTEGER,
                                  stage_label TEXT,
                                  stage_detail TEXT,
                                  system_label TEXT,
                                  total_time INTEGER,
                                  ship_exp_total INTEGER,
                                  ship_exp_avg INTEGER,
                                  mvp_uid TEXT,
                                  mvp_name TEXT,
                                  top_exp_summary TEXT,
                                  raw_json TEXT
                              );
                              CREATE TABLE IF NOT EXISTS current_battle_ship_rates (
                                  ship_uid TEXT PRIMARY KEY,
                                  name TEXT,
                                  level INTEGER,
                                  sample_exp INTEGER,
                                  avg_exp INTEGER,
                                  last_exp INTEGER,
                                  mvp_count INTEGER,
                                  runs_to_120_avg INTEGER,
                                  mood_value INTEGER,
                                  raw_json TEXT
                              );
                              CREATE TABLE IF NOT EXISTS current_state_meta (
                                  id INTEGER PRIMARY KEY CHECK (id = 1),
                                  schema_version TEXT NOT NULL,
                                  mode TEXT NOT NULL,
                                  last_decoded_rowid INTEGER NOT NULL DEFAULT 0,
                                  updated_at TEXT NOT NULL,
                                  last_error TEXT NOT NULL DEFAULT ''
                              );
                              CREATE TABLE IF NOT EXISTS current_pending_requests (
                                  pending_key TEXT PRIMARY KEY,
                                  request_packet_id INTEGER NOT NULL,
                                  response_packet_id INTEGER NOT NULL,
                                  area TEXT NOT NULL,
                                  source TEXT,
                                  stream_id TEXT,
                                  packet_index INTEGER,
                                  decoded_rowid INTEGER,
                                  created_at TEXT NOT NULL,
                                  request_json TEXT NOT NULL
                              );
                              CREATE TABLE IF NOT EXISTS current_packet_apply_log (
                                  decoded_rowid INTEGER PRIMARY KEY,
                                  packet_id INTEGER,
                                  packet_name TEXT,
                                  direction TEXT,
                                  status TEXT NOT NULL,
                                  area TEXT NOT NULL,
                                  detail TEXT,
                                  applied_at TEXT NOT NULL
                              );
                              CREATE TABLE IF NOT EXISTS current_equipment_items (
                                  equipment_uid INTEGER PRIMARY KEY,
                                  equipment_template_id INTEGER,
                                  enhance_level INTEGER,
                                  count INTEGER,
                                  raw_json TEXT
                              );
                              CREATE TABLE IF NOT EXISTS current_items (
                                  item_id INTEGER PRIMARY KEY,
                                  count INTEGER,
                                  item_type TEXT,
                                  raw_json TEXT
                              );
                              CREATE TABLE IF NOT EXISTS current_academy_rooms (
                                  room_id INTEGER PRIMARY KEY,
                                  ship_uid TEXT,
                                  skill_pos INTEGER,
                                  start_time INTEGER,
                                  finish_time INTEGER,
                                  exp INTEGER,
                                  raw_json TEXT
                              );
                              CREATE TABLE IF NOT EXISTS current_research_queue (
                                  queue_order INTEGER PRIMARY KEY,
                                  tech_id INTEGER,
                                  refresh_id INTEGER,
                                  finish_time INTEGER,
                                  raw_json TEXT
                              );
                              CREATE TABLE IF NOT EXISTS current_fleet_tech_groups (
                                  group_id INTEGER PRIMARY KEY,
                                  effect_tech_id INTEGER,
                                  study_tech_id INTEGER,
                                  study_finish_time INTEGER,
                                  rewarded_tech INTEGER,
                                  raw_json TEXT,
                                  updated_rowid INTEGER,
                                  updated_at TEXT
                              );
                              CREATE TABLE IF NOT EXISTS current_fleet_tech_attr_settings (
                                  ship_type INTEGER NOT NULL,
                                  attr_type INTEGER NOT NULL,
                                  set_value INTEGER,
                                  raw_json TEXT,
                                  updated_rowid INTEGER,
                                  updated_at TEXT,
                                  PRIMARY KEY (ship_type, attr_type)
                              );
                              CREATE TABLE IF NOT EXISTS current_commissions (
                                  commission_id INTEGER PRIMARY KEY,
                                  finish_time INTEGER,
                                  over_time INTEGER,
                                  ship_id_list_json TEXT,
                                  raw_json TEXT
                              );
                              CREATE TABLE IF NOT EXISTS current_build_queue (
                                  slot INTEGER PRIMARY KEY,
                                  template_id INTEGER,
                                  build_id INTEGER,
                                  start_time INTEGER,
                                  finish_time INTEGER,
                                  status TEXT,
                                  raw_json TEXT
                              );
                              CREATE TABLE IF NOT EXISTS current_chapter_state (
                                  key TEXT PRIMARY KEY,
                                  value_json TEXT NOT NULL,
                                  updated_rowid INTEGER,
                                  updated_at TEXT NOT NULL
                              );
                               CREATE INDEX IF NOT EXISTS idx_current_equipment_items_template ON current_equipment_items(equipment_template_id);
                               CREATE INDEX IF NOT EXISTS idx_current_fleet_ships_ship ON current_fleet_ships(ship_uid);
                               CREATE INDEX IF NOT EXISTS idx_current_pending_response ON current_pending_requests(response_packet_id);
                              CREATE INDEX IF NOT EXISTS idx_current_apply_log_packet ON current_packet_apply_log(packet_id);
                              """;
        command.ExecuteNonQuery();
        using var meta = connection.CreateCommand();
        meta.CommandText = """
                           INSERT INTO current_state_meta (id, schema_version, mode, last_decoded_rowid, updated_at, last_error)
                           VALUES (1, $version, 'incremental', COALESCE((SELECT MAX(rowid) FROM decoded_packets), 0), $now, '')
                           ON CONFLICT(id) DO UPDATE SET schema_version = excluded.schema_version
                           """;
        meta.Parameters.AddWithValue("$version", SchemaVersion);
        meta.Parameters.AddWithValue("$now", NowText());
        meta.ExecuteNonQuery();
    }
}
