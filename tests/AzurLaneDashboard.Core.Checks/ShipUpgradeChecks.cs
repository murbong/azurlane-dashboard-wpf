using System.Text.Json;

/// <summary>Limit breaks, META breakouts and strengthening, which the server answers with a result only.</summary>
internal static class ShipUpgradeChecks
{
    public static void Run()
    {
        using var db = new PacketDatabase();
        List<long> Skills(string ship) => db.Column($"SELECT skill_id FROM current_ship_skills WHERE ship_uid = '{ship}' ORDER BY slot_index");
        long Value(string sql) => db.Scalar(sql);

        db.Apply("SC", 12001, """
                              {"shiplist":[
                                {"id":701,"template_id":102071,"level":70,"exp":0,"max_level":70,"equip_info_list":[],
                                 "skill_id_list":[{"skill_id":10080,"skill_lv":5,"skill_exp":0},{"skill_id":4091,"skill_lv":3,"skill_exp":0}]},
                                {"id":702,"template_id":101021,"level":1,"exp":0,"max_level":70,"equip_info_list":[{"id":11200,"skin_id":0}]},
                                {"id":703,"template_id":9701011,"level":100,"exp":0,"max_level":100,"equip_info_list":[]},
                                {"id":704,"template_id":102074,"level":100,"exp":0,"max_level":100,"equip_info_list":[],
                                 "strength_list":[{"id":2,"exp":700}]},
                                {"id":705,"template_id":102071,"level":1,"exp":0,"max_level":70,"equip_info_list":[]},
                                {"id":706,"template_id":101021,"level":1,"exp":0,"max_level":70,"equip_info_list":[]}]}
                              """);
        db.Apply("SC", 14001, """{"equip_list":[{"id":11200,"count":1}],"spweapon_list":[]}""");

        // Limit break: 102071 → 102072 gains skill 20131 and the template's max level 80; the material leaves
        // and its equipment returns.
        db.Apply("CS", 12027, """{"ship_id":701,"material_id_list":[702]}""");
        Check(db.Apply("SC", 12028, """{"result":0}""") == "Applied", "a limit break applies");
        Check(Value("SELECT ship_template_id FROM current_ships WHERE ship_uid = '701'") == 102072 &&
              Value("SELECT max_level FROM current_ships WHERE ship_uid = '701'") == 80 &&
              Value("SELECT limit_break FROM current_ships WHERE ship_uid = '701'") == 1,
            "the ship moves to the next template, its limit break and max level");
        Check(Skills("701").SequenceEqual(new long[] { 10080, 4091, 20131 }) &&
              Value("SELECT skill_lv FROM current_ship_skills WHERE ship_uid = '701' AND skill_id = 10080") == 5,
            $"the new template's skill joins at level 1 and the others keep theirs ({string.Join(",", Skills("701"))})");
        Check(Value("SELECT COUNT(*) FROM current_ships WHERE ship_uid = '702'") == 0 &&
              Value("SELECT count FROM current_equipment_items WHERE equipment_uid = 11200") == 2,
            "the material ship is used up and its equipment goes back to the bag");

        db.Apply("CS", 63303, """{"ship_id":703}""");
        Check(db.Apply("SC", 63304, """{"result":0}""") == "Applied", "a META breakout applies");
        Check(Value("SELECT ship_template_id FROM current_ships WHERE ship_uid = '703'") == 9701012 &&
              Skills("703").SequenceEqual(new long[] { 800601 }),
            "a META breakout follows ship_meta_breakout and adds the new template's skill");

        // Strengthen 704 (102074, Lv.100) with 705 (same group, double) and 706 (another group):
        // cannon 700 + 25 stops at 24 * 30 = 720; torpedo 37; reload 37; air has no durability.
        db.Apply("CS", 12017, """{"ship_id":704,"material_id_list":[705,706]}""");
        Check(db.Apply("SC", 12018, """{"result":0}""") == "Applied", "strengthening applies");
        using var raw = JsonDocument.Parse(db.Text("SELECT raw_json FROM current_ships WHERE ship_uid = '704'"));
        var strength = raw.RootElement.GetProperty("strength_list").EnumerateArray()
            .ToDictionary(item => item.GetProperty("id").GetInt32(), item => item.GetProperty("exp").GetInt32());
        Check(strength.Count == 3 && strength[2] == 720 && strength[3] == 37 && strength[6] == 37,
            $"strengthen EXP follows ship_data_strengthen and its cap ({string.Join(", ", strength.Select(pair => $"{pair.Key}={pair.Value}"))})");
        Check(Value("SELECT COUNT(*) FROM current_ships WHERE ship_uid IN ('705', '706')") == 0, "strengthen materials are used up");

        Console.WriteLine("Ship upgrade checks passed.");
    }

    private static void Check(bool condition, string description)
    {
        if (!condition) throw new InvalidOperationException(description);
    }
}
