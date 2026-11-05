using System.Windows;
using System.Windows.Controls;

namespace AzurLaneDashboard.App.Views;

public partial class TitleBarView : UserControl
{
    private Window? hostWindow;

    public TitleBarView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        hostWindow = Window.GetWindow(this);
        if (hostWindow is not null)
        {
            hostWindow.StateChanged += HostWindow_StateChanged;
            UpdateMaximizeButton();
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (hostWindow is not null)
        {
            hostWindow.StateChanged -= HostWindow_StateChanged;
            hostWindow = null;
        }
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
    {
        if (hostWindow is not null)
        {
            hostWindow.WindowState = WindowState.Minimized;
        }
    }

    private void MaximizeButton_Click(object sender, RoutedEventArgs e)
    {
        if (hostWindow is null)
        {
            return;
        }

        hostWindow.WindowState = hostWindow.WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        hostWindow?.Close();
    }

    private void HostWindow_StateChanged(object? sender, EventArgs e)
    {
        UpdateMaximizeButton();
    }

    private void UpdateMaximizeButton()
    {
        var maximized = hostWindow?.WindowState == WindowState.Maximized;
        // Segoe Fluent Icons: ChromeRestore / ChromeMaximize.
        MaximizeButton.Content = maximized ? "" : "";
        MaximizeButton.ToolTip = maximized ? "이전 크기로" : "최대화";
    }
}
