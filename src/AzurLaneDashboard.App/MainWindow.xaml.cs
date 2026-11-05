using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using AzurLaneDashboard.App.ViewModels;
using AzurLaneDashboard.App.Services;
using AzurLaneDashboard.Core.Services;

namespace AzurLaneDashboard.App;

public partial class MainWindow : Window
{
    private readonly MainWindowViewModel viewModel;
    private bool isViewModelDisposed;
    private readonly OperationStatusWriter? operationStatusWriter;
    private const uint StopMacroKey = 0x77; // F8
    private readonly GlobalHotkey stopMacroHotkey;
    private readonly DesktopNotifier notifier;
    private bool noticeOpensMacroPage;

    public MainWindow()
    {
        InitializeComponent();
        viewModel = new MainWindowViewModel();
        DataContext = viewModel;
        viewModel.PropertyChanged += ViewModelPropertyChanged;
        // F8 stops a running macro even while the game window has focus; held only during a run.
        stopMacroHotkey = new GlobalHotkey(this, StopMacroKey, () => viewModel.Macros.Stop());
        viewModel.Macros.RunningChanged += MacroRunningChanged;
        notifier = new DesktopNotifier(this, Title, ShowNoticeSource);
        viewModel.Macros.EndNotice += MacroEndNotice;
        viewModel.SortieNotice += SortieNotice;
        PreviewKeyDown += (_, args) =>
        {
            if (args.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control && viewModel.IsInventorySearchVisible)
            {
                DashboardShell.FocusSearch();
                args.Handled = true;
            }
        };
        var arguments = Environment.GetCommandLineArgs().Skip(1).ToArray();
        var statusArgument = arguments.FirstOrDefault(argument => argument.StartsWith("--status-file=", StringComparison.Ordinal));
        if (statusArgument is not null)
            operationStatusWriter = new OperationStatusWriter(viewModel, statusArgument["--status-file=".Length..]);
        Loaded += (_, _) => Dispatcher.BeginInvoke(
            () => viewModel.StartInitialLoad(arguments.Contains("--update-data"), arguments.Contains("--start-capture")),
            DispatcherPriority.ContextIdle);
    }

    private void MacroRunningChanged(object? sender, EventArgs e)
    {
        if (viewModel.Macros.IsRunning)
        {
            viewModel.Macros.ReportStopKey(stopMacroHotkey.Register());
        }
        else
        {
            stopMacroHotkey.Unregister();
        }
    }

    private void MacroEndNotice(object? sender, MacroEndNotice notice)
    {
        noticeOpensMacroPage = true;
        notifier.Show(notice.Title, notice.Detail, notice.IsWarning);
    }

    private void SortieNotice(object? sender, SortieRunNotice notice)
    {
        noticeOpensMacroPage = false;
        notifier.Show(notice.Title, notice.Detail, notice.IsWarning);
    }

    // A click on the notification or the notification-area icon brings the window back, on the macro page when the
    // last notification was about a macro run.
    private void ShowNoticeSource()
    {
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Show();
        Activate();
        if (noticeOpensMacroPage) viewModel.SelectedMainTabIndex = MainWindowViewModel.MacroPageIndex;
    }

    private void ViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainWindowViewModel.IsShipDetailOpen))
        {
            AnimateShipDetail(viewModel.IsShipDetailOpen);
        }
    }

    private void AnimateShipDetail(bool show)
    {
        ShipDetailOverlay.IsHitTestVisible = show;

        var overlayAnimation = new DoubleAnimation
        {
            To = show ? 1 : 0,
            Duration = TimeSpan.FromMilliseconds(show ? 180 : 140)
        };
        ShipDetailOverlay.BeginAnimation(OpacityProperty, overlayAnimation);

        var panelAnimation = new DoubleAnimation
        {
            To = show ? 0 : 760,
            Duration = TimeSpan.FromMilliseconds(show ? 220 : 160),
            EasingFunction = new CubicEase
            {
                EasingMode = show ? EasingMode.EaseOut : EasingMode.EaseIn
            }
        };
        ShipDetailPanelTransform.BeginAnimation(System.Windows.Media.TranslateTransform.XProperty, panelAnimation);
    }

    protected override void OnClosed(EventArgs e)
    {
        DisposeViewModel();

        base.OnClosed(e);
        Application.Current.Shutdown();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        DisposeViewModel();
        base.OnClosing(e);
    }

    private void DisposeViewModel()
    {
        if (isViewModelDisposed)
        {
            return;
        }

        isViewModelDisposed = true;
        viewModel.PropertyChanged -= ViewModelPropertyChanged;
        viewModel.Macros.RunningChanged -= MacroRunningChanged;
        viewModel.Macros.EndNotice -= MacroEndNotice;
        viewModel.SortieNotice -= SortieNotice;
        stopMacroHotkey.Dispose();
        notifier.Dispose();
        viewModel.Dispose();
        operationStatusWriter?.Dispose();
        DataContext = null;
    }
}
