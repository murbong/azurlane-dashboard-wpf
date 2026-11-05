using System.Text.Json.Nodes;
using AzurLaneDashboard.Automation.Device;
using AzurLaneDashboard.Automation.Engine;
using AzurLaneDashboard.Automation.Vision;
using OpenCvSharp;

// Self-contained: synthetic screens and assets in a temp ALAuto folder, a fake device and fake game state.
var root = Path.Combine(Path.GetTempPath(), $"azurlane-automation-checks-{Guid.NewGuid():N}");
try
{
    Fixture.Create(root);
    TemplateChecks.Run(root);
    SchemaChecks.Run();
    CatalogChecks.Run(root);
    RunnerChecks.Run(root);
    DormChecks.Run(root);
    Console.WriteLine("Automation matching, macro schema, catalog, runner and plugin checks passed.");
}
finally
{
    try { Directory.Delete(root, recursive: true); } catch (IOException) { }
}

static class Check
{
    public static void That(bool condition, string description)
    {
        if (!condition) throw new InvalidOperationException(description);
    }
}

/// <summary>A temp ALAuto folder: noise-pattern assets, a screen holding them, and macros like ALAuto's.</summary>
static class Fixture
{
    public static readonly OpenCvSharp.Point RepeatAt = new(1500, 900);
    public static readonly OpenCvSharp.Point BadgeAt = new(300, 200);

    // The same seed always draws the same pattern, so the asset and the screen hold identical pixels.
    public static Mat Pattern(int width, int height, int seed)
    {
        var pixels = new byte[width * height * 3];
        new Random(seed).NextBytes(pixels);
        using var data = Mat.FromPixelData(height, width, MatType.CV_8UC3, pixels);
        var mat = data.Clone();
        Cv2.PutText(mat, seed.ToString(), new OpenCvSharp.Point(4, height - 6), HersheyFonts.HersheySimplex, 0.6, Scalar.White, 2);
        return mat;
    }

    public static byte[] Screen(bool showRepeat)
    {
        using var screen = new Mat(1080, 1920, MatType.CV_8UC3, new Scalar(40, 30, 20));
        Cv2.Randu(screen, new Scalar(0, 0, 0), new Scalar(60, 60, 60));
        using var badge = Pattern(90, 50, 7);
        badge.CopyTo(new Mat(screen, new Rect(BadgeAt.X, BadgeAt.Y, badge.Width, badge.Height)));
        if (showRepeat)
        {
            using var repeat = Pattern(111, 57, 3);
            repeat.CopyTo(new Mat(screen, new Rect(RepeatAt.X, RepeatAt.Y, repeat.Width, repeat.Height)));
        }

        return screen.ImEncode(".png");
    }

