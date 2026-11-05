using System.Globalization;
using AzurLaneDashboard.Core.Models;

namespace AzurLaneDashboard.Data.Services;

public sealed partial class NativeSnapshotService
{
    private sealed class EquipmentStatRecord
    {
        public int BaseId { get; set; }
        public Dictionary<int, string> Attributes { get; } = new();
        public Dictionary<int, int> Values { get; } = new();
    }

    private static IReadOnlyDictionary<int, IReadOnlyList<EquipmentAttributeRow>> LoadEquipmentAttributes(
        string projectRoot)
    {
        var path = Path.Combine(projectRoot, "external", "AzurLaneLuaScripts", "kr", "sharecfgdata",
            "equip_data_statistics.lua");
        if (!File.Exists(path))
        {
            return new Dictionary<int, IReadOnlyList<EquipmentAttributeRow>>();
        }

        var signature = FileSignature(path);
        lock (EquipmentAttributesCacheLock)
        {
            if (EquipmentAttributesCache.TryGetValue(path, out var cached) && cached.Signature == signature)
            {
                return cached.Attributes;
            }
        }

        var records = LoadEquipmentStatRecords(path);
        var attributes = new Dictionary<int, IReadOnlyList<EquipmentAttributeRow>>();
        foreach (var equipmentId in records.Keys)
        {
            var rows = ResolveEquipmentAttributes(equipmentId, records);
            if (rows.Count > 0)
            {
                attributes[equipmentId] = rows;
            }
        }

        lock (EquipmentAttributesCacheLock)
        {
            EquipmentAttributesCache[path] = new CachedEquipmentAttributes(signature, attributes);
        }

        return attributes;
    }

    private static Dictionary<int, EquipmentStatRecord> LoadEquipmentStatRecords(string path)
    {
        var result = new Dictionary<int, EquipmentStatRecord>();
        var currentId = 0;
        var braceDepth = 0;
        EquipmentStatRecord? current = null;

        void StoreCurrent()
        {
            if (currentId > 0 && current is not null)
            {
                result[currentId] = current;
            }

            currentId = 0;
            braceDepth = 0;
            current = null;
        }

        foreach (var line in File.ReadLines(path))
        {
            var entry = EquipmentStatisticsEntryRegex.Match(line);
            if (entry.Success)
            {
                StoreCurrent();
                currentId = int.Parse(entry.Groups[1].Value, CultureInfo.InvariantCulture);
                braceDepth = CountLuaBraces(line);
                current = new EquipmentStatRecord();
                continue;
            }

            if (currentId <= 0 || current is null)
            {
                continue;
            }

            var trimmed = line.Trim();
            if (braceDepth == 1 && trimmed.StartsWith("base =", StringComparison.Ordinal))
            {
                current.BaseId = LuaAssignmentInt(trimmed);
            }

            for (var index = 1; index <= 3; index++)
            {
                if (braceDepth == 1 && trimmed.StartsWith($"attribute_{index} =", StringComparison.Ordinal))
                {
                    current.Attributes[index] = LuaString(trimmed);
                }
                else if (braceDepth == 1 && trimmed.StartsWith($"value_{index} =", StringComparison.Ordinal))
                {
                    current.Values[index] = LuaAssignmentInt(trimmed);
                }
            }

            braceDepth += CountLuaBraces(line);
            if (braceDepth <= 0)
            {
                StoreCurrent();
            }
        }

        StoreCurrent();
        return result;
    }

    private static IReadOnlyList<EquipmentAttributeRow> ResolveEquipmentAttributes(
        int equipmentId,
        IReadOnlyDictionary<int, EquipmentStatRecord> records)
    {
        var attributes = new List<EquipmentAttributeRow>();
        for (var index = 1; index <= 3; index++)
        {
            var attribute = ResolveEquipmentAttribute(equipmentId, index, records, new HashSet<int>());
            var value = ResolveEquipmentValue(equipmentId, index, records, new HashSet<int>());
            if (!string.IsNullOrWhiteSpace(attribute) && value != 0)
            {
                attributes.Add(new EquipmentAttributeRow(
                    index,
                    attribute,
                    EquipmentAttributeRow.LabelFor(attribute),
                    value));
            }
        }

        return attributes;
    }

    private static string ResolveEquipmentAttribute(
        int equipmentId,
        int index,
        IReadOnlyDictionary<int, EquipmentStatRecord> records,
        HashSet<int> visiting)
    {
        if (!visiting.Add(equipmentId) || !records.TryGetValue(equipmentId, out var record))
        {
            return "";
        }

        if (record.Attributes.TryGetValue(index, out var attribute) && !string.IsNullOrWhiteSpace(attribute))
        {
            return attribute;
        }

        return record.BaseId > 0
            ? ResolveEquipmentAttribute(record.BaseId, index, records, visiting)
            : "";
    }

    private static int ResolveEquipmentValue(
        int equipmentId,
        int index,
        IReadOnlyDictionary<int, EquipmentStatRecord> records,
        HashSet<int> visiting)
    {
        if (!visiting.Add(equipmentId) || !records.TryGetValue(equipmentId, out var record))
        {
            return 0;
        }

        if (record.Values.TryGetValue(index, out var value))
        {
            return value;
        }

        return record.BaseId > 0 ? ResolveEquipmentValue(record.BaseId, index, records, visiting) : 0;
    }

    private static int CountLuaBraces(string line) => line.Count(character => character == '{') -
                                                     line.Count(character => character == '}');

    private static int LuaAssignmentInt(string line)
    {
        var equals = line.IndexOf('=');
        return equals >= 0 ? LuaInt(line[(equals + 1)..]) : LuaInt(line);
    }

    private static string LuaString(string line)
    {
        var start = line.IndexOf('"');
        if (start < 0)
        {
            return "";
        }

        var end = line.IndexOf('"', start + 1);
        return end > start ? line[(start + 1)..end] : "";
    }
}
