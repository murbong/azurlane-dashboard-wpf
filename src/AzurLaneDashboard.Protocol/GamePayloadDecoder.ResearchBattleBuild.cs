using System.Text.Json.Nodes;

namespace AzurLaneDashboard.Protocol;

public sealed partial class GamePayloadDecoder
{
    private static JsonObject DecodeResearch(byte[] payload)
    {
        var result = new JsonObject { ["refresh_list"] = new JsonArray(), ["queue"] = new JsonArray() };
        foreach (var field in ProtobufReader.ReadFields(payload))
        {
            if (field.Number == 1 && field.WireType == ProtoWireType.LengthDelimited)
            {
                result.Array("refresh_list").Add(DecodeTechnologyRefresh(field.Bytes));
            }
            else if (field.Number == 2 && field.WireType == ProtoWireType.Varint)
            {
                result["refresh_flag"] = (long)field.Varint;
            }
            else if (field.Number == 3 && field.WireType == ProtoWireType.LengthDelimited)
            {
                result["catchup"] = DecodeTechnologyCatchup(field.Bytes);
            }
            else if (field.Number == 4 && field.WireType == ProtoWireType.LengthDelimited)
            {
                result.Array("queue").Add(DecodeTechnologyInfo(field.Bytes));
            }
        }

        return result;
    }

    private static JsonObject DecodeResearchFinish(byte[] payload)
    {
        var result = new JsonObject
        {
            ["common_list"] = new JsonArray(),
            ["refresh_list"] = new JsonArray(),
            ["drop_list"] = new JsonArray(),
            ["catchup_list"] = new JsonArray(),
            ["catchupact_list"] = new JsonArray()
        };
        foreach (var field in ProtobufReader.ReadFields(payload))
        {
            if (field.Number == 1 && field.WireType == ProtoWireType.Varint)
            {
                result["result"] = (long)field.Varint;
            }
            else if (field.WireType == ProtoWireType.LengthDelimited)
            {
                if (field.Number == 2) result.Array("common_list").Add(DecodeDropInfo(field.Bytes));
                else if (field.Number == 3) result.Array("refresh_list").Add(DecodeTechnologyRefresh(field.Bytes));
                else if (field.Number == 4) result.Array("drop_list").Add(DecodeDropInfo(field.Bytes));
                else if (field.Number == 5) result.Array("catchup_list").Add(DecodeDropInfo(field.Bytes));
                else if (field.Number == 6) result.Array("catchupact_list").Add(DecodeDropInfo(field.Bytes));
            }
        }

        return result;
    }

    private static JsonObject DecodeBattleStart(byte[] payload)
    {
        var result = new JsonObject
        {
            ["ship_id_list"] = new JsonArray(), ["data2"] = new JsonArray(), ["other_ship_id_list"] = new JsonArray()
        };
        foreach (var field in ProtobufReader.ReadFields(payload))
        {
            if (field.WireType == ProtoWireType.Varint)
            {
                if (field.Number == 1) result["system"] = (long)field.Varint;
                else if (field.Number == 2) result.Array("ship_id_list").Add((long)field.Varint);
                else if (field.Number == 3) result["data"] = (long)field.Varint;
                else if (field.Number == 4) result.Array("data2").Add((long)field.Varint);
            }
            else if (field.WireType == ProtoWireType.LengthDelimited)
            {
                if (field.Number == 2) AddPackedValues(result, "ship_id_list", field.Bytes);
                else if (field.Number == 4) AddPackedValues(result, "data2", field.Bytes);
                else if (field.Number == 5)
                    result.Array("other_ship_id_list").Add(Fields(field.Bytes,
                        new Dictionary<int, string> { [1] = "id", [2] = "pos" }));
            }
        }

        return result;
    }

