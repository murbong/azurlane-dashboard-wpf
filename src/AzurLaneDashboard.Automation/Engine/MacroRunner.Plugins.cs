using System.Globalization;
using System.Text.Json.Nodes;
using AzurLaneDashboard.Automation.Vision;

namespace AzurLaneDashboard.Automation.Engine;

/// <summary>
/// ALAuto's macro_plugins/headquarters.py. The courtyard's food and snack counts come from packets (the
/// dashboard's dorm.* state) while capture runs; without it the plugins read the food bar off the screen as
/// ALAuto does. print_stats reports the oil the packets follow instead of reading it from the screen.
/// </summary>
public sealed partial class MacroRunner
{
    private static readonly int[] SnackValues = [1000, 2000, 3000, 5000, 10000, 20000];
    // SUPPLY_WHITEOUT_THRESHOLD: a snack slot with none left is greyed out below this brightness.
    private const int SnackShownBrightness = 120;
    // _estimate_dorm_fill_ratio: the bar's empty part, in OpenCV HSV.
    private static readonly (int H, int S, int V) EmptyBarLow = (0, 0, 0);
    private static readonly (int H, int S, int V) EmptyBarHigh = (255, 130, 220);
    private const int DefaultDormCapacity = 44000;
    private const int MaxDormPrompts = 30;

    public static IReadOnlySet<string> SupportedPlugins { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "headquarters.refill_dorm",
        "headquarters.feed_snacks"
    };

    private object? RunPlugin(JsonObject spec, Context context)
    {
        var name = MacroSchema.Text(spec["plugin"]);
        return name switch
        {
            "headquarters.refill_dorm" => RefillDorm(spec, context),
            "headquarters.feed_snacks" => FeedSnacks(spec, context, null),
            _ => throw new MacroUnsupportedException($"플러그인 '{name}'은 아직 지원하지 않습니다.")
        };
    }

    // Clears the settlement and feeding prompts, then feeds when the food is under the start threshold.
    private bool RefillDorm(JsonObject payload, Context context)
    {
        ScriptSleep(Number(payload["initial_sleep"]) ?? 5, null);
        for (var round = 0; round < MaxDormPrompts; round++)
        {
            ScriptSleep(Number(payload["poll_seconds"]) ?? 1, null);
            UpdateScreen();
            if (FindImage("headquarters/dorm_summary_confirm_button", options.DefaultSimilarity, false, false, false) is not null)
            {
                TouchRandomly(NamedRegion(context, "confirm_dorm_summary"));
                continue;
            }

            if (FindImage("headquarters/give_food_button", options.DefaultSimilarity, false, false, false) is not null)
            {
                TouchRandomly(NamedRegion(context, "ignore_give_food_button"));
                continue;
            }

            var dorm = ReadDorm(payload);
            var start = Number(payload["start_feed_threshold"]) ?? 0.5;
            if (dorm.Food >= dorm.Capacity * start)
            {
                Log(MacroLogSource.Macro, $"숙소 식량이 {start:P0} 이상이라 간식을 주지 않습니다.");
                return false;
            }

            FeedSnacks(payload, context, dorm);
            return true;
        }

        Log(MacroLogSource.Screen, "숙소 안내 창이 계속 떠서 간식 확인을 건너뜁니다.");
        return false;
    }

    // Opens the snack menu and taps the largest snack that fits under the stop threshold until the food reaches it.
    private bool FeedSnacks(JsonObject payload, Context context, DormReading? reading)
    {
        var dorm = reading ?? ReadDorm(payload);
        var food = (double)dorm.Food;
        var target = dorm.Capacity * (Number(payload["stop_feed_threshold"]) ?? 0.9);
        TouchRandomly(NamedRegion(context, "supplies_bar"));
        ScriptSleep(Number(payload["menu_sleep"]) ?? 1, null);
        UpdateScreen();

        var counts = PacketSnackCounts();
        var alert = FindImage("menu/alert_close", options.DefaultSimilarity, false, false, false);
        var maxTaps = payload.ContainsKey("max_retries") ? MacroSchema.Int(payload["max_retries"]) : 40;
        var fed = new List<int>();
        while (fed.Count < maxTaps && alert is null && target - food > 0)
        {
            if (PickSnack(context, target - food, counts) is not { } snack) break;
            TouchRandomly(snack.Region);
            ScriptSleep(Number(payload["retry_wait"]) ?? 0.5, null);
            UpdateScreen();
            alert = FindImage("menu/alert_close", options.DefaultSimilarity, false, false, false);
            fed.Add(snack.Value);
            if (counts is not null) counts[snack.Value] = Math.Max(0, counts[snack.Value] - 1);
            // The packets' food goes up by what the snack holds; the screen bar is read again as ALAuto does.
            food = dorm.FromPackets
                ? Math.Min(dorm.Capacity, food + snack.Value)
                : ScanDormBar(payload) * dorm.Capacity;
        }

        Log(dorm.FromPackets ? MacroLogSource.Packet : MacroLogSource.Screen, fed.Count == 0
            ? $"줄 간식이 없습니다 (식량 {food:N0}/{dorm.Capacity:N0}, 목표 {target:N0})."
            : $"간식 {fed.Count}개({string.Join(", ", fed.Select(value => value.ToString("N0", CultureInfo.InvariantCulture)))}) → 식량 약 {food:N0}/{dorm.Capacity:N0}");
        if (alert is { } close)
        {
            TouchRandomly(close);
            ScriptSleep(1, null);
            UpdateScreen();
        }

        TouchRandomly(NamedRegion(context, "exit_snacks_menu"));
        return true;
    }

