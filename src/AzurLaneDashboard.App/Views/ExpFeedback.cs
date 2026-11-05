using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using AzurLaneDashboard.Core.Models;

namespace AzurLaneDashboard.App.Views;

/// <summary>
/// Live EXP feedback for captured battles. A reload rebuilds every row, so the views remember per ship what
/// they last showed: a bar fills from that value to the new one (through full and back to empty on a
/// level-up) and a "+EXP" floater rises over the ship when its total grew. A ship seen for the first time,
/// or client-area animations turned off in Windows, shows the value without motion.
/// </summary>
public static class ExpFeedback
{
    private static readonly TimeSpan FillDuration = TimeSpan.FromMilliseconds(700);
    private static readonly TimeSpan FloatDuration = TimeSpan.FromMilliseconds(1600);
    private static readonly Dictionary<string, BarState> Bars = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, (long Total, int Level)> Totals = new(StringComparer.Ordinal);

    /// <summary>A bar of the EXP inside the level, like the game's ship card.</summary>
    public static readonly DependencyProperty LevelBarProperty = DependencyProperty.RegisterAttached(
        "LevelBar", typeof(ExpGauge), typeof(ExpFeedback),
        new PropertyMetadata(null, (d, e) => ShowBar(d as RangeBase, e.NewValue as ExpGauge, wrap: true)));

    /// <summary>A bar of the progress toward Lv.120.</summary>
    public static readonly DependencyProperty ReadyBarProperty = DependencyProperty.RegisterAttached(
        "ReadyBar", typeof(ExpGauge), typeof(ExpFeedback),
        new PropertyMetadata(null, (d, e) => ShowBar(d as RangeBase, e.NewValue as ExpGauge, wrap: false)));

    /// <summary>Raises "+EXP" over the element when the ship's total grew since this scope last showed it.</summary>
    public static readonly DependencyProperty FloaterProperty = DependencyProperty.RegisterAttached(
        "Floater", typeof(ExpGauge), typeof(ExpFeedback), new PropertyMetadata(null, OnFloaterChanged));

    /// <summary>Keeps floaters apart when one view shows a ship twice (a fleet tile and a queue row).</summary>
    public static readonly DependencyProperty ScopeProperty = DependencyProperty.RegisterAttached(
        "Scope", typeof(string), typeof(ExpFeedback), new PropertyMetadata(""));

    public static ExpGauge? GetLevelBar(DependencyObject element) => (ExpGauge?)element.GetValue(LevelBarProperty);
    public static void SetLevelBar(DependencyObject element, ExpGauge? value) => element.SetValue(LevelBarProperty, value);
    public static ExpGauge? GetReadyBar(DependencyObject element) => (ExpGauge?)element.GetValue(ReadyBarProperty);
    public static void SetReadyBar(DependencyObject element, ExpGauge? value) => element.SetValue(ReadyBarProperty, value);
    public static ExpGauge? GetFloater(DependencyObject element) => (ExpGauge?)element.GetValue(FloaterProperty);
    public static void SetFloater(DependencyObject element, ExpGauge? value) => element.SetValue(FloaterProperty, value);
    public static string GetScope(DependencyObject element) => (string)element.GetValue(ScopeProperty);
    public static void SetScope(DependencyObject element, string value) => element.SetValue(ScopeProperty, value);

    private static bool Animate => SystemParameters.ClientAreaAnimation;

    private static void ShowBar(RangeBase? bar, ExpGauge? gauge, bool wrap)
    {
        if (bar is null) return;
        bar.BeginAnimation(RangeBase.ValueProperty, null);
        var value = gauge is null ? 0 : wrap ? gauge.LevelFraction : gauge.ReadyFraction;
        bar.Value = value;
        if (gauge is null || gauge.Uid.Length == 0) return;

        var key = (wrap ? "level:" : "ready:") + gauge.Uid;
        var now = DateTime.UtcNow;
        if (!Bars.TryGetValue(key, out var last) || gauge.Level < last.Level ||
            gauge.Level == last.Level && value < last.Value - 0.0005)
        {
            // First sight, or the data went backwards (a fresh login snapshot): show it as is.
            Bars[key] = new BarState(gauge.Level, value, gauge.Level, value, DateTime.MinValue);
            return;
        }

        var state = last;
        if (gauge.Level != last.Level || Math.Abs(value - last.Value) >= 0.0005)
        {
            Bars[key] = state = new BarState(last.Level, last.Value, gauge.Level, value, now);
        }

        // An unchanged value replays the rest of a fill that a reload interrupted by rebuilding the row.
        var elapsed = now - state.StartedUtc;
        if (!Animate || elapsed >= FillDuration) return;
        bar.BeginAnimation(RangeBase.ValueProperty, Fill(state, wrap, elapsed));
    }

