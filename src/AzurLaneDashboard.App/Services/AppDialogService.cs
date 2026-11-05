using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace AzurLaneDashboard.App.Services;

public static class AppDialogService
{
    public static MessageBoxResult Show(
        string message,
        string title,
        MessageBoxButton buttons = MessageBoxButton.OK,
        MessageBoxImage image = MessageBoxImage.None,
        MessageBoxResult defaultResult = MessageBoxResult.None)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess())
        {
            return dispatcher.Invoke(() => Show(message, title, buttons, image, defaultResult));
        }

        return ShowCore(message, title, buttons, image, defaultResult);
    }

    private static MessageBoxResult ShowCore(
        string message,
        string title,
        MessageBoxButton buttons,
        MessageBoxImage image,
        MessageBoxResult defaultResult)
    {
        var result = NormalizeDefault(buttons, defaultResult);
        var window = new Window
        {
            Title = title,
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            SizeToContent = SizeToContent.WidthAndHeight,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            ShowInTaskbar = false,
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            UseLayoutRounding = true,
            SnapsToDevicePixels = true,
            MinWidth = 420,
            MaxWidth = 600
        };

        var owner = FindOwner();
        if (owner is not null)
        {
            window.Owner = owner;
            window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }

        var surfaceBrush = BrushResource("SurfaceBrush", new SolidColorBrush(Color.FromRgb(43, 43, 43)));
        var surface2Brush = BrushResource("Surface2Brush", new SolidColorBrush(Color.FromRgb(51, 51, 51)));
        var lineBrush = BrushResource("LineBrush", new SolidColorBrush(Color.FromRgb(58, 58, 58)));
        var textBrush = BrushResource("TextBrush", Brushes.White);
        var mutedBrush = BrushResource("MutedBrush", new SolidColorBrush(Color.FromRgb(166, 166, 166)));
        var accentBrush = BrushResource("AccentBrush", new SolidColorBrush(Color.FromRgb(96, 205, 255)));
        var iconBrush = IconBrush(image, accentBrush);

        var root = new Border
        {
            Background = surfaceBrush,
            BorderBrush = lineBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(20),
            Effect = new DropShadowEffect
            {
                BlurRadius = 24,
                ShadowDepth = 0,
                Opacity = 0.38,
                Color = Colors.Black
            }
        };

        var layout = new Grid();
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.Child = layout;

        var contentGrid = new Grid();
        contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(contentGrid, 0);
        layout.Children.Add(contentGrid);

        var icon = new Border
        {
            Width = 34,
            Height = 34,
            CornerRadius = new CornerRadius(17),
            Background = surface2Brush,
            BorderBrush = iconBrush,
            BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 1, 14, 0),
            Child = new TextBlock
            {
                Text = IconText(image),
                Foreground = iconBrush,
                FontSize = 18,
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                TextAlignment = TextAlignment.Center
            }
        };
        contentGrid.Children.Add(icon);

        var textPanel = new StackPanel
        {
            MaxWidth = 500
        };
        Grid.SetColumn(textPanel, 1);
        contentGrid.Children.Add(textPanel);

        var titleBlock = new TextBlock
        {
            Text = title,
            Foreground = textBrush,
            FontSize = 17,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap
        };
        textPanel.Children.Add(titleBlock);

        var messageBlock = new TextBlock
        {
            Text = message,
            Foreground = mutedBrush,
            FontSize = 13,
            LineHeight = 20,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 10, 0, 0)
        };
        textPanel.Children.Add(messageBlock);

        var buttonPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 22, 0, 0)
        };
        Grid.SetRow(buttonPanel, 1);
        layout.Children.Add(buttonPanel);

        foreach (var button in GetButtons(buttons))
        {
            var isDefault = button.Result == result;
            var isPrimary = isDefault;
            var control = new Button
            {
                Content = button.Label,
                MinWidth = 86,
                MinHeight = 34,
                Padding = new Thickness(14, 0, 14, 0),
                Margin = new Thickness(8, 0, 0, 0),
                IsDefault = isDefault,
                IsCancel = IsCancelButton(button.Result, buttons),
                Background = isPrimary ? accentBrush : surface2Brush,
                BorderBrush = isPrimary ? accentBrush : lineBrush,
                Foreground = isPrimary ? ContrastBrush(accentBrush) : textBrush,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center
            };

            control.Click += (_, _) =>
            {
                result = button.Result;
                window.DialogResult = true;
                window.Close();
            };

            buttonPanel.Children.Add(control);
        }

        window.KeyDown += (_, args) =>
        {
            if (args.Key != Key.Escape)
            {
                return;
            }

            result = EscapeResult(buttons);
            window.DialogResult = true;
            window.Close();
            args.Handled = true;
        };

        root.MouseLeftButtonDown += (_, args) =>
        {
            if (args.ButtonState != MouseButtonState.Pressed)
            {
                return;
            }

            try
            {
                window.DragMove();
            }
            catch (InvalidOperationException)
            {
                // DragMove can throw if the mouse state changes between the event and the call.
            }
        };

        window.Content = root;
        window.ShowDialog();
        return result;
    }

    private static Window? FindOwner()
    {
        var app = Application.Current;
        if (app is null)
        {
            return null;
        }

        foreach (Window window in app.Windows)
        {
            if (window.IsActive && window.IsVisible)
            {
                return window;
            }
        }

        var mainWindow = app.MainWindow;
        return mainWindow is { IsVisible: true } ? mainWindow : null;
    }

    private static Brush BrushResource(string key, Brush fallback)
    {
        return Application.Current?.TryFindResource(key) as Brush ?? fallback;
    }

    private static Brush IconBrush(MessageBoxImage image, Brush accentBrush)
    {
        return image switch
        {
            MessageBoxImage.Warning => BrushResource("CommunityHighBorderBrush", new SolidColorBrush(Color.FromRgb(185, 130, 24))),
            MessageBoxImage.Error => new SolidColorBrush(Color.FromRgb(232, 17, 35)),
            MessageBoxImage.Question => accentBrush,
            MessageBoxImage.Information => accentBrush,
            _ => accentBrush
        };
    }

    private static string IconText(MessageBoxImage image)
    {
        return image switch
        {
            MessageBoxImage.Warning => "!",
            MessageBoxImage.Error => "x",
            MessageBoxImage.Question => "?",
            MessageBoxImage.Information => "i",
            _ => "i"
        };
    }

    private static Brush ContrastBrush(Brush background)
    {
        if (background is not SolidColorBrush solid)
        {
            return Brushes.White;
        }

        var color = solid.Color;
        var luminance = (0.299 * color.R + 0.587 * color.G + 0.114 * color.B) / 255;
        return luminance > 0.55 ? Brushes.Black : Brushes.White;
    }

    private static IReadOnlyList<DialogButton> GetButtons(MessageBoxButton buttons)
    {
        const string ok = "\uD655\uC778";
        const string cancel = "\uCDE8\uC18C";
        const string yes = "\uC608";
        const string no = "\uC544\uB2C8\uC624";

        return buttons switch
        {
            MessageBoxButton.OKCancel => new[]
            {
                new DialogButton(ok, MessageBoxResult.OK),
                new DialogButton(cancel, MessageBoxResult.Cancel)
            },
            MessageBoxButton.YesNo => new[]
            {
                new DialogButton(yes, MessageBoxResult.Yes),
                new DialogButton(no, MessageBoxResult.No)
            },
            MessageBoxButton.YesNoCancel => new[]
            {
                new DialogButton(yes, MessageBoxResult.Yes),
                new DialogButton(no, MessageBoxResult.No),
                new DialogButton(cancel, MessageBoxResult.Cancel)
            },
            _ => new[]
            {
                new DialogButton(ok, MessageBoxResult.OK)
            }
        };
    }

    private static MessageBoxResult NormalizeDefault(MessageBoxButton buttons, MessageBoxResult defaultResult)
    {
        foreach (var button in GetButtons(buttons))
        {
            if (button.Result == defaultResult)
            {
                return defaultResult;
            }
        }

        return buttons switch
        {
            MessageBoxButton.OKCancel => MessageBoxResult.OK,
            MessageBoxButton.YesNo => MessageBoxResult.Yes,
            MessageBoxButton.YesNoCancel => MessageBoxResult.Yes,
            _ => MessageBoxResult.OK
        };
    }

    private static MessageBoxResult EscapeResult(MessageBoxButton buttons)
    {
        return buttons switch
        {
            MessageBoxButton.OK => MessageBoxResult.OK,
            MessageBoxButton.OKCancel => MessageBoxResult.Cancel,
            MessageBoxButton.YesNo => MessageBoxResult.No,
            MessageBoxButton.YesNoCancel => MessageBoxResult.Cancel,
            _ => MessageBoxResult.Cancel
        };
    }

    private static bool IsCancelButton(MessageBoxResult result, MessageBoxButton buttons)
    {
        return result == MessageBoxResult.Cancel ||
               (buttons == MessageBoxButton.YesNo && result == MessageBoxResult.No) ||
               (buttons == MessageBoxButton.OK && result == MessageBoxResult.OK);
    }

    private sealed record DialogButton(string Label, MessageBoxResult Result);
}
