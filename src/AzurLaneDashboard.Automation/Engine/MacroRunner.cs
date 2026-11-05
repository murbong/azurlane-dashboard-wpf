using System.Globalization;
using System.Text.Json.Nodes;
using AzurLaneDashboard.Automation.Device;
using AzurLaneDashboard.Automation.Vision;

namespace AzurLaneDashboard.Automation.Engine;

public sealed class MacroUnsupportedException(string message) : Exception(message);

public sealed class MacroRunOptions
{
    /// <summary>The ALAuto folder: macros/, assets/ and config.json.</summary>
    public required string AlautoRoot { get; init; }
    /// <summary>ALAuto's config.json; a macro's own "runtime" block is merged over it.</summary>
    public JsonObject BaseRuntime { get; init; } = new();
    public IGameState Game { get; init; } = NoGameState.Instance;
    public Action<MacroEvent>? OnEvent { get; init; }
    public double DefaultSimilarity { get; init; } = 0.95;
    public Random Random { get; init; } = Random.Shared;
    /// <summary>Waits; the default blocks until the time passes or the run is cancelled.</summary>
    public Action<TimeSpan, CancellationToken>? Sleep { get; init; }
}

/// <summary>
/// Port of ALAuto's macro_engine/runtime.py over <see cref="IAutomationDevice"/> and OpenCV. Steps, conditions,
/// waits, taps and their timings follow the Python runner so the same JSON macros behave the same. Cancelling the
/// token stops the run at the next step or inside a wait.
/// </summary>
public sealed partial class MacroRunner(IAutomationDevice device, MacroRunOptions options, CancellationToken cancellation)
    : IDisposable
{
    private readonly Dictionary<string, TemplateLibrary> libraries = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, long> stats = new(StringComparer.Ordinal);
    private readonly List<string> trail = [];
    private ScreenFrame? frame;

    public IReadOnlyDictionary<string, long> Stats => stats;

    public object? RunPath(string path, IReadOnlyDictionary<string, object?>? variables = null) =>
        RunMacro(MacroSchema.Load(path), path, variables, null);

    private object? RunMacro(JsonObject macro, string path, IReadOnlyDictionary<string, object?>? variables,
        JsonObject? parentRuntime)
    {
        var runtime = Merge(Merge(options.BaseRuntime, parentRuntime), macro["runtime"] as JsonObject);
        var context = new Context(path, MacroSchema.Text(macro["name"]) is { Length: > 0 } name
            ? name
            : Path.GetFileNameWithoutExtension(path), runtime);
        foreach (var (key, value) in variables ?? new Dictionary<string, object?>()) context.Variables[key] = value;
        if (macro["regions"] is JsonObject regions)
        {
            foreach (var (key, value) in regions) context.Regions[key] = ToRegion((JsonObject)value!);
        }

        if (macro["routines"] is JsonObject routines)
        {
            foreach (var (key, value) in routines) context.Routines[key] = (JsonArray)value!;
        }

        trail.Add(context.Name);
        Log(MacroLogSource.Macro, $"매크로 시작: {context.Name}");
        try
        {
            RunSteps((JsonArray)macro["steps"]!, context);
            return null;
        }
        catch (MacroReturn signal)
        {
            return signal.Value;
        }
        finally
        {
            trail.RemoveAt(trail.Count - 1);
        }
    }

    private void RunSteps(JsonArray steps, Context context)
    {
        foreach (var node in steps)
        {
            cancellation.ThrowIfCancellationRequested();
            RunStep((JsonObject)node!, context);
        }
    }

    private void RunStep(JsonObject step, Context context)
    {
        cancellation.ThrowIfCancellationRequested();
        var action = MacroSchema.Text(step["action"]);
        Emit(new MacroStepEvent(DateTime.Now, string.Join(" › ", trail), action, Describe(step, context)));
        switch (action)
        {
            case "log":
                Log(MacroLogSource.Macro, Render(MacroSchema.Text(step["message"]), context));
                return;
            case "sleep":
                ScriptSleep(Number(step["seconds"]), Number(step["flex"]));
                return;
            case "update_screen":
                UpdateScreen();
                return;
            case "wait_update_screen":
                ScriptSleep(Number(step["seconds"]), null);
                UpdateScreen();
                return;
            case "tap_region":
                TouchRandomly(ResolveRegion(step["region"], context));
                return;
            case "tap_image":
            {
                // As ALAuto's find_and_touch: the mask option is not used for taps.
                var match = FindImage(MacroSchema.Text(step["image"]), Similarity(step), Flag(step["color"]), false, false);
                if (match is { } region) TouchRandomly(region);
                SetInto(step, context, match is not null);
                return;
            }
            case "tap_found_image":
            {
                var match = FindImage(MacroSchema.Text(step["image"]), Similarity(step), Flag(step["color"]),
                    Flag(step["interrupt_if_not_found"]), Flag(step["use_mask"]));
                if (match is { } region) TouchRandomly(region);
                SetInto(step, context, match is not null);
                return;
            }
            case "back":
                device.Back();
                ScriptSleep(null, null);
                return;
            case "swipe":
            {
                var from = (JsonObject)step["from"]!;
                var to = (JsonObject)step["to"]!;
                device.Swipe(MacroSchema.Int(from["x"]), MacroSchema.Int(from["y"]), MacroSchema.Int(to["x"]),
                    MacroSchema.Int(to["y"]), MacroSchema.Int(step["duration_ms"]));
                UpdateScreen();
                return;
            }
            case "set":
                context.Variables[MacroSchema.Text(step["var"])] = ResolveValue(step["value"], context);
                EmitVariables(context);
                return;
            case "increment":
            {
                var name = MacroSchema.Text(step["var"]);
                context.Variables[name] = MacroValues.ToLong(context.Variables.GetValueOrDefault(name)) +
                                          MacroValues.ToLong(ResolveValue(step["amount"], context));
                EmitVariables(context);
                return;
            }
            case "stats_increment":
            {
                var method = MacroSchema.Text(step["method"]);
                var key = method.StartsWith("increment_", StringComparison.Ordinal) ? method["increment_".Length..] : method;
                stats[key] = stats.GetValueOrDefault(key) + 1;
                return;
            }
            case "print_stats":
                PrintStats(step, context);
                return;
            case "if":
                if (Evaluate(step["condition"], context)) RunSteps((JsonArray)step["steps"]!, context);
                else if (step["else_steps"] is JsonArray elseSteps) RunSteps(elseSteps, context);
                return;
            case "while":
            {
                var limit = step.ContainsKey("max_iterations") ? MacroSchema.Int(step["max_iterations"]) : 1000;
                var iterations = 0;
                while (Evaluate(step["condition"], context))
                {
                    cancellation.ThrowIfCancellationRequested();
                    if (iterations >= limit)
                    {
                        Log(MacroLogSource.Macro, $"반복이 최대 {limit}회에 닿아 빠져나옵니다.");
                        break;
                    }

                    iterations++;
                    try
                    {
                        RunSteps((JsonArray)step["steps"]!, context);
                    }
                    catch (MacroContinue)
                    {
                    }
                    catch (MacroBreak)
                    {
                        break;
                    }
                }

                return;
            }
            case "repeat":
            {
                var times = step.ContainsKey("times") ? MacroSchema.Int(step["times"]) : 1;
                for (var index = 0; index < times; index++)
                {
                    cancellation.ThrowIfCancellationRequested();
                    try
                    {
                        RunSteps((JsonArray)step["steps"]!, context);
                    }
                    catch (MacroContinue)
                    {
                    }
                    catch (MacroBreak)
                    {
                        break;
                    }
                }

                return;
            }
            case "wait_for":
            {
                var timeout = Number(step["timeout"]) ?? 10;
                var poll = Number(step["poll_seconds"]) ?? 0.5;
                var started = DateTime.UtcNow;
                while ((DateTime.UtcNow - started).TotalSeconds <= timeout)
                {
                    cancellation.ThrowIfCancellationRequested();
                    UpdateScreen();
                    if (Evaluate(step["condition"], context))
                    {
                        SetInto(step, context, true);
                        return;
                    }

                    Wait(TimeSpan.FromSeconds(poll));
                }

                SetInto(step, context, false);
                if (step["timeout_steps"] is JsonArray timeoutSteps) RunSteps(timeoutSteps, context);
                return;
            }
            case "call":
            {
                var routine = MacroSchema.Text(step["routine"]);
                if (!context.Routines.TryGetValue(routine, out var steps))
                    throw new MacroSchemaException($"Unknown routine '{routine}'.");
                object? result = null;
                try
                {
                    RunSteps(steps, context);
                }
                catch (MacroReturn signal)
                {
                    result = signal.Value;
                }

                SetInto(step, context, result);
                return;
            }
            case "run_macro":
            {
                var path = ResolveMacroPath(MacroSchema.Text(step["path"]), context);
                var variables = new Dictionary<string, object?>(StringComparer.Ordinal);
                if (step["variables"] is JsonObject passed)
                {
                    foreach (var (key, value) in passed) variables[key] = ResolveValue(value, context);
                }

                var result = RunMacro(MacroSchema.Load(path), path, variables, context.Runtime);
                SetInto(step, context, result);
                return;
            }
            case "plugin":
                SetInto(step, context, RunPlugin(step, context));
                return;
            case "break":
                throw new MacroBreak();
            case "continue":
                throw new MacroContinue();
            case "return":
                throw new MacroReturn(ResolveValue(step["value"], context));
            default:
                throw new MacroSchemaException($"Unsupported action '{action}'.");
        }
    }

    private bool Evaluate(JsonNode? condition, Context context)
    {
        if (condition is not JsonObject spec) return MacroValues.Truthy(MacroValues.FromJson(condition));
        switch (MacroSchema.Text(spec["type"]))
        {
            case "all":
                return spec["items"] is not JsonArray all || all.All(item => Evaluate(item, context));
            case "any":
                return spec["items"] is JsonArray any && any.Any(item => Evaluate(item, context));
            case "not":
                return !Evaluate(spec["item"], context);
            case "always":
                return !spec.ContainsKey("value") || MacroValues.Truthy(MacroValues.FromJson(spec["value"]));
            case "image":
            {
                var match = FindImage(MacroSchema.Text(spec["image"]), Similarity(spec), Flag(spec["color"]),
                    Flag(spec["interrupt_if_not_found"]), Flag(spec["use_mask"]));
                if (match is not { } region) return false;
                return Between(spec["x_between"], region.X) && Between(spec["y_between"], region.Y);
            }
            case "compare":
            {
                var source = MacroSchema.Text(spec["source"]);
                var key = MacroSchema.Text(spec["key"]);
                var left = source switch
                {
                    "var" => context.Variables.GetValueOrDefault(key),
                    "runtime" => ResolveRuntime(key, context),
                    "game" => options.Game.Get(key),
                    _ => null
                };
                var op = spec.ContainsKey("op") ? MacroSchema.Text(spec["op"]) : "truthy";
                var right = ResolveValue(spec["value"], context);
                var result = MacroValues.Compare(left, op, right);
                if (source == "game")
                {
                    Log(MacroLogSource.Packet, op == "truthy"
                        ? $"{key} = {MacroValues.Format(left)} → {(result ? "참" : "거짓")}"
                        : $"{key} {MacroValues.Format(left)} {OperatorText(op)} {MacroValues.Format(right)} → {(result ? "참" : "거짓")}");
                }

                return result;
            }
            case "region_color":
            {
                EnsureScreen();
                var (h, s, v) = frame!.AverageHsv(ToRegion((JsonObject)spec["region"]!));
                int[] color = [h, s, v];
                if (spec["match"] is JsonObject match)
                {
                    var low = (JsonArray)match["low"]!;
                    var high = (JsonArray)match["high"]!;
                    return Enumerable.Range(0, 3).All(index =>
                        MacroSchema.Int(low[index]) <= color[index] && color[index] <= MacroSchema.Int(high[index]));
                }

                var channel = Math.Clamp(MacroSchema.Int(spec["channel"]), 0, 2);
                var target = MacroValues.ToDouble(MacroValues.FromJson(spec["value"]) ?? 0L);
                return MacroSchema.Text(spec["op"]) switch
                {
                    "gt" => color[channel] > target,
                    "lt" => color[channel] < target,
                    "gte" => color[channel] >= target,
                    "lte" => color[channel] <= target,
                    "equals" => color[channel] == target,
                    _ => false
                };
            }
            case "plugin":
                return MacroValues.Truthy(RunPlugin(spec, context));
            default:
                return false;
        }
    }

    /// <summary>ALAuto's Utils.find, including the retry-then-interrupt path of interrupt_if_not_found.</summary>
    private ScreenRegion? FindImage(string image, double similarity, bool useColor, bool interrupt, bool useMask)
    {
        EnsureScreen();
        var library = Library(LibraryRoot());
        if (!library.Exists(image))
        {
            Log(MacroLogSource.Screen, $"에셋 없음: {image}");
            return null;
        }

        var match = Match(library, image, similarity, useColor, useMask);
        if (match is not null || !interrupt) return match;

        for (var attempt = 1; attempt < 3; attempt++)
        {
            ScriptSleep(0.5, null);
            UpdateScreen();
            match = Match(library, image, similarity, useColor, useMask);
            if (match is not null) return match;
        }

        Log(MacroLogSource.Screen, $"{image}을 3번 찾지 못해 뒤로 가기 후 화면 가운데를 누릅니다.");
        device.Back();
        ScriptSleep(null, null);
        TouchRandomly(new ScreenRegion(860, 440, 200, 200));
        UpdateScreen();
        return Match(library, image, similarity, useColor, useMask);
    }

    private ScreenRegion? Match(TemplateLibrary library, string image, double similarity, bool useColor, bool useMask)
    {
        var match = library.Find(frame!, image, similarity, useColor, useMask);
        if (match is not { Found: true } found) return null;
        Log(MacroLogSource.Screen, string.Create(CultureInfo.InvariantCulture, $"{image} {found.Score:0.00} 찾음"));
        Emit(new MacroScreenEvent(DateTime.Now, frame!.Png, found.Region,
            string.Create(CultureInfo.InvariantCulture, $"{image} {found.Score:0.00}")));
        return found.Region;
    }

    private void TouchRandomly(ScreenRegion region)
    {
        var x = options.Random.Next(region.X, region.X + region.W + 1);
        var y = options.Random.Next(region.Y, region.Y + region.H + 1);
        device.Tap(x, y, options.Random.Next(50, 121));
        Log(MacroLogSource.Screen, string.Create(CultureInfo.InvariantCulture, $"탭 {x},{y}"));
        ScriptSleep(null, null);
    }

    private void UpdateScreen()
    {
        var png = device.Capture();
        var next = ScreenFrame.FromPng(png);
        frame?.Dispose();
        frame = next;
        Emit(new MacroScreenEvent(DateTime.Now, png, null, ""));
    }

    private void EnsureScreen()
    {
        if (frame is null) UpdateScreen();
    }

    // utils.script_sleep: 0.4-0.7 s by default, else between base and base + flex (flex defaults to base).
    private void ScriptSleep(double? seconds, double? flex)
    {
        var duration = seconds is { } basis
            ? basis + options.Random.NextDouble() * Math.Max(0, flex ?? basis)
            : 0.4 + options.Random.NextDouble() * 0.3;
        Wait(TimeSpan.FromSeconds(duration));
    }

    private void Wait(TimeSpan duration)
    {
        if (options.Sleep is { } sleep)
        {
            sleep(duration, cancellation);
        }
        else
        {
            cancellation.WaitHandle.WaitOne(duration);
        }

        cancellation.ThrowIfCancellationRequested();
    }

    private object? ResolveValue(JsonNode? value, Context context)
    {
        if (value is JsonObject reference && reference.ContainsKey("ref"))
        {
            var key = MacroSchema.Text(reference["key"]);
            return MacroSchema.Text(reference["ref"]) switch
            {
                "var" => context.Variables.GetValueOrDefault(key),
                "runtime" => ResolveRuntime(key, context),
                "stats" => stats.TryGetValue(key, out var count) ? count : null,
                "game" => options.Game.Get(key),
                _ => null
            };
        }

        return MacroValues.FromJson(value);
    }

    private static object? ResolveRuntime(string key, Context context)
    {
        JsonNode? current = context.Runtime;
        foreach (var part in key.Split('.'))
        {
            if (current is not JsonObject map || !map.TryGetPropertyValue(part, out current)) return null;
        }

        return MacroValues.FromJson(current);
    }

    private ScreenRegion ResolveRegion(JsonNode? region, Context context)
    {
        if (region is JsonValue && context.Regions.TryGetValue(MacroSchema.Text(region), out var named)) return named;
        if (region is JsonObject or JsonArray) return ToRegion(MacroSchema.NormalizeRegion(region, "region"));
        throw new MacroSchemaException($"Unknown region '{MacroSchema.Text(region)}'.");
    }

    private string ResolveMacroPath(string path, Context context)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(context.Path));
        if (directory is not null)
        {
            var candidate = Path.GetFullPath(Path.Combine(directory, path));
            if (File.Exists(candidate)) return candidate;
        }

        return Path.GetFullPath(Path.Combine(options.AlautoRoot, path));
    }

    // Like ALAuto, the asset set comes from config.json's assets.server once for the whole run.
    private string LibraryRoot()
    {
        var server = options.BaseRuntime["assets"] is JsonObject assets ? MacroSchema.Text(assets["server"]) : "";
        return Path.Combine(options.AlautoRoot, "assets",
            string.IsNullOrWhiteSpace(server) ? "KR" : server.Trim().ToUpperInvariant());
    }

    private TemplateLibrary Library(string root)
    {
        if (!libraries.TryGetValue(root, out var library)) libraries[root] = library = new TemplateLibrary(root);
        return library;
    }

    private double Similarity(JsonObject spec) => Number(spec["similarity"]) ?? options.DefaultSimilarity;

    private static bool Between(JsonNode? bounds, int value) =>
        bounds is not JsonArray { Count: 2 } pair ||
        MacroValues.ToDouble(MacroValues.FromJson(pair[0]) ?? 0L) < value && value < MacroValues.ToDouble(MacroValues.FromJson(pair[1]) ?? 0L);

    private static bool Flag(JsonNode? node) => MacroValues.Truthy(MacroValues.FromJson(node));

    private static double? Number(JsonNode? node) =>
        MacroValues.FromJson(node) is { } value && MacroValues.IsNumber(value) ? MacroValues.ToDouble(value) : null;

    private static void SetInto(JsonObject step, Context context, object? value)
    {
        if (step["into"] is { } into) context.Variables[MacroSchema.Text(into)] = value;
    }

    private static ScreenRegion ToRegion(JsonObject region) =>
        new(MacroSchema.Int(region["x"]), MacroSchema.Int(region["y"]), MacroSchema.Int(region["w"]), MacroSchema.Int(region["h"]));

    private static JsonObject Merge(JsonObject? parent, JsonObject? local)
    {
        var merged = (JsonObject?)parent?.DeepClone() ?? new JsonObject();
        if (local is null) return merged;
        foreach (var (key, value) in local)
        {
            merged[key] = value is JsonObject child && merged[key] is JsonObject existing
                ? Merge(existing, child)
                : value?.DeepClone();
        }

        return merged;
    }

    private static string Render(string message, Context context)
    {
        foreach (var (name, value) in context.Variables)
            message = message.Replace("{{" + name + "}}", MacroValues.Format(value), StringComparison.Ordinal);
        return message;
    }

    private string Describe(JsonObject step, Context context)
    {
        string Seconds(JsonNode? node) => (Number(node) ?? 0).ToString("0.#", CultureInfo.InvariantCulture);
        return MacroSchema.Text(step["action"]) switch
        {
            "log" => Render(MacroSchema.Text(step["message"]), context),
            "sleep" => $"{Seconds(step["seconds"])}초 쉬는 중",
            "update_screen" => "화면 확인",
            "wait_update_screen" => "잠시 뒤 화면 확인",
            "tap_region" => $"영역 누르기: {MacroSchema.Text(step["region"])}",
            "tap_image" => $"이미지 누르기: {MacroSchema.Text(step["image"])}",
            "tap_found_image" => $"찾으면 누르기: {MacroSchema.Text(step["image"])}",
            "back" => "뒤로 가기",
            "swipe" => "화면 밀기",
            "set" or "increment" => $"{MacroSchema.Text(step["var"])} 갱신",
            "if" => $"확인: {DescribeCondition(step["condition"])}",
            "while" => $"반복 조건: {DescribeCondition(step["condition"])}",
            "repeat" => $"{MacroSchema.Int(step["times"])}번 반복",
            "wait_for" => $"기다리는 중: {DescribeCondition(step["condition"])} (최대 {Seconds(step["timeout"] ?? 10)}초)",
            "call" => $"루틴: {MacroSchema.Text(step["routine"])}",
            "run_macro" => $"매크로 실행: {Path.GetFileNameWithoutExtension(MacroSchema.Text(step["path"]))}",
            "plugin" => $"플러그인: {MacroSchema.Text(step["plugin"])}",
            "stats_increment" or "print_stats" => "통계",
            "break" => "반복 끝내기",
            "continue" => "다음 반복으로",
            "return" => "매크로 끝내기",
            var other => other
        };
    }

    public static string DescribeCondition(JsonNode? condition)
    {
        if (condition is not JsonObject spec) return MacroValues.Format(MacroValues.FromJson(condition));
        return MacroSchema.Text(spec["type"]) switch
        {
            "all" => string.Join(" 그리고 ", ((spec["items"] as JsonArray) ?? []).Select(DescribeCondition)),
            "any" => string.Join(", ", ((spec["items"] as JsonArray) ?? []).Select(DescribeCondition)),
            "not" => $"아님({DescribeCondition(spec["item"])})",
            "always" => "항상",
            "image" => MacroSchema.Text(spec["image"]),
            "compare" => spec.ContainsKey("value")
                ? $"{MacroSchema.Text(spec["key"])} {OperatorText(MacroSchema.Text(spec["op"]))} {DescribeValue(spec["value"])}"
                : MacroSchema.Text(spec["key"]),
            "region_color" => "영역 색",
            "plugin" => MacroSchema.Text(spec["plugin"]),
            var other => other
        };
    }

    private static string DescribeValue(JsonNode? value) =>
        value is JsonObject reference && reference.ContainsKey("ref")
            ? MacroSchema.Text(reference["key"])
            : MacroValues.Format(MacroValues.FromJson(value));

    private static string OperatorText(string op) => op switch
    {
        "equals" => "=",
        "not_equals" => "≠",
        "gt" => ">",
        "gte" => "≥",
        "lt" => "<",
        "lte" => "≤",
        _ => op
    };

    private void Log(MacroLogSource source, string message) => Emit(new MacroLogEvent(DateTime.Now, source, message));

    private void EmitVariables(Context context) =>
        Emit(new MacroVariablesEvent(DateTime.Now, new Dictionary<string, object?>(context.Variables, StringComparer.Ordinal)));

    private void Emit(MacroEvent macroEvent) => options.OnEvent?.Invoke(macroEvent);

    public void Dispose()
    {
        frame?.Dispose();
        frame = null;
        foreach (var library in libraries.Values) library.Dispose();
        libraries.Clear();
    }

    private sealed class Context(string path, string name, JsonObject runtime)
    {
        public string Path { get; } = path;
        public string Name { get; } = name;
        public JsonObject Runtime { get; } = runtime;
        public Dictionary<string, object?> Variables { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, ScreenRegion> Regions { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, JsonArray> Routines { get; } = new(StringComparer.Ordinal);
    }

    private sealed class MacroBreak : Exception;

    private sealed class MacroContinue : Exception;

    private sealed class MacroReturn(object? value) : Exception
    {
        public object? Value { get; } = value;
    }
}
