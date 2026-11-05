using System.Reflection;
using AzurLaneDashboard.Resources;

internal static class UpdateProcessChecks
{
    public static async Task RunAsync()
    {
        var method = typeof(DataUpdateService).GetMethod("RunProcessCaptureAsync", BindingFlags.NonPublic | BindingFlags.Static)!;
        var executable = Environment.ProcessPath!;
        var prefix = Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase)
            ? new[] { Assembly.GetExecutingAssembly().Location } : Array.Empty<string>();

        async Task<object> Run(bool logOutput, bool wait, CancellationToken cancellationToken)
        {
            var arguments = prefix.Concat(new[] { "--process-output-fixture" })
                .Concat(wait ? new[] { "wait" } : Array.Empty<string>()).ToArray();
            var task = (Task)method.Invoke(null, [executable, arguments, Environment.CurrentDirectory,
                new List<string>(), logOutput, cancellationToken])!;
            await task.WaitAsync(TimeSpan.FromSeconds(15));
            return task.GetType().GetProperty("Result")!.GetValue(task)!;
        }

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var logged = await Run(logOutput: true, wait: false, timeout.Token);
        Check((int)logged.GetType().GetProperty("ExitCode")!.GetValue(logged)! == 0,
            "large stdout and stderr must drain without blocking the updater");
        var stdout = (string)logged.GetType().GetProperty("StandardOutput")!.GetValue(logged)!;
        Check(stdout.Contains("log truncated") && stdout.Length < 1_049_000, "large logs remain bounded");

        var structured = await Run(logOutput: false, wait: false, timeout.Token);
        Check((int)structured.GetType().GetProperty("ExitCode")!.GetValue(structured)! == 124,
            "oversized structured output fails promptly rather than returning partial data");

        using var cancelled = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
        try
        {
            await Run(logOutput: true, wait: true, cancelled.Token);
            throw new InvalidOperationException("process cancellation was ignored");
        }
        catch (OperationCanceledException) when (cancelled.IsCancellationRequested) { }
        Console.WriteLine("Update subprocess output limits and cancellation checks passed.");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
