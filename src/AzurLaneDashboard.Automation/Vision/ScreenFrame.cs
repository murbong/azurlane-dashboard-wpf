using OpenCvSharp;

namespace AzurLaneDashboard.Automation.Vision;

/// <summary>A rectangle on the 1920x1080 screen, as ALAuto's Region.</summary>
public readonly record struct ScreenRegion(int X, int Y, int W, int H)
{
    public int CenterX => X + W / 2;
    public int CenterY => Y + H / 2;
}

/// <summary>One captured screen: the PNG as captured plus its colour and grayscale images for matching.</summary>
public sealed class ScreenFrame : IDisposable
{
    private ScreenFrame(byte[] png, Mat color, Mat gray)
    {
        Png = png;
        Color = color;
        Gray = gray;
    }

    public byte[] Png { get; }
    public Mat Color { get; }
    public Mat Gray { get; }
    public int Width => Color.Width;
    public int Height => Color.Height;

    public static ScreenFrame FromPng(byte[] png)
    {
        var color = Cv2.ImDecode(png, ImreadModes.Color);
        if (color.Empty())
        {
            color.Dispose();
            throw new InvalidOperationException("화면 캡처를 읽지 못했습니다.");
        }

        var gray = new Mat();
        Cv2.CvtColor(color, gray, ColorConversionCodes.BGR2GRAY);
        return new ScreenFrame(png, color, gray);
    }

    /// <summary>Average colour of the region in OpenCV HSV (H 0-179), as ALAuto's get_region_color_average.</summary>
    public (int H, int S, int V) AverageHsv(ScreenRegion region)
    {
        var bounds = new Rect(region.X, region.Y, region.W, region.H).Intersect(new Rect(0, 0, Width, Height));
        if (bounds.Width <= 0 || bounds.Height <= 0) return (0, 0, 0);
        using var crop = new Mat(Color, bounds);
        var mean = Cv2.Mean(crop);
        using var pixel = new Mat(1, 1, MatType.CV_8UC3, new Scalar((byte)mean.Val0, (byte)mean.Val1, (byte)mean.Val2));
        using var hsv = new Mat();
        Cv2.CvtColor(pixel, hsv, ColorConversionCodes.BGR2HSV);
        var value = hsv.Get<Vec3b>(0, 0);
        return (value.Item0, value.Item1, value.Item2);
    }

    /// <summary>
    /// The share of the region's columns with at least one pixel outside the HSV range
    /// <paramref name="low"/>..<paramref name="high"/>: how far a bar on an empty background is filled.
    /// </summary>
    public double FilledColumnRatio(ScreenRegion region, (int H, int S, int V) low, (int H, int S, int V) high)
    {
        var bounds = new Rect(region.X, region.Y, region.W, region.H).Intersect(new Rect(0, 0, Width, Height));
        if (bounds.Width <= 0 || bounds.Height <= 0) return 0;
        using var crop = new Mat(Color, bounds);
        using var hsv = new Mat();
        Cv2.CvtColor(crop, hsv, ColorConversionCodes.BGR2HSV);
        using var empty = new Mat();
        Cv2.InRange(hsv, new Scalar(low.H, low.S, low.V), new Scalar(high.H, high.S, high.V), empty);
        // Each column's minimum is 0 when any of its pixels is outside the empty range.
        using var columnMin = new Mat();
        Cv2.Reduce(empty, columnMin, ReduceDimension.Row, ReduceTypes.Min, -1);
        var columns = columnMin.Cols;
        return (double)(columns - Cv2.CountNonZero(columnMin)) / columns;
    }

    public void Dispose()
    {
        Color.Dispose();
        Gray.Dispose();
    }
}
