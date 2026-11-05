using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using AzurLaneDashboard.Core.Models;

namespace AzurLaneDashboard.Data.Services;

public sealed partial class NativeSnapshotService
{
    private const int GearSlotCount = 5;

    private static readonly Regex TemplateSlotRegex =
        new(@"^equip_([1-5])\s*=\s*\{", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex TemplateIntFieldRegex =
        new(@"^(star|group_type)\s*=\s*(\d+)", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private const int SimulationTemplateIds = 900000;

    private static readonly object TemplateSlotsCacheLock = new();
    private static (string Signature, IReadOnlyDictionary<int, ShipTemplateSlots> Slots)? TemplateSlotsCache;

    /// <summary>One ship_data_template entry: its group, star count and the equipment types of slots 1..5.</summary>
    private sealed record ShipTemplateSlots(int Id, int Group, int Star, IReadOnlyList<IReadOnlySet<int>> Slots)
    {
        /// <summary>
        /// The id without its last digit. A ship's limit breaks share it (499111..499114); retrofit and
        /// simulation templates of the same group use other ranges (101994, 900931), so they never count as a break.
        /// </summary>
        public int BreakFamily => Id / 10;

        /// <summary>Unretrofitted templates sit at group × 10 + break (499112 in group 49911).</summary>
        public bool IsBase => BreakFamily == Group;

        /// <summary>Another template of the group outside the break family and simulation ids: a retrofit.</summary>
        public bool IsRetrofitOf(ShipTemplateSlots ship) =>
            Group == ship.Group && BreakFamily != ship.BreakFamily && Id < SimulationTemplateIds;
    }

    /// <summary>
    /// equip_1..equip_5 of every ship template. Each limit break is its own template, and low breaks can take
    /// fewer types (Max Immelmann's 2nd slot takes torpedo bombers only from her 2nd break).
    /// </summary>
    private static IReadOnlyDictionary<int, ShipTemplateSlots> LoadShipTemplateSlots(string projectRoot)
    {
        var path = Path.Combine(projectRoot, "external", "AzurLaneLuaScripts", "kr", "sharecfgdata",
            "ship_data_template.lua");
        var signature = FileSignature(path);
        lock (TemplateSlotsCacheLock)
        {
            if (TemplateSlotsCache is { } cached && cached.Signature == signature) return cached.Slots;
        }

        var result = new Dictionary<int, ShipTemplateSlots>();
        if (File.Exists(path))
        {
            var id = 0;
            var group = 0;
            var star = 0;
            var slots = NewSlots();
            HashSet<int>? reading = null;

            void Store()
            {
                if (id > 0 && slots.Any(slot => slot.Count > 0))
                    result[id] = new ShipTemplateSlots(id, group, star, slots.Select(slot => (IReadOnlySet<int>)slot).ToList());
            }

            foreach (var line in File.ReadLines(path))
            {
                var entry = ShipTemplateEntryRegex.Match(line);
                if (entry.Success)
                {
                    Store();
                    id = int.Parse(entry.Groups[1].Value, CultureInfo.InvariantCulture);
                    group = 0;
                    star = 0;
                    slots = NewSlots();
                    reading = null;
                    continue;
                }

                if (id <= 0) continue;
                var trimmed = line.Trim();
                if (reading is not null)
                {
                    AddInts(reading, trimmed);
                    if (trimmed.Contains('}')) reading = null;
                    continue;
                }

                var slotMatch = TemplateSlotRegex.Match(trimmed);
                if (slotMatch.Success)
                {
                    var target = slots[slotMatch.Groups[1].Value[0] - '1'];
                    // Only the text after the brace holds types; the field name itself has a digit.
                    var rest = trimmed[(trimmed.IndexOf('{') + 1)..];
                    AddInts(target, rest);
                    if (!rest.Contains('}')) reading = target;
                    continue;
                }

                var field = TemplateIntFieldRegex.Match(trimmed);
                if (field.Success)
                {
                    var value = int.Parse(field.Groups[2].Value, CultureInfo.InvariantCulture);
                    if (field.Groups[1].Value == "star") star = value;
                    else group = value;
                }
            }

            Store();
        }

        lock (TemplateSlotsCacheLock)
        {
            TemplateSlotsCache = (signature, result);
        }

        return result;

        static List<HashSet<int>> NewSlots() => Enumerable.Range(0, GearSlotCount).Select(_ => new HashSet<int>()).ToList();

        static void AddInts(HashSet<int> target, string text)
        {
            var end = text.IndexOf('}');
            foreach (Match number in Regex.Matches(end >= 0 ? text[..end] : text, @"\d+"))
                target.Add(int.Parse(number.Value, CultureInfo.InvariantCulture));
        }
    }

    /// <summary>
    /// What each slot takes at the ship's template, plus the limit break that first adds each further type
    /// (later templates of the same break family, counted in stars from the current one), and for an
    /// unretrofitted ship the types only its retrofit adds.
    /// </summary>
    private static GearSlotRules? GearSlotRulesFor(
        int templateId, int limitBreak, IReadOnlyDictionary<int, ShipTemplateSlots> templates,
        ILookup<int, ShipTemplateSlots> byFamily, ILookup<int, ShipTemplateSlots> byGroup)
    {
        if (!templates.TryGetValue(templateId, out var now)) return null;
        var unlocks = Enumerable.Range(0, GearSlotCount).Select(_ => new Dictionary<int, int>()).ToList();
        foreach (var later in byFamily[now.BreakFamily].Where(item => item.Star > now.Star).OrderBy(item => item.Star))
        {
            for (var slot = 0; slot < GearSlotCount && slot < later.Slots.Count; slot++)
            {
                foreach (var type in later.Slots[slot].Where(type => !now.Slots[slot].Contains(type)))
                    unlocks[slot].TryAdd(type, limitBreak + later.Star - now.Star);
            }
        }

        var retrofit = Enumerable.Range(0, GearSlotCount).Select(_ => new HashSet<int>()).ToList();
        if (now.IsBase)
        {
            foreach (var other in byGroup[now.Group].Where(item => item.IsRetrofitOf(now)))
            {
                for (var slot = 0; slot < GearSlotCount && slot < other.Slots.Count; slot++)
                    retrofit[slot].UnionWith(other.Slots[slot].Where(type => !now.Slots[slot].Contains(type)));
            }
        }

        return new GearSlotRules
        {
            Allowed = now.Slots,
            UnlockLimitBreak = unlocks.Select(map => (IReadOnlyDictionary<int, int>)map).ToList(),
            UnlockByRetrofit = retrofit.Select(set => (IReadOnlySet<int>)set).ToList()
        };
    }
}