    public static void Create(string root)
    {
        var assets = Path.Combine(root, "assets", "KR", "combat");
        Directory.CreateDirectory(assets);
        Directory.CreateDirectory(Path.Combine(root, "macros"));
        using (var repeat = Pattern(111, 57, 3)) Cv2.ImWrite(Path.Combine(assets, "repeat.png"), repeat);
        using (var badge = Pattern(90, 50, 7))
        {
            // Transparent corners: only the opaque middle should count with use_mask.
            using var alpha = new Mat(badge.Rows, badge.Cols, MatType.CV_8UC1, Scalar.All(0));
            Cv2.Rectangle(alpha, new Rect(10, 8, 70, 34), Scalar.All(255), -1);
            var channels = Cv2.Split(badge).Append(alpha).ToArray();
            using var withAlpha = new Mat();
            Cv2.Merge(channels, withAlpha);
            Cv2.ImWrite(Path.Combine(assets, "badge.png"), withAlpha);
            foreach (var channel in channels) channel.Dispose();
        }

        File.WriteAllText(Path.Combine(root, "config.json"), """{ "assets": { "server": "KR" } }""");
        File.WriteAllText(Path.Combine(root, "macros", "combat.json"), """
            {
              "name": "combat",
              "steps": [
                { "action": "wait_for", "condition": { "image": "combat/repeat" }, "timeout": 0.05, "poll_seconds": 1 },
                { "action": "tap_image", "image": "combat/repeat", "into": "tapped" },
                { "action": "if", "condition": { "var": "tapped" },
                  "steps": [ { "action": "return", "value": true } ], "else_steps": [ { "action": "return", "value": "waiting" } ] }
              ]
            }
            """);
        // The same structure as ALAuto's macros/leveling_loop.json: only real taps on 반복 count, and battles are
        // checked between two taps.
        File.WriteAllText(Path.Combine(root, "macros", "loop.json"), """
            {
              "name": "loop",
              "ui": { "stops": [ { "text": "성장 중인 쩔이 줄어들면", "detail": "지금 {{game:leveling.growing}}척" } ] },
              "steps": [
                { "action": "if", "condition": { "not": { "game": "live" } }, "steps": [ { "action": "return", "value": false } ] },
                { "action": "if", "condition": { "not": { "var": "max_cycles" } }, "steps": [ { "action": "set", "var": "max_cycles", "value": 20 } ] },
                { "action": "set", "var": "cycles", "value": 0 },
                { "action": "set", "var": "waits", "value": 0 },
                { "action": "set", "var": "growing_at_start", "value": { "game": "leveling.growing" } },
                { "action": "set", "var": "last_battle", "value": { "game": "battle.last_id" } },
                { "action": "while", "condition": { "var": "cycles", "lt": { "var": "max_cycles" } }, "steps": [
                  { "action": "run_macro", "path": "combat.json", "into": "combat_ok" },
                  { "action": "if", "condition": { "var": "combat_ok", "equals": false }, "steps": [ { "action": "return", "value": false } ] },
                  { "action": "if", "condition": { "var": "combat_ok", "not_equals": true }, "steps": [
                    { "action": "increment", "var": "waits" },
                    { "action": "if", "condition": { "var": "waits", "gte": 6 }, "steps": [ { "action": "return", "value": false } ] },
                    { "action": "continue" }
                  ] },
                  { "action": "set", "var": "waits", "value": 0 },
                  { "action": "increment", "var": "cycles" },
                  { "action": "if", "condition": { "all": [ { "var": "cycles", "gt": 1 }, { "game": "battle.last_id", "lte": { "var": "last_battle" } } ] },
                    "steps": [ { "action": "return", "value": false } ] },
                  { "action": "set", "var": "last_battle", "value": { "game": "battle.last_id" } },
                  { "action": "log", "message": "반복 {{cycles}}회" },
                  { "action": "if", "condition": { "game": "leveling.growing", "lt": { "var": "growing_at_start" } }, "steps": [ { "action": "return", "value": true } ] }
                ] },
                { "action": "return", "value": true }
              ]
            }
            """);
        File.WriteAllText(Path.Combine(root, "macros", "hq.json"), """
            { "name": "hq", "steps": [ { "action": "plugin", "plugin": "headquarters.unknown_room" } ] }
            """);
        // ALAuto's headquarters regions for the courtyard and its snack menu.
        File.WriteAllText(Path.Combine(root, "macros", "dorm.json"), """
            {
              "name": "dorm",
              "runtime": { "dorm": { "AvailableSupplies": [20000, 10000, 5000, 3000, 2000, 1000] } },
              "regions": {
                "supplies_bar": { "x": 345, "y": 980, "w": 215, "h": 65 },
                "exit_snacks_menu": { "x": 900, "y": 880, "w": 380, "h": 135 },
                "confirm_dorm_summary": { "x": 1545, "y": 905, "w": 235, "h": 65 },
                "ignore_give_food_button": { "x": 690, "y": 750, "w": 185, "h": 60 },
                "snack_1": { "x": 620, "y": 600, "w": 100, "h": 100 },
                "snack_2": { "x": 815, "y": 600, "w": 100, "h": 100 },
                "snack_3": { "x": 1010, "y": 600, "w": 100, "h": 100 },
                "snack_4": { "x": 1205, "y": 600, "w": 100, "h": 100 },
                "snack_5": { "x": 1400, "y": 600, "w": 100, "h": 100 },
                "snack_6": { "x": 1595, "y": 600, "w": 100, "h": 100 }
              },
              "steps": [
                { "action": "plugin", "plugin": "headquarters.refill_dorm", "initial_sleep": 0, "into": "fed" },
                { "action": "print_stats", "oil_limit": 20000 },
                { "action": "return", "value": { "var": "fed" } }
              ]
            }
            """);
        File.WriteAllText(Path.Combine(root, "macros", "main.json"), """
            { "name": "main", "steps": [ { "action": "run_macro", "path": "hq.json" } ] }
            """);
    }
}