    // _pick_supply_greedy: the largest snack that is shown, in stock by the packets and not over the remainder.
    private (int Value, ScreenRegion Region)? PickSnack(Context context, double remaining, Dictionary<int, int>? counts)
    {
        EnsureScreen();
        (int Value, ScreenRegion Region)? best = null;
        foreach (var value in SnackOrder(context))
        {
            var index = Array.IndexOf(SnackValues, value);
            if (index < 0 || !context.Regions.TryGetValue($"snack_{index + 1}", out var region)) continue;
            var shown = frame!.AverageHsv(region).V > SnackShownBrightness;
            var stocked = counts is null || counts.GetValueOrDefault(value) > 0;
            if (shown != stocked && counts is not null)
            {
                Log(MacroLogSource.Packet,
                    $"간식 {value:N0}: 화면 {(shown ? "있음" : "없음")}, 패킷 {counts.GetValueOrDefault(value):N0}개 → 건너뜀");
            }

            if (shown && stocked && value <= remaining && (best is null || value > best.Value.Value))
                best = (value, region);
        }

        return best;
    }

    // dorm.AvailableSupplies from the config, largest first by default.
    private static IEnumerable<int> SnackOrder(Context context)
    {
        var configured = context.Runtime["dorm"] is JsonObject dorm && dorm["AvailableSupplies"] is JsonArray list
            ? list.Select(item => MacroValues.FromJson(item))
                .Where(item => item is not null && MacroValues.IsNumber(item))
                .Select(item => (int)MacroValues.ToLong(item))
                .ToList()
            : [];
        return configured.Count > 0 ? configured : SnackValues.Reverse();
    }

    private DormReading ReadDorm(JsonObject payload)
    {
        EnsureScreen();
        var bar = ScanDormBar(payload);
        if (PacketNumber("dorm.food") is { } food && PacketNumber("dorm.capacity") is { } capacity and > 0)
        {
            Log(MacroLogSource.Packet, string.Create(CultureInfo.InvariantCulture,
                $"숙소 식량 {food:N0}/{capacity:N0} ({(double)food / capacity:P0}) · 화면 막대 {bar:P0}"));
            return new DormReading((int)food, (int)capacity, true);
        }

        var fallback = payload.ContainsKey("total_capacity") ? MacroSchema.Int(payload["total_capacity"]) : DefaultDormCapacity;
        Log(MacroLogSource.Screen, string.Create(CultureInfo.InvariantCulture,
            $"패킷 식량이 없어 화면 막대로 봅니다: {bar:P0}"));
        return new DormReading((int)(bar * fallback), fallback, false);
    }

    private double ScanDormBar(JsonObject payload)
    {
        EnsureScreen();
        int Setting(string key, int fallback) => payload.ContainsKey(key) ? MacroSchema.Int(payload[key]) : fallback;
        var x1 = Setting("fill_scan_x1", 595);
        var x2 = Setting("fill_scan_x2", 1470);
        var region = new ScreenRegion(x1, Setting("fill_scan_y", 460), Math.Max(1, x2 - x1),
            Math.Max(1, Setting("fill_scan_h", 3)));
        return frame!.FilledColumnRatio(region, EmptyBarLow, EmptyBarHigh);
    }

    // Snack counts by food value while the packet state is live; null when they are not known.
    private Dictionary<int, int>? PacketSnackCounts()
    {
        var counts = new Dictionary<int, int>();
        foreach (var value in SnackValues)
        {
            if (PacketNumber($"dorm.snacks.{value}") is not { } count) return null;
            counts[value] = (int)count;
        }

        return counts;
    }

    // A number from the dashboard's live packet state; null while capture is off or the state is stale.
    private long? PacketNumber(string key)
    {
        if (!MacroValues.Truthy(options.Game.Get("live"))) return null;
        return options.Game.Get(key) is { } value && MacroValues.IsNumber(value) ? MacroValues.ToLong(value) : null;
    }

    // stats.print_stats with check_oil's oil, which the packets follow instead of the screen.
    private void PrintStats(JsonObject step, Context context)
    {
        var parts = stats.Select(pair => $"{pair.Key} {pair.Value}").ToList();
        var limit = ResolveValue(step["oil_limit"], context) is { } raw && MacroValues.IsNumber(raw) ? MacroValues.ToLong(raw) : 0;
        if (PacketNumber("resources.oil") is { } oil)
        {
            var estimated = MacroValues.Truthy(options.Game.Get("resources.oil_estimated"));
            parts.Add(string.Create(CultureInfo.InvariantCulture, $"기름 {oil:N0}{(estimated ? " (추정)" : "")}"));
            if (limit > 0 && oil < limit)
                Log(MacroLogSource.Packet, string.Create(CultureInfo.InvariantCulture, $"기름이 한도 {limit:N0}보다 적습니다: {oil:N0}"));
        }
        else
        {
            parts.Add("기름 모름 (캡처 꺼짐)");
        }

        Log(MacroLogSource.Macro, parts.Count == 0 ? "통계: 아직 없음" : "통계: " + string.Join(" · ", parts));
    }

    private static ScreenRegion NamedRegion(Context context, string name) =>
        context.Regions.TryGetValue(name, out var region)
            ? region
            : throw new MacroSchemaException($"Unknown region '{name}'.");

    private sealed record DormReading(int Food, int Capacity, bool FromPackets);
}
