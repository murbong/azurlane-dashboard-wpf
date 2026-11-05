using AssetsTools.NET.Extra;
using AssetsTools.NET.Texture;

namespace AzurLaneDashboard.Resources;

internal sealed partial class NativeAssetExtractor
{
    private enum TextureWriteOutcome
    {
        Written,
        Existing,
        Empty,
        TooLarge
    }

    private sealed record TextureAsset(AssetsFileInstance AssetsFile, TextureFile Texture)
    {
        public string TextureName =>
            string.IsNullOrWhiteSpace(Texture.m_Name) ? "texture" : Texture.m_Name;
    }

    private sealed record AssetManifestEntry(
        string Category,
        string BundlePath,
        string TextureName,
        string File,
        string Url,
        bool Alias);

    private sealed record BundleExtractionResult(IReadOnlyList<AssetManifestEntry> Entries, int Skipped);
}