static class TemplateChecks
{
    public static void Run(string root)
    {
        using var library = new TemplateLibrary(Path.Combine(root, "assets", "KR"));
        using var frame = ScreenFrame.FromPng(Fixture.Screen(showRepeat: true));
        var repeat = library.Find(frame, "combat/repeat", 0.95);
        Check.That(repeat is { Found: true } && repeat.Value.Region == new ScreenRegion(1500, 900, 111, 57) && repeat.Value.Score > 0.99,
            "grayscale match finds the template where it was drawn");
        Check.That(library.Find(frame, "combat/repeat", 0.95, useColor: true) is { Found: true }, "colour match finds it too");
        var badge = library.Find(frame, "combat/badge", 0.95, useMask: true);
        Check.That(badge is { Found: true } && badge.Value.Region.X == Fixture.BadgeAt.X && badge.Value.Region.Y == Fixture.BadgeAt.Y,
            "masked match uses the alpha channel");
        // A plain area of similar brightness: TM_CCORR_NORMED called this a match (the 재출격 false alarm).
        using (var plain = new Mat(1080, 1920, MatType.CV_8UC3))
        {
            Cv2.Randu(plain, new Scalar(100, 100, 100), new Scalar(140, 140, 140));
            using var plainFrame = ScreenFrame.FromPng(plain.ImEncode(".png"));
            Check.That(library.Find(plainFrame, "combat/badge", 0.9, useMask: true) is { Found: false },
                "a masked template does not match an unrelated area of the same brightness");
        }

        using (var flat = new Mat(1080, 1920, MatType.CV_8UC3, new Scalar(128, 128, 128)))
        {
            using var flatFrame = ScreenFrame.FromPng(flat.ImEncode(".png"));
            Check.That(library.Find(flatFrame, "combat/badge", 0.9, useMask: true) is { Found: false } &&
                       library.Find(flatFrame, "combat/repeat", 0.9) is { Found: false },
                "a flat screen (division by zero) is no match");
        }
        using var empty = ScreenFrame.FromPng(Fixture.Screen(showRepeat: false));
        Check.That(library.Find(empty, "combat/repeat", 0.95) is { Found: false }, "a missing button is not found");
        Check.That(library.Find(frame, "combat/nothing", 0.95) is null, "a missing asset reads as no match");
        // MuMu 12 prints a multi-display warning before the PNG on stdout.
        var warned = System.Text.Encoding.ASCII.GetBytes("[Warning] Multiple displays were found, but no display id was specified!\n")
            .Concat(Fixture.Screen(showRepeat: true)).ToArray();
        using (var stripped = ScreenFrame.FromPng(AdbDevice.StripToPng(warned)))
        {
            Check.That(stripped.Width == 1920 && stripped.Height == 1080, "a warning before the PNG is dropped");
        }

        Check.That(AdbDevice.StripToPng(frame.Png).Length == frame.Png.Length, "a clean PNG is left as it is");

        // MuMu 12 (as captured): the game on logical display 2, the launcher on 0, a global ResumedActivity line after display 3.
        const string activities = """
            Display #2 (activities from top to bottom):
                topResumedActivity=ActivityRecord{160742536 u0 kr.txwy.and.blhx/com.manjuu.azurlane.MainActivity t9}
            Display #0 (activities from top to bottom):
                  topResumedActivity=ActivityRecord{253722711 u0 app.lawnchair/.LawnchairLauncher t2}
            Display #3 (activities from top to bottom):
              ResumedActivity: ActivityRecord{160742536 u0 kr.txwy.and.blhx/com.manjuu.azurlane.MainActivity t9}
            """;
        const string displays = """
                mDisplayId=0
                mPrimaryDisplayDevice=기본으로 제공되는 화면(local:4619827820427265280)
                mDisplayId=2
                mPrimaryDisplayDevice=mumuscreen001(local:4619827767814508545)
                mDisplayId=3
                mPrimaryDisplayDevice=mumuscreen002(local:4619826888814064386)
            """;
        Check.That(AdbDevice.FindGameDisplay(activities, () => displays) == new GameDisplay(2, "4619827767814508545", "mumuscreen001"),
            "the game's own display is found with the physical id screencap needs");
        const string onDefault = """
            Display #0 (activities from top to bottom):
              topResumedActivity=ActivityRecord{1 u0 com.YoStarEN.AzurLane/.Main t1}
            """;
        Check.That(AdbDevice.FindGameDisplay(onDefault, () => throw new InvalidOperationException("display 0 needs no lookup")) ==
                   new GameDisplay(0, "", ""), "a game on the default display needs no display arguments");
        const string noGame = """
            Display #0 (activities from top to bottom):
              topResumedActivity=ActivityRecord{1 u0 app.lawnchair/.Launcher t2}
            """;
        Check.That(AdbDevice.FindGameDisplay(noGame, () => displays) is null, "no game, no display");
        var hsv = frame.AverageHsv(new ScreenRegion(0, 0, 10, 10));
        Check.That(hsv.V <= 60, "region colour average reads the screen");
    }
}

