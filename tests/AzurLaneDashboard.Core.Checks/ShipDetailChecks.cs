using AzurLaneDashboard.Data.Services;

/// <summary>Skill EXP from lessons and META training, and the affinity and morale battles and time change.</summary>
internal static class ShipDetailChecks
{
    public static void Run()
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        using var db = new PacketDatabase();
        (long Level, long Exp) Skill(string ship, int skill) =>
            (db.Scalar($"SELECT skill_lv FROM current_ship_skills WHERE ship_uid = '{ship}' AND skill_id = {skill}"),
             db.Scalar($"SELECT skill_exp FROM current_ship_skills WHERE ship_uid = '{ship}' AND skill_id = {skill}"));

        db.Apply("SC", 12001, """
                              {"shiplist":[
                                {"id":601,"template_id":102074,"level":50,"exp":0,"max_level":100,"intimacy":9990,"energy":100,
                                 "equip_info_list":[],"skill_id_list":[{"skill_id":1001,"skill_lv":2,"skill_exp":150}]},
                                {"id":602,"template_id":102074,"level":50,"exp":0,"max_level":100,"intimacy":19995,"energy":50,"propose":1,
                                 "equip_info_list":[],"skill_id_list":[{"skill_id":2002,"skill_lv":9,"skill_exp":5700}]}]}
                              """);
        db.Apply("SC", 22001, $$"""
                                {"skill_class_list":[
                                  {"room_id":2,"ship_id":601,"skill_pos":1001,"exp":300,"start_time":{{now - 100}},"finish_time":{{now + 100}}},
                                  {"room_id":3,"ship_id":602,"skill_pos":2002,"exp":300,"start_time":{{now - 100}},"finish_time":{{now + 100}}}]}
                                """);

        db.Apply("CS", 22203, """{"room_id":2,"type":1}""");
        Check(db.Apply("SC", 22204, """{"result":0,"exp":500}""") == "Applied", "ending a lesson applies");
        Check(Skill("601", 1001) == (4, 50), $"lesson EXP levels the skill (2: 150 + 500 = level 4 with 50) {Skill("601", 1001)}");
        db.Apply("CS", 22014, """{"roomid":3}""");
        Check(db.Apply("SC", 22015, """{"result":0}""") == "Applied", "finishing a lesson early applies");
        Check(Skill("602", 2002) == (10, 0) && db.Scalar("SELECT COUNT(*) FROM current_academy_rooms") == 0,
            $"an early finish gives the room's EXP; the top level keeps none {Skill("602", 2002)}");

        Check(db.Apply("SC", 63316, """{"skill_info_list":{"ship_id":601,"skill_id":1001,"skill_level":7,"skill_exp":10}}""") == "Applied" &&
              Skill("601", 1001) == (7, 10), "a META skill push sets the level and EXP it states");

        db.Apply("SC", 19001, """{"lv":4,"food":1000,"ship_list":[{"id":602,"floor":2}],"next_timestamp":1}""");
        db.Apply("CS", 40003, """{"system":1,"data":1204010}""");
        db.Apply("SC", 40004, """
                              {"ship_exp_list":[{"ship_id":601,"exp":10,"intimacy":10018,"energy":2},
                                                {"ship_id":602,"exp":10,"intimacy":10010,"energy":6}]}
                              """);
        Check(db.Scalar("SELECT intimacy FROM current_ships WHERE ship_uid = '601'") == 10000 &&
              db.Scalar("SELECT intimacy FROM current_ships WHERE ship_uid = '602'") == 20000,
            "battle affinity stops at 100 unmarried and 200 married");
        Check(db.Scalar("SELECT energy FROM current_ships WHERE ship_uid = '601'") == 98 &&
              db.Scalar("SELECT energy FROM current_ships WHERE ship_uid = '602'") == 44, "a battle spends morale");

        // Three recovery ticks since the server's last one: +2 each up to 119 in port; married in the courtyard's
        // floor 2, +3 and +3 more each up to 150.
        db.Apply("SC", 12031, $$"""{"energy_auto_increase_time":{{now - 3 * 360}}}""");
        var ships = new NativeSnapshotService().Load(db.Path).Ships.ToDictionary(ship => ship.ShipUid);
        Check(ships["601"].MoodValue == 104 && ships["602"].MoodValue == 62,
            $"morale recovers with time ({ships["601"].MoodValue}, {ships["602"].MoodValue})");

        Console.WriteLine("Ship detail checks passed.");
    }

    private static void Check(bool condition, string description)
    {
        if (!condition) throw new InvalidOperationException(description);
    }
}
