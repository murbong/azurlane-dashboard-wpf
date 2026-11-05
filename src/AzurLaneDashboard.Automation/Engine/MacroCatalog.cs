using System.Text.Json.Nodes;

namespace AzurLaneDashboard.Automation.Engine;

/// <summary>A macro file as the macro page lists it.</summary>
public sealed record MacroInfo(
    string Path,
    string FileName,
    string Title,
    string Description,
    bool UsesPackets,
    bool UsesScreen,
    bool HasMaxCycles,
    string UnsupportedReason,
    IReadOnlyList<MacroStopInfo> Stops)
{
    public bool IsSupported => UnsupportedReason.Length == 0;
}

/// <summary>
/// One "이럴 때 멈춤" line from the macro's optional "ui.stops" list. <see cref="Detail"/> may hold {{var}} and
/// {{game:key}} placeholders for the current value.
/// </summary>
public sealed record MacroStopInfo(string Text, string Detail);

/// <summary>Reads ALAuto's macros folder: titles, what each macro looks at, and whether this runner can run it.</summary>
public static class MacroCatalog
{
    // Korean names and one-line descriptions for ALAuto's bundled macros; other files show their own fields.
    private static readonly Dictionary<string, (string Title, string Description)> Known = new(StringComparer.OrdinalIgnoreCase)
    {
        ["leveling_loop"] = ("쫄작 반복", "반복 출격을 돌리며 전투마다 결과를 확인하고, 쩔이 다음 단계에 닿으면 멈춤"),
        ["combat_auto_base"] = ("전투 기본", "반복 화면을 기다렸다가 다시 반복, 도크 가득이면 퇴역"),
        ["main_loop"] = ("메인 루프", "의뢰·임무·본부·연구를 돌며 알림이 뜬 곳을 처리"),
        ["commission_collect"] = ("의뢰 수령", "완료된 의뢰를 받고 새 의뢰 배치"),
        ["mission_claim"] = ("임무 보상", "완료된 임무 보상 받기"),
        ["headquarters"] = ("본부", "기숙사 간식과 학원 처리"),
        ["research"] = ("연구", "연구 완료 수령과 새 연구 시작")
    };

    public static IReadOnlyList<MacroInfo> Scan(string alautoRoot)
    {
        var directory = Path.Combine(alautoRoot, "macros");
        if (!Directory.Exists(directory)) return [];
        return Directory.GetFiles(directory, "*.json")
            .Where(path => !Path.GetFileName(path).EndsWith("_manifest.json", StringComparison.OrdinalIgnoreCase))
            .Select(path => Inspect(path, alautoRoot))
            .OrderBy(info => Order(info.FileName))
            .ThenBy(info => info.FileName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static MacroInfo Inspect(string path, string alautoRoot)
    {
        var fileName = System.IO.Path.GetFileName(path);
        var key = System.IO.Path.GetFileNameWithoutExtension(path);
        JsonObject macro;
        try
        {
            macro = MacroSchema.Load(path);
        }
        catch (MacroSchemaException ex)
        {
            return new MacroInfo(path, fileName, key, "", false, false, false, $"읽을 수 없는 매크로: {ex.Message}", []);
        }

        var known = Known.GetValueOrDefault(key);
        var title = known.Title ?? (MacroSchema.Text(macro["name"]) is { Length: > 0 } name ? name : key);
        var description = known.Description ?? MacroSchema.Text(macro["description"]);
        var scan = new Usage();
        Walk(macro, path, alautoRoot, scan, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        var stops = (macro["ui"] as JsonObject)?["stops"] is JsonArray list
            ? list.OfType<JsonObject>()
                .Select(item => new MacroStopInfo(MacroSchema.Text(item["text"]), MacroSchema.Text(item["detail"])))
                .Where(item => item.Text.Length > 0)
                .ToList()
            : [];
        var unsupported = scan.Plugins.Where(plugin => !MacroRunner.SupportedPlugins.Contains(plugin)).Distinct().ToList();
        var reason = unsupported.Count > 0
            ? $"아직 지원하지 않는 플러그인이 필요합니다: {string.Join(", ", unsupported)}"
            : scan.MissingMacros.Count > 0
                ? $"부르는 매크로가 없습니다: {string.Join(", ", scan.MissingMacros.Distinct())}"
                : "";
        return new MacroInfo(path, fileName, title, description, scan.UsesPackets, scan.UsesScreen, scan.UsesMaxCycles,
            reason, stops);
    }

    private static int Order(string fileName) => fileName switch
    {
        "leveling_loop.json" => 0,
        "combat_auto_base.json" => 1,
        "main_loop.json" => 2,
        _ => 3
    };

    // Follows run_macro so a macro that calls a macro with an unsupported plugin is marked as well.
    private static void Walk(JsonNode? node, string macroPath, string alautoRoot, Usage scan, HashSet<string> visited)
    {
        switch (node)
        {
            case JsonObject map:
                var action = MacroSchema.Text(map["action"]);
                var type = MacroSchema.Text(map["type"]);
                if (action == "plugin" || type == "plugin") scan.Plugins.Add(MacroSchema.Text(map["plugin"]));
                if (type == "image" || action is "tap_image" or "tap_found_image") scan.UsesScreen = true;
                if (type == "compare" && MacroSchema.Text(map["source"]) == "game" ||
                    map["ref"] is { } reference && MacroSchema.Text(reference) == "game")
                {
                    scan.UsesPackets = true;
                }

                if (MacroSchema.Text(map["var"]) == "max_cycles" && action == "set") scan.UsesMaxCycles = true;
                if (action == "run_macro") Follow(MacroSchema.Text(map["path"]), macroPath, alautoRoot, scan, visited);
                foreach (var (key, child) in map)
                {
                    if (key != "ui") Walk(child, macroPath, alautoRoot, scan, visited);
                }

                break;
            case JsonArray list:
                foreach (var child in list) Walk(child, macroPath, alautoRoot, scan, visited);
                break;
        }
    }

    private static void Follow(string target, string macroPath, string alautoRoot, Usage scan, HashSet<string> visited)
    {
        var directory = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(macroPath)) ?? alautoRoot;
        var path = System.IO.Path.GetFullPath(System.IO.Path.Combine(directory, target));
        if (!File.Exists(path)) path = System.IO.Path.GetFullPath(System.IO.Path.Combine(alautoRoot, target));
        if (!File.Exists(path))
        {
            scan.MissingMacros.Add(target);
            return;
        }

        if (!visited.Add(path)) return;
        try
        {
            var usedMaxCycles = scan.UsesMaxCycles;
            Walk(MacroSchema.Load(path), path, alautoRoot, scan, visited);
            // max_cycles belongs to the macro the player starts, not to the ones it calls.
            scan.UsesMaxCycles = usedMaxCycles;
        }
        catch (MacroSchemaException)
        {
            scan.MissingMacros.Add(target);
        }
    }

    private sealed class Usage
    {
        public bool UsesPackets { get; set; }
        public bool UsesScreen { get; set; }
        public bool UsesMaxCycles { get; set; }
        public List<string> Plugins { get; } = [];
        public List<string> MissingMacros { get; } = [];
    }
}