static class SchemaChecks
{
    public static void Run()
    {
        var condition = MacroSchema.NormalizeCondition(JsonNode.Parse("""{ "game": "battle.last_id", "gt": { "var": "mark" } }"""), "test");
        Check.That(JsonNode.DeepEquals(condition, JsonNode.Parse(
                """{ "type": "compare", "source": "game", "key": "battle.last_id", "op": "gt", "value": { "ref": "var", "key": "mark" } }""")),
            "game shorthand normalizes like ALAuto's schema.py");
        Check.That(JsonNode.DeepEquals(MacroSchema.NormalizeCondition(JsonNode.Parse("""{ "feature": "combat" }"""), "test"),
                JsonNode.Parse("""{ "type": "compare", "source": "runtime", "key": "combat.enabled", "op": "truthy" }""")),
            "feature shorthand reads runtime <name>.enabled");
        var macro = MacroSchema.NormalizeMacro(JsonNode.Parse("""
            { "regions": { "a": [1, 2, 3, 4] },
              "steps": [ { "action": "swipe", "x1": 1, "y1": 2, "x2": 3, "y2": 4, "ms": 300 }, { "action": "increment", "name": "n" } ] }
            """));
        Check.That(JsonNode.DeepEquals(macro["regions"]!["a"], JsonNode.Parse("""{ "x": 1, "y": 2, "w": 3, "h": 4 }""")),
            "list regions become objects");
        Check.That(JsonNode.DeepEquals(macro["steps"]![0], JsonNode.Parse(
                """{ "action": "swipe", "from": { "x": 1, "y": 2 }, "to": { "x": 3, "y": 4 }, "duration_ms": 300 }""")),
            "swipe coordinates normalize to from/to");
        Check.That(MacroSchema.Text(macro["steps"]![1]!["var"]) == "n" && MacroSchema.Int(macro["steps"]![1]!["amount"]) == 1,
            "increment takes name and a default amount");
        try
        {
            MacroSchema.NormalizeMacro(JsonNode.Parse("""{ "steps": [ { "action": "fly" } ] }"""));
            Check.That(false, "unknown actions are rejected");
        }
        catch (MacroSchemaException)
        {
        }

        Check.That(MacroValues.Compare(null, "gt", 3L) == false && MacroValues.Compare(5L, "gt", null) == false,
            "ordering with a missing side is false");
        Check.That(MacroValues.Compare(5L, "gte", 5.0) && MacroValues.Compare("S", "equals", "S") && !MacroValues.Compare(0L, "truthy", null),
            "numbers compare across int and float; truthy follows Python");
    }
}

