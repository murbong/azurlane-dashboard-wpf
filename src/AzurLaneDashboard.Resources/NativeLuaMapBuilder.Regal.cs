using System.Globalization;
using System.Net.Http;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AzurLaneDashboard.Resources;

internal sealed partial class NativeLuaMapBuilder
{
    private static async Task FetchRegalAssetsAsync(
        string outputDirectory,
        IDictionary<string, int> counts,
        IList<string> logs,
        CancellationToken cancellationToken)
    {
        if (!Http.DefaultRequestHeaders.UserAgent.Any())
        {
            Http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 azurlane-dashboard-native");
        }

        foreach (var (key, fileName) in RegalAssets)
        {
            var url = $"{RegalBaseUrl}/{fileName}";
            using var stream = await Http.GetStreamAsync(url, cancellationToken).ConfigureAwait(false);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            var outputName = $"regal_{fileName}";
            WriteJsonElement(Path.Combine(outputDirectory, outputName), document.RootElement);
            counts[$"regal_{key}"] = document.RootElement.ValueKind switch
            {
                JsonValueKind.Array => document.RootElement.GetArrayLength(),
                JsonValueKind.Object => document.RootElement.EnumerateObject().Count(),
                _ => 0
            };
            logs.Add(
                $"community {key}: {counts[$"regal_{key}"].ToString(CultureInfo.InvariantCulture)} -> {outputName}");
        }
    }
}