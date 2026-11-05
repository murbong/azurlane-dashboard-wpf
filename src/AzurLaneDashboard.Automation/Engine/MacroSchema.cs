using System.Text.Json;
using System.Text.Json.Nodes;

namespace AzurLaneDashboard.Automation.Engine;

public sealed class MacroSchemaException(string message) : Exception(message);

/// <summary>
/// Port of ALAuto's macro_engine/schema.py: turns the shorthand JSON macros into the canonical form the runner
/// reads, so the same files run in ALAuto's Python viewer and here.
/// </summary>
public static class MacroSchema
{
    public static readonly IReadOnlySet<string> SupportedActions = new HashSet<string>(StringComparer.Ordinal)
    {
        "log", "sleep", "update_screen", "wait_update_screen", "tap_region", "tap_image", "tap_found_image", "back",
        "swipe", "set", "increment", "stats_increment", "print_stats", "if", "while", "repeat", "wait_for", "call",
        "run_macro", "plugin", "break", "continue", "return"
    };

    private static readonly string[] CompareKeys = ["equals", "not_equals", "gt", "gte", "lt", "lte"];
    private static readonly string[] CompareSources = ["var", "runtime", "config", "feature", "game"];

    public static JsonObject Load(string path)
    {
        JsonNode? root;
        try
        {
            root = JsonNode.Parse(File.ReadAllText(path), documentOptions: new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip
            });
        }
        catch (JsonException ex)
        {
            throw new MacroSchemaException($"{Path.GetFileName(path)}: JSON 오류 {ex.Message}");
        }