static class CatalogChecks
{
    public static void Run(string root)
    {
        var macros = MacroCatalog.Scan(root).ToDictionary(info => info.FileName);
        var loop = macros["loop.json"];
        Check.That(loop.UsesPackets && loop.UsesScreen && loop.HasMaxCycles && loop.IsSupported,
            "the loop uses packets and the screen (through the macro it runs) and takes max_cycles");
        Check.That(loop.Stops.Count == 1 && loop.Stops[0].Detail.Contains("{{game:leveling.growing}}"), "ui.stops are read");
        Check.That(!macros["hq.json"].IsSupported && macros["hq.json"].UnsupportedReason.Contains("headquarters.unknown_room"),
            "a plugin the runner does not have is named");
        Check.That(!macros["main.json"].IsSupported, "a macro that runs such a macro is marked too");
        Check.That(macros["dorm.json"].IsSupported, "the headquarters plugins are supported");
        Check.That(!macros["combat.json"].UsesPackets && !macros["combat.json"].HasMaxCycles, "screen-only macro");
    }
}

static class RunnerChecks
{
    public static void Run(string root)
    {
        var finished = Play(root, new FakeGame { Growing = 2, StopsAfter = 3 }, null, out var device);
        Check.That(finished is true && device.Taps == 3, "stops when a farming ship reaches its next stop");

        // The first tap may start the very first sortie, so the battle check begins with the second.
        finished = Play(root, new FakeGame { RecordsBattles = false }, null, out device);
        Check.That(finished is false && device.Taps == 2, "stops when no battle result arrives between two repeats");

        var hidden = new FakeDevice(new FakeGame()) { ShowsRepeat = false };
        using (var waiting = new MacroRunner(hidden, Options(root, new FakeGame(), null), CancellationToken.None))
        {
            finished = waiting.RunPath(Path.Combine(root, "macros", "loop.json"));
        }

        Check.That(finished is false && hidden.Taps == 0 && hidden.Captures >= 6,
            "waits that never find 반복 are not cycles, and six of them stop the run");

        finished = Play(root, new FakeGame(), new Dictionary<string, object?> { ["max_cycles"] = 5L }, out device);
        Check.That(finished is true && device.Taps == 5, "the dashboard's 최대 반복 replaces the default");

        finished = Play(root, new FakeGame { Live = false }, null, out device);
        Check.That(finished is false && device.Taps == 0, "refuses to start without live packet state");

        var events = new List<MacroEvent>();
        using var cancel = new CancellationTokenSource();
        var game = new FakeGame();
        var stopping = new FakeDevice(game) { OnTap = () => { if (game.Battles == 2) cancel.Cancel(); } };
        using var runner = new MacroRunner(stopping, Options(root, game, events.Add), cancel.Token);
        try
        {
            runner.RunPath(Path.Combine(root, "macros", "loop.json"));
            Check.That(false, "a cancelled run throws");
        }
        catch (OperationCanceledException)
        {
        }

        Check.That(stopping.Taps == 2, "stop takes effect at the next step");
        Check.That(events.OfType<MacroLogEvent>().Any(entry => entry.Source == MacroLogSource.Packet && entry.Message.StartsWith("live = true")),
            "game conditions are logged as packet lines");
        Check.That(events.OfType<MacroScreenEvent>().Any(screen => screen.Match is { X: 1500, Y: 900 }),
            "found images reach the screen preview with their region");
        Check.That(events.OfType<MacroStepEvent>().Any(step => step.Trail == "loop › combat" && step.Description.StartsWith("기다리는 중: combat/repeat")),
            "steps carry the macro trail and a description");
        var variables = events.OfType<MacroVariablesEvent>().Last().Variables;
        Check.That(variables["cycles"] is 1L or 2L, "variables are reported for the status panel");
    }