    private static JsonObject DecodeBattleFinishRequest(byte[] payload)
    {
        var result = new JsonObject
        {
            ["statistics"] = new JsonArray(),
            ["kill_id_list"] = new JsonArray(),
            ["enemy_info"] = new JsonArray(),
            ["data2"] = new JsonArray(),
            ["commander_id_list"] = new JsonArray(),
            ["otherstatistics"] = new JsonArray()
        };
        var scalar = new Dictionary<int, string>
        {
            [1] = "system", [2] = "data", [3] = "key", [4] = "score", [7] = "total_time",
            [8] = "bot_percentage", [9] = "extra_param", [11] = "boss_hp",
            [16] = "auto_before", [17] = "auto_switch_time", [18] = "auto_after"
        };
        foreach (var field in ProtobufReader.ReadFields(payload))
        {
            if (field.WireType == ProtoWireType.Varint)
            {
                if (scalar.TryGetValue(field.Number, out var name))
                {
                    result[name] = (long)field.Varint;
                }
                else if (field.Number == 6) result.Array("kill_id_list").Add((long)field.Varint);
                else if (field.Number == 13) result.Array("data2").Add((long)field.Varint);
                else if (field.Number == 14) result.Array("commander_id_list").Add((long)field.Varint);
            }
            else if (field.WireType == ProtoWireType.LengthDelimited)
            {
                if (field.Number == 10) result["file_check"] = ProtobufReader.DecodeString(field.Bytes);
                else if (field.Number == 6) AddPackedValues(result, "kill_id_list", field.Bytes);
                else if (field.Number == 13) AddPackedValues(result, "data2", field.Bytes);
                else if (field.Number == 14) AddPackedValues(result, "commander_id_list", field.Bytes);
                else if (field.Number == 5) result.Array("statistics").Add(Fields(field.Bytes, BattleStatisticNames));
                else if (field.Number == 12) result.Array("enemy_info").Add(Fields(field.Bytes, EnemyInfoNames));
                else if (field.Number == 15)
                    result.Array("otherstatistics").Add(Fields(field.Bytes, BattleStatisticNames));
            }
        }

        return result;
    }

    // FinishStageCommand.GeneralPackage: damage_cause is the damage the ship dealt, damage_caused the damage it took,
    // hp_rest the HP it kept out of 10,000.
    private static readonly Dictionary<int, string> BattleStatisticNames = new()
    {
        [1] = "ship_id", [2] = "damage_cause", [3] = "damage_caused", [4] = "hp_rest", [5] = "max_damage_once",
        [6] = "ship_gear_score"
    };

    private static readonly Dictionary<int, string> EnemyInfoNames = new()
    {
        [1] = "enemy_id", [2] = "damage_taken", [3] = "total_hp"
    };

    private static JsonObject DecodeBattleResult(byte[] payload)
    {
        var result = new JsonObject
        {
            ["drop_info"] = new JsonArray(),
            ["extra_drop_info"] = new JsonArray(),
            ["ship_exp_list"] = new JsonArray(),
            ["commander_exp"] = new JsonArray(),
            ["hp_drop_info"] = new JsonArray()
        };
        foreach (var field in ProtobufReader.ReadFields(payload))
        {
            if (field.WireType == ProtoWireType.Varint)
            {
                if (field.Number == 1) result["result"] = (long)field.Varint;
                else if (field.Number == 4) result["player_exp"] = (long)field.Varint;
                else if (field.Number == 6) result["mvp"] = (long)field.Varint;
            }
            else if (field.WireType == ProtoWireType.LengthDelimited)
            {
                if (field.Number == 2) result.Array("drop_info").Add(DecodeDropInfo(field.Bytes));
                else if (field.Number == 3) result.Array("extra_drop_info").Add(DecodeDropInfo(field.Bytes));
                else if (field.Number == 5)
                    result.Array("ship_exp_list").Add(Fields(field.Bytes,
                        new Dictionary<int, string>
                            { [1] = "ship_id", [2] = "exp", [3] = "intimacy", [4] = "energy" }));
                else if (field.Number == 7)
                    result.Array("commander_exp").Add(new JsonObject
                        { ["raw"] = Convert.ToHexString(field.Bytes).ToLowerInvariant() });
                else if (field.Number == 8)
                    result.Array("hp_drop_info").Add(new JsonObject
                        { ["raw"] = Convert.ToHexString(field.Bytes).ToLowerInvariant() });
            }
        }

        return result;
    }

