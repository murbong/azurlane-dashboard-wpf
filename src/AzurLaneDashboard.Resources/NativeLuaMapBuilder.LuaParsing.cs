using System.Globalization;
using System.Net.Http;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AzurLaneDashboard.Resources;

internal sealed partial class NativeLuaMapBuilder
{
    private static List<Dictionary<string, object?>> LoadLuaRecords(
        string path,
        string tableName,
        string[] scalarFields,
        string[]? arrayFields = null)
    {
        var records = new List<Dictionary<string, object?>>();
        foreach (var (id, block) in LoadLuaBlocks(path, tableName))
        {
            var record = ParseLuaRecordBlock(block, scalarFields, arrayFields);
            record.TryAdd("id", id);
            records.Add(record);
        }

        return records;
    }

    /// <summary>Each record's id with its whole Lua table text, for loaders that need nested tables.</summary>
    private static List<(int Id, string Block)> LoadLuaBlocks(string path, string tableName)
    {
        if (!File.Exists(path))
        {
            return [];
        }

        var text = File.ReadAllText(path);
        var pattern = new Regex($@"(?:_G\.)?pg\.base\.{Regex.Escape(tableName)}\[(\d+)\]\s*=\s*\{{",
            RegexOptions.CultureInvariant);
        var blocks = new List<(int Id, string Block)>();
        foreach (Match match in pattern.Matches(text))
        {
            var openIndex = match.Index + match.Length - 1;
            var closeIndex = FindLuaTableEnd(text, openIndex);
            blocks.Add((int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture),
                text.Substring(openIndex, closeIndex - openIndex + 1)));
        }