    private static AnimationTimeline Fill(BarState state, bool wrap, TimeSpan elapsed)
    {
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        AnimationTimeline animation;
        if (wrap && state.Level > state.FromLevel)
        {
            // Finish the old level, start the new one empty, then fill to the EXP carried over.
            var levelUp = new DoubleAnimationUsingKeyFrames { Duration = FillDuration };
            levelUp.KeyFrames.Add(new DiscreteDoubleKeyFrame(state.From, KeyTime.FromPercent(0)));
            levelUp.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromPercent(0.4)));
            levelUp.KeyFrames.Add(new DiscreteDoubleKeyFrame(0, KeyTime.FromPercent(0.42)));
            levelUp.KeyFrames.Add(new EasingDoubleKeyFrame(state.Value, KeyTime.FromPercent(1), ease));
            animation = levelUp;
        }
        else
        {
            animation = new DoubleAnimation(state.From, state.Value, FillDuration) { EasingFunction = ease };
        }

        animation.FillBehavior = FillBehavior.Stop;
        animation.BeginTime = -elapsed;
        return animation;
    }

    private static void OnFloaterChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement host || e.NewValue is not ExpGauge gauge || gauge.Uid.Length == 0) return;
        // After layout: the scope is set by then and the host has a position to rise from.
        host.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () => Float(host, gauge));
    }

    private static void Float(FrameworkElement host, ExpGauge gauge)
    {
        // A recycled container may already show another ship.
        if (!ReferenceEquals(GetFloater(host), gauge)) return;
        var key = GetScope(host) + ":" + gauge.Uid;
        var known = Totals.TryGetValue(key, out var last);
        Totals[key] = (gauge.Total, gauge.Level);
        if (!known || last.Total <= 0 || gauge.Total <= last.Total || !Animate || !host.IsVisible) return;

        var gained = string.Create(CultureInfo.CurrentCulture, $"+{gauge.Total - last.Total:N0} EXP");
        FloaterAdorner.Show(host, gained, gauge.Level > last.Level ? $"Lv.{gauge.Level} ▲" : null);
    }

    private sealed record BarState(int FromLevel, double From, int Level, double Value, DateTime StartedUtc);

    /// <summary>
    /// The floater adorns the scroll content (or window content) its row sits in rather than the row, so it
    /// keeps rising when the next reload replaces the row.
    /// </summary>
    private sealed class FloaterAdorner : Adorner
    {
        private readonly FrameworkElement badge;
        private readonly Point origin;

        private FloaterAdorner(UIElement anchor, FrameworkElement badge, Point origin) : base(anchor)
        {
            this.badge = badge;
            this.origin = origin;
            IsHitTestVisible = false;
            AddVisualChild(badge);
        }

        protected override int VisualChildrenCount => 1;

        protected override Visual GetVisualChild(int index) => badge;

        protected override Size MeasureOverride(Size constraint)
        {
            badge.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            return AdornedElement.RenderSize;
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            var size = badge.DesiredSize;
            badge.Arrange(new Rect(new Point(origin.X - size.Width / 2, origin.Y - size.Height / 2), size));
            return finalSize;
        }

        public static void Show(FrameworkElement host, string gained, string? levelUp)
        {
            var layer = AdornerLayer.GetAdornerLayer(host);
            var anchor = layer is null ? null : AnchorOf(layer);
            if (layer is null || anchor is null || !anchor.IsAncestorOf(host)) return;

            var origin = host.TransformToAncestor(anchor)
                .Transform(new Point(host.ActualWidth / 2, host.ActualHeight * 0.58));
            var badge = Badge(host, gained, levelUp);
            var adorner = new FloaterAdorner(anchor, badge, origin);
            layer.Add(adorner);

            var rise = new TranslateTransform();
            badge.RenderTransform = rise;
            badge.Opacity = 0;
            var fade = new DoubleAnimationUsingKeyFrames { Duration = FloatDuration };
            fade.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromPercent(0.12)));
            fade.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromPercent(0.7)));
            fade.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromPercent(1)));
            fade.Completed += (_, _) =>
            {
                if (layer.GetAdorners(anchor)?.Contains(adorner) == true) layer.Remove(adorner);
            };
            badge.BeginAnimation(OpacityProperty, fade);
            rise.BeginAnimation(TranslateTransform.YProperty,
                new DoubleAnimation(6, -12, FloatDuration) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        }

        private static UIElement? AnchorOf(AdornerLayer layer) => VisualTreeHelper.GetParent(layer) switch
        {
            ScrollContentPresenter presenter => presenter.Content as UIElement ??
                                                (VisualTreeHelper.GetChildrenCount(presenter) > 0
                                                    ? VisualTreeHelper.GetChild(presenter, 0) as UIElement
                                                    : null),
            AdornerDecorator decorator => decorator.Child,
            _ => null
        };

        private static FrameworkElement Badge(FrameworkElement host, string gained, string? levelUp)
        {
            var text = new StackPanel { Orientation = Orientation.Horizontal };
            if (levelUp is not null)
            {
                text.Children.Add(new TextBlock
                {
                    Text = levelUp, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 6, 0),
                    Foreground = Brush(host, "AccentBrush")
                });
            }

            text.Children.Add(new TextBlock { Text = gained, FontWeight = FontWeights.Bold, Foreground = Brush(host, "GoodBrush") });
            var badge = new Border
            {
                Child = text,
                Padding = new Thickness(7, 2, 7, 2),
                CornerRadius = new CornerRadius(9),
                Background = Brush(host, "ShellBrush"),
                BorderBrush = Brush(host, "GoodBrush"),
                BorderThickness = new Thickness(1)
            };
            TextElement.SetFontSize(badge, 12);
            return badge;
        }

        private static Brush Brush(FrameworkElement host, string key) =>
            host.TryFindResource(key) as Brush ?? Brushes.White;
    }
}