    private static object? Play(string root, FakeGame game, IReadOnlyDictionary<string, object?>? variables, out FakeDevice device)
    {
        device = new FakeDevice(game);
        using var runner = new MacroRunner(device, Options(root, game, null), CancellationToken.None);
        return runner.RunPath(Path.Combine(root, "macros", "loop.json"), variables);
    }

    private static MacroRunOptions Options(string root, IGameState game, Action<MacroEvent>? onEvent) => new()
    {
        AlautoRoot = root,
        BaseRuntime = JsonNode.Parse(File.ReadAllText(Path.Combine(root, "config.json")))!.AsObject(),
        Game = game,
        OnEvent = onEvent,
        Random = new Random(7),
        Sleep = (_, token) => token.ThrowIfCancellationRequested()
    };
}

static class DormChecks
{
    private const int Capacity = 44000;

    public static void Run(string root)
    {
        // Packets: 10,000 of 44,000 is under half, so feed up to 90% (39,600) with the largest snack that fits.
        // 3,000 shows on screen but the packets count none left; 20,000 is greyed out.
        var game = new DormGame
        {
            Food = 10000,
            Snacks = { [1000] = 50, [2000] = 50, [3000] = 0, [5000] = 5, [10000] = 2, [20000] = 0 }
        };
        var events = new List<MacroEvent>();
        var device = new DormDevice(food: 10000, shown: [true, true, true, true, true, false]);
        var fed = Play(root, device, game, events);
        Check.That(fed is true, "refill_dorm reports that it fed");
        Check.That(device.Taps.SequenceEqual(["supplies_bar", "10000", "10000", "5000", "2000", "2000", "exit_snacks_menu"]),
            $"snacks follow the packet food and counts: {string.Join(", ", device.Taps)}");
        Check.That(events.OfType<MacroLogEvent>().Any(entry => entry.Source == MacroLogSource.Packet && entry.Message.StartsWith("숙소 식량 10,000/44,000")),
            "the packet food is logged with the screen bar beside it");
        Check.That(events.OfType<MacroLogEvent>().Any(entry => entry.Message.Contains("기름 13,239 (추정)")) &&
                   events.OfType<MacroLogEvent>().Any(entry => entry.Message.StartsWith("기름이 한도 20,000보다 적습니다")),
            "print_stats reports the packet oil and the limit");

        game = new DormGame { Food = 30000 };
        device = new DormDevice(food: 30000, shown: [true, true, true, true, true, true]);
        Check.That(Play(root, device, game, null) is false && device.Taps.Count == 0, "no feeding at half or more");

        // No capture: the food bar on screen (30%) and ALAuto's 44,000, read again after every snack.
        device = new DormDevice(food: 13200, shown: [true, true, true, true, true, false]);
        events.Clear();
        fed = Play(root, device, new DormGame { Live = false }, events);
        Check.That(fed is true && device.Taps.SequenceEqual(["supplies_bar", "10000", "10000", "5000", "1000", "exit_snacks_menu"]),
            $"without packets the bar is read off the screen: {string.Join(", ", device.Taps)}");
        Check.That(events.OfType<MacroLogEvent>().Any(entry => entry.Message.Contains("기름 모름")), "no oil without capture");
    }

    private static object? Play(string root, DormDevice device, DormGame game, List<MacroEvent>? events)
    {
        using var runner = new MacroRunner(device, new MacroRunOptions
        {
            AlautoRoot = root,
            BaseRuntime = JsonNode.Parse(File.ReadAllText(Path.Combine(root, "config.json")))!.AsObject(),
            Game = game,
            OnEvent = events is null ? null : events.Add,
            Random = new Random(11),
            Sleep = (_, token) => token.ThrowIfCancellationRequested()
        }, CancellationToken.None);
        return runner.RunPath(Path.Combine(root, "macros", "dorm.json"));
    }

    private sealed class DormGame : IGameState
    {
        public bool Live { get; init; } = true;
        public long Food { get; init; }
        public Dictionary<int, long> Snacks { get; } = new() { [1000] = 9, [2000] = 9, [3000] = 9, [5000] = 9, [10000] = 9, [20000] = 9 };

