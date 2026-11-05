using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace AzurLaneDashboard.Data.Services;

/// <summary>
/// Ship details the client changes itself after a response: skill EXP from the tactical class and META
/// training, and the affinity and morale a battle costs or gives (FinishStageCommand.addShipsExp).
/// </summary>
public sealed partial class CurrentStateWriter
{
    // skill_need_exp: EXP for each skill level 1-9; level 10 is the top.
    private static readonly int[] SkillNeedExp = [100, 200, 400, 800, 1400, 2200, 3200, 4400, 5800];
    private const int SkillMaxLevel = 10;
    // intimacy_template's top bound; Ship.setIntimacy stops at 10000 until the ship is married.
    private const int MaxIntimacy = 20000;
    private const int UnmarriedMaxIntimacy = 10000;

    /// <summary>Ship.addSkillExp: levels the skill while the EXP covers it; the top level keeps no EXP.</summary>
    private void AddSkillExp(SqliteTransaction tx, string shipUid, int skillId, int gained)
    {
        if (string.IsNullOrWhiteSpace(shipUid) || skillId <= 0 || gained <= 0) return;
        int level, exp;
        using (var command = connection.CreateCommand())
        {
            command.Transaction = tx;
            command.CommandText = """
                                  SELECT COALESCE(skill_lv, 1), COALESCE(skill_exp, 0) FROM current_ship_skills
                                  WHERE ship_uid = $ship AND skill_id = $skill LIMIT 1
                                  """;
            command.Parameters.AddWithValue("$ship", shipUid);
            command.Parameters.AddWithValue("$skill", skillId);
            using var reader = command.ExecuteReader();
            if (!reader.Read()) return;
            level = Math.Max(1, reader.GetInt32(0));
            exp = reader.GetInt32(1);
        }

        if (level >= SkillMaxLevel) return;
        exp += gained;
        while (level < SkillMaxLevel && exp >= SkillNeedExp[level - 1])
        {
            exp -= SkillNeedExp[level - 1];
            level++;
        }

        if (level >= SkillMaxLevel) exp = 0;
        Execute(tx,
            "UPDATE current_ship_skills SET skill_lv = $level, skill_exp = $exp WHERE ship_uid = $ship AND skill_id = $skill",
            ("$level", level), ("$exp", exp), ("$ship", shipUid), ("$skill", skillId));
    }

    /// <summary>
    /// CancelLearnTacticsCommand (SC 22204, EXP in the response) and QuickFinsihLearnTacticsCommand (SC 22015, the
    /// room's EXP): the lesson's skill (skill_pos holds the skill id) gains the EXP, then the room empties.
    /// </summary>
    private void FinishLesson(SqliteTransaction tx, int roomId, int? responseExp)
    {
        string shipUid = "";
        int skillId = 0, roomExp = 0;
        using (var command = connection.CreateCommand())
        {
            command.Transaction = tx;
            command.CommandText = "SELECT ship_uid, skill_pos, exp FROM current_academy_rooms WHERE room_id = $room";
            command.Parameters.AddWithValue("$room", roomId);
            using var reader = command.ExecuteReader();
            if (reader.Read())
            {
                shipUid = reader.IsDBNull(0) ? "" : reader.GetString(0);
                skillId = reader.IsDBNull(1) ? 0 : reader.GetInt32(1);
                roomExp = reader.IsDBNull(2) ? 0 : reader.GetInt32(2);
            }
        }

        AddSkillExp(tx, shipUid, skillId, responseExp ?? roomExp);
        Execute(tx, "DELETE FROM current_academy_rooms WHERE room_id = $room", ("$room", roomId));
    }

    /// <summary>MetaCharacterProxy on SC 63316: META skills' level and EXP as the server counts them.</summary>
    private int ApplyMetaSkills(SqliteTransaction tx, JsonElement json)
    {
        var count = 0;
        foreach (var item in Items(json, "skill_info_list"))
        {
            Execute(tx,
                "UPDATE current_ship_skills SET skill_lv = $level, skill_exp = $exp WHERE ship_uid = $ship AND skill_id = $skill",
                ("$level", Int(item, "skill_level")), ("$exp", Int(item, "skill_exp")),
                ("$ship", Text(item, "ship_id")), ("$skill", Int(item, "skill_id")));
            count++;
        }

        return count;
    }

    /// <summary>
    /// FinishStageCommand.addShipsExp: intimacy arrives as 10000 + the gain, energy as the morale spent. Morale
    /// recovery over time is added when the snapshot loads.
    /// </summary>
    private void ApplyBattleAffinityAndMorale(SqliteTransaction tx, JsonElement item)
    {
        var uid = Text(item, "ship_id");
        if (string.IsNullOrWhiteSpace(uid)) return;
        var intimacyGain = Int(item, "intimacy") is > 0 and var intimacy ? intimacy - 10000 : 0;
        Execute(
            tx,
            """
            UPDATE current_ships
            SET intimacy = MIN(MAX(COALESCE(intimacy, 0) + $gain, 0),
                    CASE WHEN COALESCE(json_extract(raw_json, '$.propose'), 0) != 0 THEN $max ELSE $unmarried END),
                energy = MAX(COALESCE(energy, 0) - $spent, 0)
            WHERE ship_uid = $ship
            """,
            ("$gain", intimacyGain), ("$max", MaxIntimacy), ("$unmarried", UnmarriedMaxIntimacy),
            ("$spent", Math.Max(0, Int(item, "energy"))), ("$ship", uid));
    }
}