        return blocks;
    }

    /// <summary>The whole table assigned to <paramref name="field"/>, braces included, or "" when absent.</summary>
    private static string LuaFieldTable(string block, string field)
    {
        var match = Regex.Match(block, $@"(?m)^\s*{Regex.Escape(field)}\s*=\s*\{{", RegexOptions.CultureInvariant);
        if (!match.Success)
        {
            return "";
        }

        var openIndex = match.Index + match.Length - 1;
        return block.Substring(openIndex, FindLuaTableEnd(block, openIndex) - openIndex + 1);
    }

    /// <summary>
    /// Integer tuples such as <c>need_item = { {1, 1, 3000}, {2, 15008, 300} }</c>, kept as tuples;
    /// ParseLuaIntArray flattens every integer in the table instead.
    /// </summary>
    private static List<int[]> ParseLuaIntTuples(string block, string field)
    {
        var table = LuaFieldTable(block, field);
        if (table.Length < 2)
        {
            return [];
        }

        return Regex.Matches(table[1..^1], @"\{(?<items>[^{}]*)\}", RegexOptions.CultureInvariant)
            .Select(tuple => Regex.Matches(tuple.Groups["items"].Value, @"-?\d+", RegexOptions.CultureInvariant)
                .Select(item => SafeInt(item.Value))
                .ToArray())
            .Where(tuple => tuple.Length > 0)
            .ToList();
    }

    private static List<Dictionary<string, object?>> LoadShipTransformRecords(string path)
    {
        if (!File.Exists(path))
        {
            return [];
        }

        var text = File.ReadAllText(path);
        var pattern = new Regex(@"(?:_G\.)?pg\.base\.ship_data_trans\[(\d+)\]\s*=\s*\{", RegexOptions.CultureInvariant);
        var records = new List<Dictionary<string, object?>>();
        foreach (Match match in pattern.Matches(text))
        {
            var openIndex = match.Index + match.Length - 1;
            var closeIndex = FindLuaTableEnd(text, openIndex);
            var block = text.Substring(openIndex, closeIndex - openIndex + 1);
            var record = ParseLuaRecordBlock(block, ["id", "group_id", "skill_id", "skin_id"]);
            record.TryAdd("id", int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture));

            var listMatch = Regex.Match(block, @"transform_list\s*=\s*\{", RegexOptions.CultureInvariant);
            var transformIds = new List<int>();
            if (listMatch.Success)
            {
                var listOpen = listMatch.Index + listMatch.Length - 1;
                var listClose = FindLuaTableEnd(block, listOpen);
                var listBlock = block.Substring(listOpen, listClose - listOpen + 1);
                foreach (Match idMatch in Regex.Matches(listBlock, @"\{\s*\d+\s*,\s*(\d+)\s*\}",
                             RegexOptions.CultureInvariant))
                {
                    transformIds.Add(SafeInt(idMatch.Groups[1].Value));
                }
            }

            record["transform_ids"] = transformIds.Where(item => item != 0).ToArray();
            records.Add(record);
        }

        return records;
    }

    private static List<Dictionary<string, object?>> LoadFleetTechNationTemplateRecords(string path)
    {
        if (!File.Exists(path))
        {
            return [];
        }

        var text = File.ReadAllText(path);
        var pattern = new Regex(@"(?:_G\.)?pg\.base\.fleet_tech_template\[(\d+)\]\s*=\s*\{",
            RegexOptions.CultureInvariant);
        var records = new List<Dictionary<string, object?>>();
        foreach (Match match in pattern.Matches(text))
        {
            var openIndex = match.Index + match.Length - 1;
            var closeIndex = FindLuaTableEnd(text, openIndex);
            var block = text.Substring(openIndex, closeIndex - openIndex + 1);
            var record = ParseLuaRecordBlock(block, ["id", "groupid", "pt", "cost", "time"]);
            record.TryAdd("id", int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture));
            record["add"] = ParseFleetTechNationAdditions(block);
            records.Add(record);
        }

        return records;
    }

    private static List<Dictionary<string, object?>> ParseFleetTechNationAdditions(string block)
    {
        var match = Regex.Match(block, @"(?m)^\s*add\s*=\s*\{", RegexOptions.CultureInvariant);
        if (!match.Success)
        {
            return [];
        }

        var openIndex = match.Index + match.Length - 1;
        var closeIndex = FindLuaTableEnd(block, openIndex);
        var addBlock = block.Substring(openIndex, closeIndex - openIndex + 1);
        var additions = new List<Dictionary<string, object?>>();
        foreach (Match item in Regex.Matches(
                     addBlock,
                     @"\{\s*\{(?<types>.*?)\}\s*,\s*(?<attr>-?\d+)\s*,\s*(?<value>-?\d+)\s*\}",
                     RegexOptions.Singleline | RegexOptions.CultureInvariant))
        {
            var shipTypes = Regex.Matches(item.Groups["types"].Value, @"-?\d+", RegexOptions.CultureInvariant)
                .Select(value => SafeInt(value.Value))
                .Where(value => value > 0)
                .ToArray();
            var attrType = SafeInt(item.Groups["attr"].Value);
            if (shipTypes.Length == 0 || attrType <= 0)
            {
                continue;
            }

            additions.Add(new Dictionary<string, object?>
            {
                ["ship_types"] = shipTypes,
                ["attr_type"] = attrType,
                ["value"] = SafeInt(item.Groups["value"].Value)
            });
        }

        return additions;
    }

    private static Dictionary<string, object?> ParseLuaRecordBlock(
        string block,
        string[] scalarFields,
        string[]? arrayFields = null)
    {
        var record = new Dictionary<string, object?>(StringComparer.Ordinal);
        var depth = 0;
        foreach (var rawLine in block.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            if (depth == 1)
            {
                var match = Regex.Match(line, @"\s*([A-Za-z_][A-Za-z0-9_]*)\s*=\s*(.+?)\s*,?\s*$",
                    RegexOptions.CultureInvariant);
                if (match.Success && scalarFields.Contains(match.Groups[1].Value))
                {
                    var value = ParseLuaScalar(match.Groups[2].Value);
                    if (HasValue(value))
                    {
                        record[match.Groups[1].Value] = value;
                    }
                }
            }

            depth += LuaBraceDelta(line);
        }

        if (arrayFields is not null)
        {
            foreach (var field in arrayFields)
            {
                var values = ParseLuaIntArray(block, field);
                if (values.Length > 0)
                {
                    record[field] = values;
                }
            }
        }

        return record;
    }

    private static object? ParseLuaScalar(string raw)
    {
        raw = raw.Trim().TrimEnd(',').Trim();
        if (raw.Length == 0)
        {
            return "";
        }

        if (raw.StartsWith('"'))
        {
            var match = Regex.Match(raw, "\"((?:\\\\.|[^\"\\\\])*)\"",
                RegexOptions.Singleline | RegexOptions.CultureInvariant);
            if (match.Success)
            {
                try
                {
                    return JsonSerializer.Deserialize<string>($"\"{match.Groups[1].Value}\"") ?? "";
                }
                catch (JsonException)
                {
                    return match.Groups[1].Value.Replace("\\\"", "\"", StringComparison.Ordinal);
                }
            }
        }

        if (raw == "true")
        {
            return true;
        }

        if (raw == "false")
        {
            return false;
        }

        if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var intValue))
        {
            return intValue;
        }

        if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var doubleValue))
        {
            return doubleValue;
        }

        return "";
    }

    // Every integer in the field's table, nested tables included ({stage id, weight, 0} lists). The table is
    // found by brace matching: a lazy regex stopped at the first inner "}," and kept only the first entry.
    private static int[] ParseLuaIntArray(string block, string field)
    {
        var match = Regex.Match(
            block,
            $@"(?m)^\s*{Regex.Escape(field)}\s*=\s*\{{",
            RegexOptions.CultureInvariant);
        if (!match.Success)
        {
            return [];
        }

        var open = match.Index + match.Length - 1;
        var end = FindLuaTableEnd(block, open);
        if (end <= open)
        {
            return [];
        }

        return Regex.Matches(block[(open + 1)..end], @"-?\d+", RegexOptions.CultureInvariant)
            .Select(item => SafeInt(item.Value))
            .ToArray();
    }

    private static int FindLuaTableEnd(string text, int openIndex)
    {
        var depth = 0;
        var inString = false;
        var escape = false;
        for (var index = openIndex; index < text.Length; index++)
        {
            var character = text[index];
            if (inString)
            {
                if (escape)
                {
                    escape = false;
                }
                else if (character == '\\')
                {
                    escape = true;
                }
                else if (character == '"')
                {
                    inString = false;
                }

                continue;
            }

            if (character == '"')
            {
                inString = true;
            }
            else if (character == '{')
            {
                depth++;
            }
            else if (character == '}')
            {
                depth--;
                if (depth == 0)
                {
                    return index;
                }
            }
        }

        return text.Length - 1;
    }

    private static int LuaBraceDelta(string text)
    {
        var delta = 0;
        var inString = false;
        var escape = false;
        foreach (var character in text)
        {
            if (inString)
            {
                if (escape)
                {
                    escape = false;
                }
                else if (character == '\\')
                {
                    escape = true;
                }
                else if (character == '"')
                {
                    inString = false;
                }

                continue;
            }

            if (character == '"')
            {
                inString = true;
            }
            else if (character == '{')
            {
                delta++;
            }
            else if (character == '}')
            {
                delta--;
            }
        }

        return delta;
    }
}