        public object? Get(string key) => key switch
        {
            "live" => Live,
            "dorm.food" => Food,
            "dorm.capacity" => (long)Capacity,
            "resources.oil" => 13239L,
            "resources.oil_estimated" => true,
            _ when key.StartsWith("dorm.snacks.", StringComparison.Ordinal) =>
                Snacks.GetValueOrDefault(int.Parse(key["dorm.snacks.".Length..])),
            _ => null
        };
    }

    /// <summary>The courtyard with its snack menu: the food bar at y 460 and six snack slots; a snack tap adds its food.</summary>
    private sealed class DormDevice(int food, bool[] shown) : IAutomationDevice
    {
        private static readonly int[] Values = [1000, 2000, 3000, 5000, 10000, 20000];
        private int food = food;

        public List<string> Taps { get; } = [];

        public byte[] Capture()
        {
            using var screen = new Mat(1080, 1920, MatType.CV_8UC3, new Scalar(40, 30, 20));
            var filled = (int)((double)food / Capacity * 875);
            if (filled > 0) Cv2.Rectangle(screen, new Rect(595, 455, filled, 12), new Scalar(0, 200, 255), -1);
            for (var index = 0; index < Values.Length; index++)
            {
                var colour = shown[index] ? new Scalar(200, 200, 200) : new Scalar(50, 50, 50);
                Cv2.Rectangle(screen, new Rect(620 + index * 195, 600, 100, 100), colour, -1);
            }

            return screen.ImEncode(".png");
        }

        public void Tap(int x, int y, int durationMs)
        {
            bool In(int left, int top, int width, int height) => x >= left && x <= left + width && y >= top && y <= top + height;
            if (In(345, 980, 215, 65)) Taps.Add("supplies_bar");
            else if (In(900, 880, 380, 135)) Taps.Add("exit_snacks_menu");
            else if (y is >= 600 and <= 700 && (x - 620) / 195 is var index and >= 0 and < 6 && x <= 620 + index * 195 + 100)
            {
                Taps.Add(Values[index].ToString());
                food = Math.Min(Capacity, food + Values[index]);
            }
            else Taps.Add($"{x},{y}");
        }

        public void Swipe(int x1, int y1, int x2, int y2, int durationMs) { }

        public void Back() { }
    }
}

sealed class FakeGame : IGameState
{
    public long LastId { get; private set; } = 100;
    public int Battles { get; private set; }
    public long Growing { get; set; } = 2;
    public int? StopsAfter { get; init; }
    public bool RecordsBattles { get; init; } = true;
    public bool Live { get; init; } = true;

    public void Fight()
    {
        if (!RecordsBattles) return;
        Battles++;
        LastId += 13;
        if (StopsAfter is { } after && Battles >= after) Growing--;
    }

    public object? Get(string key) => key switch
    {
        "live" => Live,
        "battle.last_id" => LastId,
        "leveling.growing" => Growing,
        _ => null
    };
}

/// <summary>Always shows the repeat button; a tap on it fights one battle.</summary>
sealed class FakeDevice(FakeGame game) : IAutomationDevice
{
    private readonly byte[] screen = Fixture.Screen(showRepeat: true);
    private readonly byte[] empty = Fixture.Screen(showRepeat: false);

    public int Taps { get; private set; }
    public int Captures { get; private set; }
    public Action? OnTap { get; init; }
    /// <summary>False: the 반복 screen never comes (a long 연속 작전 or a stuck game).</summary>
    public bool ShowsRepeat { get; init; } = true;

    public byte[] Capture()
    {
        Captures++;
        return ShowsRepeat ? screen : empty;
    }

    public void Tap(int x, int y, int durationMs)
    {
        Check.That(x is >= 1500 and <= 1611 && y is >= 900 and <= 957 && durationMs is >= 50 and <= 120,
            "taps land inside the found button for 50-120 ms");
        Taps++;
        game.Fight();
        OnTap?.Invoke();
    }

    public void Swipe(int x1, int y1, int x2, int y2, int durationMs) { }

    public void Back() { }
}
