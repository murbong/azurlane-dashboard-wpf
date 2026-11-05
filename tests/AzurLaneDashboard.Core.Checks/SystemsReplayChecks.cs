using AzurLaneDashboard.Data.Services;

/// <summary>Commissions and research the client changes itself after a response with only a result.</summary>
internal static class SystemsReplayChecks
{
    public static void Run()
    {
        Commissions();
        Research();
        Resources();
        Console.WriteLine("Commission, research and resource checks passed.");
    }

    private static void Commissions()
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        using var db = new PacketDatabase();
        Check(db.Apply("SC", 12001, """{"shiplist":[{"id":401,"template_id":102074,"level":50,"exp":0,"max_level":100,"equip_info_list":[]}]}""") == "Applied",
            "ships load");
        Check(db.Apply("SC", 13002, $$"""
                                    {"collection_list":[
                                      {"id":30203,"finish_time":0,"over_time":0,"ship_id_list":[]},
                                      {"id":30302,"finish_time":{{now + 600}},"over_time":0,"ship_id_list":[401]},
                                      {"id":10105,"finish_time":0,"over_time":{{now + 3600}},"ship_id_list":[]}],"max_team":4}
                                    """) == "Applied", "the login list loads");
        // SC 13011 adds one and expires another; the rest of the list stays.
        Check(db.Apply("SC", 13011, $$"""
                                    {"collection":[{"id":40101,"finish_time":0,"over_time":{{now + 3600}},"ship_id_list":[]},
                                                   {"id":10105,"finish_time":0,"over_time":{{now - 5}},"ship_id_list":[]}]}
                                    """) == "Applied", "13011 applies");
        Check(db.Column("SELECT commission_id FROM current_commissions ORDER BY commission_id")
                .SequenceEqual(new long[] { 30203, 30302, 40101 }),
            "13011 merges new commissions and drops expired ones instead of replacing the list");

        db.Apply("CS", 13003, """{"id":30203,"ship_id_list":[401]}""");
        Check(db.Apply("SC", 13004, """{"result":0}""") == "Applied", "a commission start applies");
        var started = new NativeSnapshotService().Load(db.Path).Commissions.Single(row => row.Id == "30203");
        Check(started.ShipNames.Length > 0 && started.FinishAt.Length > 0 && started.Remaining is "2시간 0분" or "1시간 59분",
            $"a started commission has its ships and finishes collect_time (2 h) later ({started.Remaining}, {started.FinishAt})");

        db.Apply("CS", 13007, """{"id":30203}""");
        Check(db.Apply("SC", 13008, """{"result":0}""") == "Applied", "giving up applies");
        Check(db.Scalar("SELECT COUNT(*) FROM current_commissions WHERE commission_id = 30203 AND finish_time = 0 AND ship_id_list_json = '[]'") == 1,
            "a commission given up is available again without ships");
    }

    private static void Research()
    {
        using var db = new PacketDatabase();
        // Login: two queued projects and 876 being researched in refresh group 2.
        db.Apply("SC", 63000, """
                              {"queue":[{"id":875,"time":100},{"id":873,"time":200}],
                               "refresh_list":[{"id":2,"technologys":[{"id":876,"time":300},{"id":852,"time":0}]}]}
                              """);
        List<long> Ids() => db.Column("SELECT tech_id FROM current_research_queue ORDER BY queue_order");
        Check(Ids().SequenceEqual(new long[] { 875, 873, 876 }), "the project being researched is listed after the queue");

        db.Apply("CS", 63013, """{"tech_id":876,"refresh_id":2}""");
        db.Apply("SC", 63014, """{"result":0,"refresh_list":[]}""");
        db.Apply("CS", 63001, """{"tech_id":841,"refresh_id":2}""");
        db.Apply("SC", 63002, """{"result":0,"time":400}""");
        db.Apply("CS", 63005, """{"tech_id":841,"refresh_id":2}""");
        Check(db.Apply("SC", 63006, """{"result":0}""") == "Applied", "stopping research applies");
        Check(Ids().SequenceEqual(new long[] { 875, 873, 876 }), "a stopped project leaves; the joined one stays queued");

        db.Apply("CS", 63001, """{"tech_id":875,"refresh_id":2}""");
        db.Apply("SC", 63002, """{"result":0,"time":500}""");
        db.Apply("CS", 63015, """{"id":0}""");
        Check(db.Apply("SC", 63016, """{"result":0,"drops":[{"common_list":[]},{"common_list":[]}]}""") == "Applied",
            "finishing the queue applies");
        Check(Ids().SequenceEqual(new long[] { 876, 875 }), "two drops take the first two queued projects, not the active one");

        db.Apply("CS", 63003, """{"tech_id":875,"refresh_id":2}""");
        db.Apply("SC", 63004, """{"result":0,"common_list":[],"refresh_list":[]}""");
        Check(Ids().SequenceEqual(new long[] { 876 }), "finishing the active 875 keeps other rows");

        // One drop arrives as an object in captures decoded before the repeated-field fix.
        db.Apply("CS", 63015, """{"id":0}""");
        db.Apply("SC", 63016, """{"result":0,"drops":{"common_list":[]}}""");
        Check(Ids().Count == 0, "a single drop still finishes one queued project");
    }

    // Resources read as the items they stand for: core ones first, empty event ones left out.
    private static void Resources()
    {
        using var db = new PacketDatabase();
        db.Apply("SC", 11004, """
                              {"resource_list":[{"type":452,"num":1},{"type":318,"num":0},{"type":14,"num":3},
                                                {"type":4,"num":5},{"type":1,"num":100},{"type":2,"num":0}]}
                              """);
        var rows = new NativeSnapshotService().Load(db.Path).Resources.ToList();
        Check(rows.Select(row => row.Type).SequenceEqual(new[] { 1, 2, 4, 14, 452 }),
            $"core resources lead in their order, oil stays at 0, an empty event resource is left out ({string.Join(",", rows.Select(row => row.Type))})");
        Check(rows[0].Label == "물자" && rows[0].IconUrl.EndsWith("/props/gold.png", StringComparison.Ordinal) && rows[0].Rarity == "N",
            $"gold is named and drawn from its item ({rows[0].Label}, {rows[0].IconUrl}, {rows[0].Rarity})");
        Check(rows[2].Label == "다이아(유료)" && rows[3].Label == "다이아(무료)" && rows[2].Rarity == "SR",
            "paid and free gems are told apart; item rarity 3 frames as SR");
        Check(rows[4].Rarity == "UR" && rows[4].IconUrl.Length == 0 && rows[4].Label.Length > 0,
            "a medal outside props keeps its name and rarity without an icon");
    }

    private static void Check(bool condition, string description)
    {
        if (!condition) throw new InvalidOperationException(description);
    }
}
