namespace AzurLaneDashboard.Data.Services;

/// <summary>
/// Storage that older builds created and the current build neither writes nor reads.
/// Existing databases keep it until the user runs the cleanup from the diagnostics page,
/// which backs the database up first (see <see cref="DatabaseMaintenanceService.CleanupRetiredStorage"/>).
/// </summary>
public static class RetiredStorage
{
    /// <summary>
    /// json_store held copies of maps/*.json, which are now read from the maps folder.
    /// ships / equipment / equipment_items were text copies of the inventory superseded by the current_* tables.
    /// current_equipment_inventory was an aggregate of current_equipment_items, now grouped when read.
    /// current_battle_sessions was created but never written.
    /// </summary>
    public static IReadOnlyList<string> Tables { get; } =
    [
        "json_store",
        "ships",
        "equipment",
        "equipment_items",
        "current_equipment_inventory",
        "current_battle_sessions"
    ];

    /// <summary>Each is the leading prefix of a wider index on the same table, which serves the same lookups.</summary>
    public static IReadOnlyList<string> Indexes { get; } =
    [
        "idx_decoded_packets_id",
        "idx_decoded_packets_lookup",
        "idx_packet_decode_failures_id"
    ];

    /// <summary>
    /// Columns the writer only ever filled with '' or 0. Names, rarities, types and icons come from the maps
    /// when a snapshot is built, so storing them per row only went stale.
    /// </summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> Columns { get; } =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            ["current_ships"] =
            [
                "name", "rarity", "rarity_tier", "rarity_score", "ship_type_id", "ship_type_label",
                "ship_type_group", "nation_id", "nation_label", "icon_skin_id", "skin_name", "commander_level",
                "empty_slots", "skill_summary", "skill_gap", "equipped_summary", "fleet_tech_id",
                "fleet_tech_get_bonus", "fleet_tech_level_bonus", "fleet_tech_get_unlocked",
                "fleet_tech_level_unlocked", "fleet_tech_level_pending", "is_bulin"
            ],
            ["current_ship_equipment_slots"] =
            [
                "name", "rarity", "rarity_tier", "rarity_score", "equipment_type_id", "equipment_type_group",
                "equipment_type_label", "equipment_skin_json"
            ],
            ["current_ship_special_equipment"] = ["name", "rarity", "rarity_tier", "rarity_score"],
            ["current_spweapon_inventory"] = ["name", "rarity", "rarity_tier", "rarity_score", "icon_id", "icon_url"],
            ["current_equipment_skins"] =
            [
                "name", "rarity", "rarity_tier", "rarity_score", "theme_id", "icon_id", "icon_url", "equip_type_json"
            ],
            ["current_equipment_items"] = ["name", "rarity", "rarity_tier", "rarity_score"]
        };
}
