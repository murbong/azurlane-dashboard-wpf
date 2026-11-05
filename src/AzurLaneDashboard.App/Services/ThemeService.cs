using System.Windows;

namespace AzurLaneDashboard.App.Services;

public static class ThemeService
{
    private const string FluentPrefix = "pack://application:,,,/PresentationFramework.Fluent;component/Themes/";

    public static void Apply(bool isDarkMode)
    {
        var app = Application.Current;
        if (app is null)
        {
            return;
        }

        var dictionaries = app.Resources.MergedDictionaries;

        // Keep Fluent's built-in control colors (scroll bars, tooltips, context menus) in step with the app theme.
        // The Fluent dictionary stays at its original position so app styles merged after it still win.
        for (var index = 0; index < dictionaries.Count; index++)
        {
            if (IsFluentDictionary(dictionaries[index]))
            {
                dictionaries[index] = new ResourceDictionary
                {
                    Source = new Uri($"{FluentPrefix}Fluent.{(isDarkMode ? "Dark" : "Light")}.xaml")
                };
                break;
            }
        }

        for (var index = dictionaries.Count - 1; index >= 0; index--)
        {
            if (IsAppThemeDictionary(dictionaries[index]))
            {
                dictionaries.RemoveAt(index);
            }
        }

        dictionaries.Add(new ResourceDictionary
        {
            Source = new Uri($"pack://application:,,,/AzurLaneDashboard.App;component/Themes/{(isDarkMode ? "Dark" : "Light")}.xaml")
        });
    }

    private static bool IsFluentDictionary(ResourceDictionary dictionary)
    {
        var source = dictionary.Source?.OriginalString;
        return source is not null && source.StartsWith(FluentPrefix, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsAppThemeDictionary(ResourceDictionary dictionary)
    {
        var source = dictionary.Source?.OriginalString.Replace('\\', '/');
        return source is not null
            && (source.EndsWith("Themes/Dark.xaml", StringComparison.OrdinalIgnoreCase)
                || source.EndsWith("Themes/Light.xaml", StringComparison.OrdinalIgnoreCase));
    }
}