        return NormalizeMacro(root);
    }

    public static JsonObject NormalizeMacro(JsonNode? macro)
    {
        if (macro is not JsonObject source) throw new MacroSchemaException("Macro root must be an object.");
        var normalized = (JsonObject)source.DeepClone();
        if (normalized["regions"] is JsonObject regions)
        {
            foreach (var name in regions.Select(pair => pair.Key).ToList())
                regions[name] = NormalizeRegion(regions[name], $"regions.{name}");
        }

        if (normalized["steps"] is not JsonArray)
            throw new MacroSchemaException("Macro is missing a top-level 'steps' list.");
        normalized["steps"] = NormalizeSteps(normalized["steps"], "steps");
        if (normalized["routines"] is JsonObject routines)
        {
            foreach (var name in routines.Select(pair => pair.Key).ToList())
                routines[name] = NormalizeSteps(routines[name], $"routines.{name}");
        }

        return normalized;
    }

    private static JsonArray NormalizeSteps(JsonNode? steps, string path)
    {
        if (steps is not JsonArray list) throw new MacroSchemaException($"{path} must be a list.");
        var result = new JsonArray();
        for (var index = 0; index < list.Count; index++)
            result.Add(NormalizeStep(list[index], $"{path}[{index}]"));
        return result;
    }

    private static JsonObject NormalizeStep(JsonNode? step, string path)
    {
        if (step is not JsonObject source) throw new MacroSchemaException($"{path} must be an object.");
        var normalized = (JsonObject)source.DeepClone();
        var action = Text(normalized["action"]);
        if (!SupportedActions.Contains(action))
            throw new MacroSchemaException($"{path} has unsupported action '{action}'");

        if (action is "if" or "while" or "wait_for")
        {
            if (!normalized.ContainsKey("condition"))
                throw new MacroSchemaException($"{path} contains '{action}' without 'condition'.");
            normalized["condition"] = NormalizeCondition(normalized["condition"], path + ".condition");
        }

        if (action is "if" or "while" or "repeat" && !normalized.ContainsKey("steps"))
            throw new MacroSchemaException($"{path} contains '{action}' without 'steps'.");
        foreach (var key in new[] { "steps", "else_steps", "timeout_steps" })
        {
            if (normalized.ContainsKey(key)) normalized[key] = NormalizeSteps(normalized[key], $"{path}.{key}");
        }

        switch (action)
        {
            case "swipe":
                return NormalizeSwipe(normalized, path);
            case "set":
            case "increment":
            {
                var name = normalized["var"] ?? normalized["name"];
                normalized.Remove("name");
                if (string.IsNullOrEmpty(Text(name))) throw new MacroSchemaException($"{path} is missing 'var'.");
                normalized["var"] = Text(name);
                if (action == "set" && normalized.ContainsKey("value"))
                    normalized["value"] = NormalizeValue(normalized["value"], path + ".value");
                if (action == "increment" && !normalized.ContainsKey("amount"))
                {
                    normalized["amount"] = normalized["value"]?.DeepClone() ?? 1;
                    normalized.Remove("value");
                }

                break;
            }
            case "return":
                if (normalized.ContainsKey("value")) normalized["value"] = NormalizeValue(normalized["value"], path + ".value");
                break;
            case "print_stats":
                if (!normalized.ContainsKey("oil_limit") && normalized["oil_limit_config"] is { } config)
                    normalized["oil_limit"] = new JsonObject { ["ref"] = "runtime", ["key"] = Text(config) };
                normalized.Remove("oil_limit_config");
                break;
            case "run_macro":
                if (normalized["variables"] is JsonObject variables)
                {
                    foreach (var key in variables.Select(pair => pair.Key).ToList())
                        variables[key] = NormalizeValue(variables[key], $"{path}.variables.{key}");
                }

                break;
            case "plugin":
                if (string.IsNullOrEmpty(Text(normalized["plugin"])))
                    throw new MacroSchemaException($"{path} plugin action is missing 'plugin'.");
                break;
        }

        return normalized;
    }

    private static JsonObject NormalizeSwipe(JsonObject step, string path)
    {
        if (!step.ContainsKey("from") && step.ContainsKey("x1") && step.ContainsKey("y1"))
            step["from"] = new JsonObject { ["x"] = Int(step["x1"]), ["y"] = Int(step["y1"]) };
        if (!step.ContainsKey("to") && step.ContainsKey("x2") && step.ContainsKey("y2"))
            step["to"] = new JsonObject { ["x"] = Int(step["x2"]), ["y"] = Int(step["y2"]) };
        if (!step.ContainsKey("duration_ms") && step.ContainsKey("ms")) step["duration_ms"] = Int(step["ms"]);
        foreach (var key in new[] { "x1", "y1", "x2", "y2", "ms" }) step.Remove(key);
        if (!step.ContainsKey("from") || !step.ContainsKey("to") || !step.ContainsKey("duration_ms"))
            throw new MacroSchemaException($"{path} must contain from/to/duration_ms.");
        step["from"] = NormalizePoint(step["from"], path + ".from");
        step["to"] = NormalizePoint(step["to"], path + ".to");
        step["duration_ms"] = Int(step["duration_ms"]);
        return step;
    }

    public static JsonNode? NormalizeCondition(JsonNode? condition, string path)
    {
        if (condition is not JsonObject source) return condition?.DeepClone();
        if (source.ContainsKey("type")) return NormalizeCanonicalCondition(source, path);
        if (source["all"] is JsonArray all) return Combine("all", all, path + ".all");
        if (source["any"] is JsonArray any) return Combine("any", any, path + ".any");
        if (source.ContainsKey("not"))
            return new JsonObject { ["type"] = "not", ["item"] = NormalizeCondition(source["not"], path + ".not") };
        if (source.ContainsKey("always"))
            return new JsonObject { ["type"] = "always", ["value"] = Truthy(source["always"]) };
        if (source.ContainsKey("image"))
        {
            var image = new JsonObject { ["type"] = "image", ["image"] = source["image"]?.DeepClone() };
            foreach (var key in new[] { "similarity", "color", "interrupt_if_not_found", "use_mask", "x_between", "y_between" })
            {
                if (source.ContainsKey(key)) image[key] = source[key]?.DeepClone();
            }

            return image;
        }

        foreach (var sourceKey in CompareSources)
        {
            if (source.ContainsKey(sourceKey)) return NormalizeCompare(sourceKey, Text(source[sourceKey]), source, path);
        }

        if (source.ContainsKey("region_color"))
        {
            var normalized = new JsonObject
            {
                ["type"] = "region_color",
                ["region"] = NormalizeRegion(source["region_color"], path + ".region_color")
            };
            if (source.ContainsKey("low") && source.ContainsKey("high"))
            {
                normalized["match"] = new JsonObject
                {
                    ["low"] = source["low"]!.DeepClone(), ["high"] = source["high"]!.DeepClone()
                };
            }
            else
            {
                normalized["channel"] = source.ContainsKey("channel") ? Int(source["channel"]) : 0;
                normalized["op"] = source.ContainsKey("gt") ? "gt" : "lt";
                normalized["value"] = (source["gt"] ?? source["lt"])?.DeepClone();
            }

            return normalized;
        }

        return source.DeepClone();
    }

    private static JsonObject Combine(string type, JsonArray items, string path)
    {
        var normalized = new JsonArray();
        foreach (var item in items) normalized.Add(NormalizeCondition(item, path));
        return new JsonObject { ["type"] = type, ["items"] = normalized };
    }

    private static JsonObject NormalizeCanonicalCondition(JsonObject condition, string path)
    {
        var normalized = (JsonObject)condition.DeepClone();
        var type = Text(normalized["type"]);
        switch (type)
        {
            case "all":
            case "any":
            {
                var items = new JsonArray();
                if (normalized["items"] is JsonArray list)
                    foreach (var item in list) items.Add(NormalizeCondition(item, path + ".items"));
                normalized["items"] = items;
                return normalized;
            }
            case "not":
                normalized["item"] = NormalizeCondition(normalized["item"], path + ".item");
                return normalized;
            case "always":
                normalized["value"] = !normalized.ContainsKey("value") || Truthy(normalized["value"]);
                return normalized;
            case "image":
                return normalized;
            case "compare":
            {
                var source = Text(normalized["source"]);
                if (!CompareSources.Contains(source)) throw new MacroSchemaException($"{path} has invalid compare source.");
                if (!normalized.ContainsKey("key")) throw new MacroSchemaException($"{path} compare condition is missing key.");
                if (source == "config") normalized["source"] = "runtime";
                if (source == "feature")
                {
                    normalized["source"] = "runtime";
                    normalized["key"] = $"{Text(normalized["key"])}.enabled";
                }

                normalized["op"] = normalized.ContainsKey("op") ? Text(normalized["op"]) : "truthy";
                if (normalized.ContainsKey("value")) normalized["value"] = NormalizeValue(normalized["value"], path + ".value");
                return normalized;
            }
            case "region_color":
                normalized["region"] = NormalizeRegion(normalized["region"], path + ".region");
                if (!normalized.ContainsKey("match"))
                {
                    normalized["channel"] = normalized.ContainsKey("channel") ? Int(normalized["channel"]) : 0;
                    normalized["op"] = normalized.ContainsKey("op") ? Text(normalized["op"]) : "gt";
                }

                return normalized;
            case "plugin":
                if (string.IsNullOrEmpty(Text(normalized["plugin"])))
                    throw new MacroSchemaException($"{path} plugin condition is missing 'plugin'.");
                return normalized;
            default:
                throw new MacroSchemaException($"{path} has unsupported condition type '{type}'");
        }
    }

    private static JsonObject NormalizeCompare(string source, string key, JsonObject condition, string path)
    {
        var normalized = new JsonObject
        {
            ["type"] = "compare",
            ["source"] = source is "config" or "feature" ? "runtime" : source,
            ["key"] = source == "feature" ? $"{key}.enabled" : key,
            ["op"] = "truthy"
        };
        foreach (var op in CompareKeys)
        {
            if (!condition.ContainsKey(op)) continue;
            normalized["op"] = op;
            normalized["value"] = NormalizeValue(condition[op], $"{path}.{op}");
            break;
        }

        return normalized;
    }

    public static JsonNode? NormalizeValue(JsonNode? value, string path)
    {
        if (value is not JsonObject source) return value?.DeepClone();
        if (source.ContainsKey("ref"))
        {
            var reference = Text(source["ref"]);
            if (reference is not ("var" or "runtime" or "config" or "stats" or "game"))
                throw new MacroSchemaException($"{path} has invalid ref '{reference}'");
            if (!source.ContainsKey("key")) throw new MacroSchemaException($"{path} ref is missing key.");
            return new JsonObject { ["ref"] = reference == "config" ? "runtime" : reference, ["key"] = Text(source["key"]) };
        }

        foreach (var (shorthand, reference) in new[]
                 {
                     ("var", "var"), ("config", "runtime"), ("runtime", "runtime"), ("stats", "stats"), ("game", "game")
                 })
        {
            if (source.ContainsKey(shorthand))
                return new JsonObject { ["ref"] = reference, ["key"] = Text(source[shorthand]) };
        }

        return source.DeepClone();
    }

    public static JsonObject NormalizeRegion(JsonNode? region, string path)
    {
        if (region is JsonArray list)
        {
            if (list.Count != 4) throw new MacroSchemaException($"{path} must contain four numbers.");
            return new JsonObject { ["x"] = Int(list[0]), ["y"] = Int(list[1]), ["w"] = Int(list[2]), ["h"] = Int(list[3]) };
        }

        if (region is JsonObject box)
        {
            var missing = new[] { "x", "y", "w", "h" }.Where(key => !box.ContainsKey(key)).ToList();
            if (missing.Count > 0) throw new MacroSchemaException($"{path} is missing {string.Join(", ", missing)}.");
            return new JsonObject { ["x"] = Int(box["x"]), ["y"] = Int(box["y"]), ["w"] = Int(box["w"]), ["h"] = Int(box["h"]) };
        }

        throw new MacroSchemaException($"{path} must be [x, y, w, h] or {{x, y, w, h}}.");
    }

    private static JsonObject NormalizePoint(JsonNode? point, string path)
    {
        if (point is JsonObject box && box.ContainsKey("x") && box.ContainsKey("y"))
            return new JsonObject { ["x"] = Int(box["x"]), ["y"] = Int(box["y"]) };
        if (point is JsonArray list && list.Count == 2)
            return new JsonObject { ["x"] = Int(list[0]), ["y"] = Int(list[1]) };
        throw new MacroSchemaException($"{path} must be {{x, y}} or [x, y].");
    }

    public static string Text(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<string>(out var text) ? text : node?.ToJsonString() ?? "";

    public static int Int(JsonNode? node) =>
        node is JsonValue value
            ? value.TryGetValue<int>(out var number) ? number
            : value.TryGetValue<double>(out var real) ? (int)real
            : value.TryGetValue<string>(out var text) && int.TryParse(text, out var parsed) ? parsed : 0
            : 0;

    internal static bool Truthy(JsonNode? node) => MacroValues.Truthy(MacroValues.FromJson(node));
}
