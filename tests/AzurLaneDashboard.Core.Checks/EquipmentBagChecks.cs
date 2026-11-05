/// <summary>The equipment bag and equipment skins between logins, as EquipmentProxy moves them.</summary>
internal static class EquipmentBagChecks
{
    public static void Run()
    {
        using var db = new PacketDatabase();
        long Bag(int id) => db.Scalar($"SELECT COALESCE((SELECT count FROM current_equipment_items WHERE equipment_uid = {id}), 0)");
        long Skins(int id) => db.Scalar($"SELECT COALESCE((SELECT count FROM current_equipment_skins WHERE skin_id = {id}), 0)");
        long Slot(int slot) => db.Scalar($"SELECT equipment_template_id FROM current_ship_equipment_slots WHERE ship_uid = '501' AND slot = {slot}");
        void Exchange(int request, string requestJson, int response, string responseJson)
        {
            db.Apply("CS", request, requestJson);
            Check(db.Apply("SC", response, responseJson) == "Applied", $"SC_{response} applies");
        }

        db.Apply("SC", 12001, """
                              {"shiplist":[{"id":501,"template_id":102074,"level":50,"exp":0,"max_level":100,
                                "equip_info_list":[{"id":11200,"skin_id":0},{"id":0,"skin_id":0}]}]}
                              """);
        db.Apply("SC", 14001, """{"equip_list":[{"id":11200,"count":2},{"id":5200,"count":1}],"spweapon_list":[{"id":1,"template_id":100}]}""");
        db.Execute("INSERT INTO current_equipment_skins (skin_id, count, raw_json) VALUES (7001, 2, '{}'), (7002, 1, '{}')");

        Exchange(12006, """{"type":0,"equip_id":0,"ship_id":501,"pos":1}""", 12007, """{"result":0}""");
        Check(Bag(11200) == 3 && Slot(1) == 0, "unequipping puts the equipment back in the bag");
        Exchange(12006, """{"type":0,"equip_id":11200,"ship_id":501,"pos":2}""", 12007, """{"result":0}""");
        Check(Bag(11200) == 2 && Slot(2) == 11200, "equipping takes it out of the bag");

        Exchange(12036, """{"ship_id":501,"equip_skin_id":7001,"pos":2}""", 12037, """{"result":0}""");
        Exchange(12036, """{"ship_id":501,"equip_skin_id":7002,"pos":2}""", 12037, """{"result":0}""");
        Check(Skins(7001) == 2 && Skins(7002) == 0, "a skin swap returns the old skin and uses the new one");

        Exchange(14002, """{"ship_id":501,"pos":2,"lv":3}""", 14003, """{"result":0}""");
        Check(Slot(2) == 11203 && db.Scalar("SELECT enhance_level FROM current_ship_equipment_slots WHERE ship_uid = '501' AND slot = 2") == 0,
            "lv is the levels gained: +0 enhanced three times is the template three further on");
        Exchange(14004, """{"equip_id":11200,"lv":2}""", 14005, """{"result":0}""");
        Check(Bag(11200) == 1 && Bag(11202) == 1, "enhancing in the bag swaps one equipment for its enhanced template");

        Exchange(14006, """{"id":1,"num":2}""", 14007, """{"result":0}""");
        Check(Bag(5200) == 3, "composing adds the recipe's equipment (compose_data_template 1 makes 5200)");
        Exchange(14010, """{"equip_id":11202}""", 14011, """{"result":0}""");
        Check(Bag(11202) == 0 && Bag(11200) == 2, "reverting turns the equipment back into its +0 base");

        Exchange(14015, """{"equip_id":11200,"upgrade_id":15221}""", 14016, """{"result":0}""");
        Check(Bag(11200) == 1 && Bag(15220) == 1, "a transform yields the formula's target (formula 15221 makes 15220)");
        Exchange(14013, """{"ship_id":501,"pos":2,"upgrade_id":15221}""", 14014, """{"result":0}""");
        Check(Slot(2) == 15220, "a transform on a ship puts the target in the slot");

        Exchange(13005, """{"id":30101}""", 13006,
            """{"result":0,"exp":0,"drop_list":[{"type":3,"id":9999,"number":2},{"type":9,"id":7003,"number":1},{"type":2,"id":1,"number":5}]}""");
        Check(Bag(9999) == 2 && Skins(7003) == 1, "equipment and equipment skin rewards go to the bag");
        Check(db.Apply("SC", 20012, """{"id_list":[1],"award_list":{"type":3,"id":9998,"number":1}}""") == "Applied" && Bag(9998) == 1,
            "a mission reward with one equipment (a single object) counts too and reloads the dashboard");

        db.Apply("SC", 14200, """{"spweapon_list":[{"id":2,"template_id":200}]}""");
        Check(db.Scalar("SELECT COUNT(*) FROM current_spweapon_inventory") == 2, "SC 14200 adds special equipment without wiping the rest");

        Console.WriteLine("Equipment bag checks passed.");
    }

    private static void Check(bool condition, string description)
    {
        if (!condition) throw new InvalidOperationException(description);
    }
}