    private static JsonObject DecodeBuildResult(byte[] payload)
    {
        var result = new JsonObject { ["build_info"] = new JsonArray() };
        foreach (var field in ProtobufReader.ReadFields(payload))
        {
            if (field.Number == 1 && field.WireType == ProtoWireType.Varint)
            {
                result["result"] = (long)field.Varint;
            }
            else if (field.Number == 2 && field.WireType == ProtoWireType.LengthDelimited)
            {
                result.Array("build_info").Add(DecodeBuildInfo(field.Bytes));
            }
        }

        return result;
    }

    private static JsonObject DecodeBuildQueue(byte[] payload)
    {
        var result = new JsonObject { ["worklist_list"] = new JsonArray() };
        var scalar = new Dictionary<int, string>
            { [1] = "worklist_count", [3] = "draw_count_1", [4] = "draw_count_10", [5] = "exchange_count" };
        foreach (var field in ProtobufReader.ReadFields(payload))
        {
            if (field.WireType == ProtoWireType.Varint && scalar.TryGetValue(field.Number, out var name))
            {
                result[name] = (long)field.Varint;
            }
            else if (field.Number == 2 && field.WireType == ProtoWireType.LengthDelimited)
            {
                result.Array("worklist_list").Add(DecodeBuildInfo(field.Bytes));
            }
        }

        return result;
    }

    private static JsonObject DecodeBuildPositionRequest(byte[] payload)
    {
        var result = new JsonObject { ["pos_list"] = new JsonArray() };
        foreach (var field in ProtobufReader.ReadFields(payload))
        {
            if (field.Number == 1 && field.WireType == ProtoWireType.Varint)
            {
                result["type"] = (long)field.Varint;
            }
            else if (field.Number == 2)
            {
                AddPackedOrSingle(result, "pos_list", field);
            }
        }

        return result;
    }

    private static JsonObject DecodeBuildPositionResult(byte[] payload)
    {
        var result = new JsonObject { ["pos_list"] = new JsonArray() };
        foreach (var field in ProtobufReader.ReadFields(payload))
        {
            if (field.Number == 1 && field.WireType == ProtoWireType.Varint)
            {
                result["result"] = (long)field.Varint;
            }
            else if (field.Number == 2)
            {
                AddPackedOrSingle(result, "pos_list", field);
            }
        }

        return result;
    }

    private static JsonObject DecodeBuildFinish(byte[] payload)
    {
        var result = new JsonObject { ["info_list"] = new JsonArray() };
        foreach (var field in ProtobufReader.ReadFields(payload))
        {
            if (field.Number == 1 && field.WireType == ProtoWireType.LengthDelimited)
            {
                result.Array("info_list").Add(Fields(field.Bytes,
                    new Dictionary<int, string> { [1] = "pos", [2] = "template_id" }));
            }
        }

        return result;
    }

    private static JsonObject DecodeBuildInfo(byte[] raw) => Fields(raw,
        new Dictionary<int, string> { [1] = "time", [2] = "finish_time", [3] = "build_id" });

    private static JsonObject DecodeDropInfo(byte[] raw) =>
        Fields(raw, new Dictionary<int, string> { [1] = "type", [2] = "id", [3] = "number" });

    private static JsonObject DecodeTechnologyInfo(byte[] raw) =>
        Fields(raw, new Dictionary<int, string> { [1] = "id", [2] = "time" });

    private static JsonObject DecodeTechnologyRefresh(byte[] raw)
    {
        var result = new JsonObject { ["technologys"] = new JsonArray() };
        foreach (var field in ProtobufReader.ReadFields(raw))
        {
            if (field.WireType == ProtoWireType.Varint)
            {
                if (field.Number == 1) result["id"] = (long)field.Varint;
                else if (field.Number == 2) result["target"] = (long)field.Varint;
            }
            else if (field.Number == 3 && field.WireType == ProtoWireType.LengthDelimited)
            {
                result.Array("technologys").Add(DecodeTechnologyInfo(field.Bytes));
            }
        }

        return result;
    }

    private static JsonObject DecodeTechnologyCatchup(byte[] raw) =>
        Fields(raw, new Dictionary<int, string> { [1] = "version", [2] = "target" });
}