using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using AzurLaneDashboard.Core.Services;

namespace AzurLaneDashboard.App.Converters;

/// <summary>
/// Maps a rarity code to a brush.
/// No parameter: filled badge brush (white text sits on it).
/// "bar": the 3px leading marker on rows and cards (a vertical edge).
/// "edge": the same marker drawn along a horizontal edge, e.g. under a portrait.
/// "cell": a faint wash that fades out to the right; alpha based so it works on light and dark surfaces.
/// Every tier keeps its own hue family in all modes: SR purple, SSR gold, PR gold and blue,
/// UR full rainbow, DR crimson to gold — a thin bar or faint wash must not read as a neighbouring tier.
/// </summary>
public sealed class RarityBrushConverter : IValueConverter
{
    // Declared first: static fields initialize in source order and the brushes below depend on these.
    private static readonly (Point Start, Point End) Diagonal = (new Point(0, 0), new Point(1, 1));
    private static readonly (Point Start, Point End) Vertical = (new Point(0, 0), new Point(0, 1));
    private static readonly (Point Start, Point End) Horizontal = (new Point(0, 0.5), new Point(1, 0.5));

    private static readonly Color NormalHue = Color.FromRgb(0x8E, 0x99, 0xA6);
    private static readonly Color RareHue = Color.FromRgb(0x4C, 0x9B, 0xE8);
    private static readonly Color EliteHue = Color.FromRgb(0xA6, 0x6B, 0xE0);
    private static readonly Color SuperRareHue = Color.FromRgb(0xE0, 0xAA, 0x3E);
    private static readonly Color UnknownHue = Color.FromRgb(0x7F, 0x88, 0x92);

    private static readonly (byte, byte, byte, double)[] UltraRareStops =
    [
        (0xFF, 0x5C, 0x70, 0.00),
        (0xFF, 0xB0, 0x42, 0.25),
        (0x56, 0xD8, 0x79, 0.50),
        (0x42, 0xB9, 0xF4, 0.75),
        (0xB8, 0x65, 0xFF, 1.00)
    ];

    private static readonly (byte, byte, byte, double)[] DecisiveStops =
    [
        (0xFF, 0x4D, 0x6D, 0.00),
        (0xFF, 0x8A, 0x3D, 0.50),
        (0xFF, 0xCC, 0x4D, 1.00)
    ];

    private static readonly Brush NormalBrush = Linear(Diagonal, (0x6E, 0x79, 0x86, 0.0), (0x58, 0x62, 0x6F, 1.0));
    private static readonly Brush RareBrush = Linear(Diagonal, (0x34, 0x86, 0xD4, 0.0), (0x23, 0x68, 0xAD, 1.0));
    private static readonly Brush EliteBrush = Linear(Diagonal, (0x93, 0x55, 0xD1, 0.0), (0x73, 0x3F, 0xAD, 1.0));
    private static readonly Brush SuperRareBrush = Linear(Diagonal, (0xC6, 0x8F, 0x22, 0.0), (0x9C, 0x6C, 0x12, 1.0));
    private static readonly Brush UnknownBrush = Solid(UnknownHue);

    private static readonly Brush PriorityBrush = Linear(Diagonal,
        (0xC8, 0x9C, 0x2E, 0.00),
        (0xA8, 0x7A, 0x1E, 0.50),
        (0x4A, 0x82, 0xB4, 1.00));

    private static readonly Brush UltraRareBrush = Linear(Diagonal,
        (0xE0, 0x47, 0x5E, 0.00),
        (0xD9, 0x7A, 0x22, 0.18),
        (0x3F, 0xA8, 0x62, 0.40),
        (0x2B, 0x9C, 0xC0, 0.60),
        (0x4E, 0x5F, 0xE0, 0.80),
        (0xB0, 0x4F, 0xD8, 1.00));

    private static readonly Brush DecisiveBrush = Linear(Diagonal,
        (0xC4, 0x2B, 0x4E, 0.00),
        (0xD4, 0x58, 0x2C, 0.55),
        (0xC4, 0x92, 0x1E, 1.00));

    private static readonly Brush NormalBar = Solid(NormalHue);
    private static readonly Brush RareBar = Solid(RareHue);
    private static readonly Brush EliteBar = Solid(EliteHue);
    private static readonly Brush SuperRareBar = Solid(SuperRareHue);
    private static readonly Brush UnknownBar = Solid(UnknownHue);
    private static readonly Brush PriorityBar = Linear(Vertical, (0xE6, 0xBE, 0x52, 0.0), (0x6F, 0x9F, 0xC9, 1.0));
    private static readonly Brush UltraRareBar = Linear(Vertical, UltraRareStops);
    private static readonly Brush DecisiveBar = Linear(Vertical, DecisiveStops);

