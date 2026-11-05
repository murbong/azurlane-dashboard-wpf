using System.Windows;

namespace AzurLaneDashboard.App.Views;

/// <summary>
/// Content placed between a TabControl's header strip and its selected page. Tools that appear only on some
/// tabs (search, filters) live here so switching tabs never moves the header strip under the pointer.
/// </summary>
public static class TabTools
{
    public static readonly DependencyProperty ContentProperty = DependencyProperty.RegisterAttached(
        "Content", typeof(object), typeof(TabTools), new PropertyMetadata(null));

    public static object? GetContent(DependencyObject element) => element.GetValue(ContentProperty);

    public static void SetContent(DependencyObject element, object? value) => element.SetValue(ContentProperty, value);
}
