using System.Globalization;

namespace AzurLaneDashboard.Resources;

public sealed partial class DataUpdateService
{
    private static async Task PatchLocalAssetsAsync(UpdateContext context, IList<string> logs,
        CancellationToken cancellationToken)
    {
        if (context.SkipIconExtract)
        {
            Report(context.Progress, "이미지", "아이콘 추출과 맵 패치를 건너뜀", null, null, true);
            logs.Add("icon extraction and map patch skipped by option");
            return;
        }

        logs.Add($"static assets directory: {context.StaticAssetsDirectory}");
        var assetDirectory = Path.Combine(context.GameResourcesDirectory, context.Region, "AssetBundles");
        if (!context.ForceIconExtract &&
            HasUsableLocalAssetManifestEntries(
                context.LocalAssetManifest,
                context.StaticAssetsDirectory,
                assetDirectory,
                context.Categories,
                logs))
        {
            Report(context.Progress, "이미지",
                "existing local asset manifest reused; enable force icon extract to rebuild PNGs",
                null, null, true);
            logs.Add(
                $"native asset extraction skipped: existing manifest reused ({context.LocalAssetManifest}); enable force icon extract to rebuild PNGs");
        }
        else if (Directory.Exists(assetDirectory))
        {
            Report(context.Progress, "이미지", "AssetBundle Texture2D 스캔 중", null, null, true);
            var extractor = new NativeAssetExtractor();
            var result = await extractor.ExtractAsync(
                assetDirectory,
                context.StaticAssetsDirectory,
                context.LocalAssetManifest,
                context.Categories,
                context.ForceIconExtract,
                context.ProgressEvery,
                logs,
                context.Progress,
                cancellationToken).ConfigureAwait(false);
            logs.Add(
                $"native asset extraction: bundles={result.Bundles.ToString(CultureInfo.InvariantCulture)} images={result.Images.ToString(CultureInfo.InvariantCulture)} skipped={result.Skipped.ToString(CultureInfo.InvariantCulture)} failures={result.Failures.ToString(CultureInfo.InvariantCulture)}");
        }
        else
        {
            Report(context.Progress, "이미지", "AssetBundle 디렉터리가 없어 기존 manifest 재사용", null, null, true);
            logs.Add(
                $"asset bundle directory not found: {assetDirectory}; existing local asset manifest will be reused");
        }

        Report(context.Progress, "이미지", "로컬 이미지 URL을 맵 파일에 반영 중", null, null, true);
        LocalAssetMapPatcher.Patch(
            context.LocalAssetManifest,
            context.MapsDirectory,
            context.StaticAssetsDirectory,
            logs);
    }
}