    private static readonly Brush NormalEdge = NormalBar;
    private static readonly Brush RareEdge = RareBar;
    private static readonly Brush EliteEdge = EliteBar;
    private static readonly Brush SuperRareEdge = SuperRareBar;
    private static readonly Brush PriorityEdge = Linear(Horizontal, (0xE6, 0xBE, 0x52, 0.0), (0x6F, 0x9F, 0xC9, 1.0));
    private static readonly Brush UltraRareEdge = Linear(Horizontal, UltraRareStops);
    private static readonly Brush DecisiveEdge = Linear(Horizontal, DecisiveStops);

    private static readonly Brush NormalCell = Wash(NormalHue);
    private static readonly Brush RareCell = Wash(RareHue);
    private static readonly Brush EliteCell = Wash(EliteHue);
    private static readonly Brush SuperRareCell = Wash(SuperRareHue);
    private static readonly Brush UnknownCell = Brushes.Transparent;
    private static readonly Brush PriorityCell = Wash(Color.FromRgb(0xE0, 0xB8, 0x4E), Color.FromRgb(0x6F, 0x9F, 0xC9));
    // A two-hue pink-to-violet wash read as SR purple; the UR wash carries the whole spectrum.
    private static readonly Brush UltraRareCell = Wash(Color.FromRgb(0xFF, 0x6A, 0x7F), Color.FromRgb(0xFF, 0xB0, 0x42),
        Color.FromRgb(0x56, 0xD8, 0x79), Color.FromRgb(0x42, 0xB9, 0xF4));
    private static readonly Brush DecisiveCell = Wash(Color.FromRgb(0xFF, 0x4D, 0x6D), Color.FromRgb(0xFF, 0x9A, 0x3C));

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var rarity = RarityHelper.Normalize(value);
        var mode = parameter as string;
        if (string.Equals(mode, "cell", StringComparison.OrdinalIgnoreCase))
        {
            return rarity switch
            {
                "N" => NormalCell,
                "R" => RareCell,
                "SR" => EliteCell,
                "SSR" => SuperRareCell,
                "PR" => PriorityCell,
                "UR" => UltraRareCell,
                "DR" => DecisiveCell,
                _ => UnknownCell
            };
        }

        if (string.Equals(mode, "edge", StringComparison.OrdinalIgnoreCase))
        {
            return rarity switch
            {
                "N" => NormalEdge,
                "R" => RareEdge,
                "SR" => EliteEdge,
                "SSR" => SuperRareEdge,
                "PR" => PriorityEdge,
                "UR" => UltraRareEdge,
                "DR" => DecisiveEdge,
                _ => UnknownBar
            };
        }

        if (string.Equals(mode, "bar", StringComparison.OrdinalIgnoreCase))
        {
            return rarity switch
            {
                "N" => NormalBar,
                "R" => RareBar,
                "SR" => EliteBar,
                "SSR" => SuperRareBar,
                "PR" => PriorityBar,
                "UR" => UltraRareBar,
                "DR" => DecisiveBar,
                _ => UnknownBar
            };
        }

        return rarity switch
        {
            "N" => NormalBrush,
            "R" => RareBrush,
            "SR" => EliteBrush,
            "SSR" => SuperRareBrush,
            "PR" => PriorityBrush,
            "UR" => UltraRareBrush,
            "DR" => DecisiveBrush,
            _ => UnknownBrush
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }

    private static Brush Solid(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    // Hues are spread over the visible first half; opacity falls from 0x2E to 0x12, then fades out.
    private static Brush Wash(params Color[] hues)
    {
        if (hues.Length == 1) hues = [hues[0], hues[0]];
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0.5),
            EndPoint = new Point(1, 0.5)
        };
        var last = hues.Length - 1;
        for (var index = 0; index <= last; index++)
        {
            var share = (double)index / last;
            var alpha = (byte)Math.Round(0x2E + (0x12 - 0x2E) * share);
            var hue = hues[index];
            brush.GradientStops.Add(new GradientStop(Color.FromArgb(alpha, hue.R, hue.G, hue.B), 0.45 * share));
        }

        var end = hues[last];
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0x00, end.R, end.G, end.B), 0.9));
        brush.Freeze();
        return brush;
    }

    private static Brush Linear((Point Start, Point End) direction,
        params (byte Red, byte Green, byte Blue, double Offset)[] stops)
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = direction.Start,
            EndPoint = direction.End
        };

        foreach (var stop in stops)
        {
            brush.GradientStops.Add(new GradientStop(Color.FromRgb(stop.Red, stop.Green, stop.Blue), stop.Offset));
        }

        brush.Freeze();
        return brush;
    }
}
