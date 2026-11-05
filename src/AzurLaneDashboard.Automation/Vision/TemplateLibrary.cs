using OpenCvSharp;

namespace AzurLaneDashboard.Automation.Vision;

public readonly record struct TemplateMatch(ScreenRegion Region, double Score, bool Found);

/// <summary>
/// ALAuto's asset images (assets/KR/combat/repeat.png …) matched the way its Utils.find does: grayscale
/// TM_CCOEFF_NORMED by default, the colour screen when asked, and TM_CCORR_NORMED with the alpha channel as mask.
/// </summary>
public sealed class TemplateLibrary(string assetsRoot) : IDisposable
{
    private readonly Dictionary<string, Mat?> gray = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Mat?> color = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Mat?> masks = new(StringComparer.OrdinalIgnoreCase);

    public string AssetsRoot { get; } = assetsRoot;

    public string PathOf(string image) => Path.Combine(AssetsRoot, image.Replace('/', Path.DirectorySeparatorChar) + ".png");

    public bool Exists(string image) => File.Exists(PathOf(image));

    /// <summary>The best match; <see cref="TemplateMatch.Found"/> when it reaches <paramref name="similarity"/>.</summary>
    public TemplateMatch? Find(ScreenFrame frame, string image, double similarity, bool useColor = false, bool useMask = false)
    {
        var template = useColor ? Load(color, image, ImreadModes.Color) : Load(gray, image, ImreadModes.Grayscale);
        if (template is null || template.Width > frame.Width || template.Height > frame.Height) return null;

        var mask = useMask && !useColor ? Mask(image) : null;
        using var result = new Mat();
        if (mask is not null)
        {
            // ALAuto uses TM_CCORR_NORMED here, which ignores brightness offsets and scored 0.93-0.94 for the
            // morale and unable-to-sortie warnings on a plain 재출격 screen. The mean-subtracted method keeps
            // the mask and gives 0.3-0.45 there and 1.0 on the real warning.
            Cv2.MatchTemplate(frame.Gray, template, result, TemplateMatchModes.CCoeffNormed, mask);
        }
        else
        {
            Cv2.MatchTemplate(useColor ? frame.Color : frame.Gray, template, result, TemplateMatchModes.CCoeffNormed);
        }

        // Flat areas divide by zero: NaN and infinity must not read as a perfect match.
        Cv2.PatchNaNs(result, 0);
        using (var overflow = new Mat())
        {
            Cv2.Compare(result, new Scalar(1.0001), overflow, CmpTypes.GT);
            result.SetTo(new Scalar(0), overflow);
        }

        Cv2.MinMaxLoc(result, out _, out var best, out _, out var location);
        var score = double.IsFinite(best) ? best : 0;
        return new TemplateMatch(new ScreenRegion(location.X, location.Y, template.Width, template.Height), score,
            score >= similarity);
    }

    private Mat? Load(Dictionary<string, Mat?> cache, string image, ImreadModes mode)
    {
        if (cache.TryGetValue(image, out var cached)) return cached;
        var path = PathOf(image);
        Mat? loaded = null;
        if (File.Exists(path))
        {
            // ImRead cannot open non-ASCII paths on Windows; decode the bytes instead.
            loaded = Cv2.ImDecode(File.ReadAllBytes(path), mode);
            if (loaded.Empty())
            {
                loaded.Dispose();
                loaded = null;
            }
        }

        cache[image] = loaded;
        return loaded;
    }

    // Opaque pixels count, transparent ones are ignored, as get_mask_from_alpha thresholds alpha > 0.
    private Mat? Mask(string image)
    {
        if (masks.TryGetValue(image, out var cached)) return cached;
        Mat? mask = null;
        var path = PathOf(image);
        if (File.Exists(path))
        {
            using var source = Cv2.ImDecode(File.ReadAllBytes(path), ImreadModes.Unchanged);
            if (!source.Empty() && source.Channels() == 4)
            {
                var channels = Cv2.Split(source);
                mask = new Mat();
                Cv2.Threshold(channels[3], mask, 0, 255, ThresholdTypes.Binary);
                foreach (var channel in channels) channel.Dispose();
            }
        }

        masks[image] = mask;
        return mask;
    }

    public void Dispose()
    {
        foreach (var mat in gray.Values.Concat(color.Values).Concat(masks.Values)) mat?.Dispose();
        gray.Clear();
        color.Clear();
        masks.Clear();
    }
}
