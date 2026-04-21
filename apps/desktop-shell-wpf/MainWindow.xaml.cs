using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Interop;
using DesktopCompanion.WpfHost.Services;
using DesktopCompanion.WpfHost.ViewModels;

namespace DesktopCompanion.WpfHost;

public partial class MainWindow : Window
{
    private const double HostWidth = 540;
    private static readonly string DiagnosticLogPath = Path.Combine(
        AppContext.BaseDirectory,
        "window-diagnostics.log");
    private Point _pendingShellGestureStart;
    private bool _hasPendingShellGesture;
    private bool _suppressNextToggleClick;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainWindowViewModel();
        ContentRendered += OnContentRendered;
        Deactivated += OnWindowDeactivated;
        SizeChanged += OnWindowSizeChanged;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        LogWindowMetrics("loaded:before-layout");
        var isOpen = (DataContext as MainWindowViewModel)?.IsPanelOpen ?? false;
        Width = HostWidth;
        Height = 470;
        MinWidth = HostWidth;
        MinHeight = Height;
        WindowPlacementService.SnapToBottomRight(this);
        ApplyPanelVisualState(animated: false);

        if (isOpen)
        {
            _ = Dispatcher.BeginInvoke(FocusInputBox);
        }

        _ = Dispatcher.BeginInvoke(new Action(() => LogWindowMetrics("loaded:dispatcher")));
    }

    private void OnDragSurfaceMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var source = e.OriginalSource as DependencyObject;
        if (!CanStartShellGesture(source))
        {
            return;
        }

        if (DataContext is MainWindowViewModel viewModel && !viewModel.IsPanelOpen)
        {
            _pendingShellGestureStart = e.GetPosition(this);
            _hasPendingShellGesture = true;
            return;
        }

        _hasPendingShellGesture = false;
        if (IsShellToggleTarget(source))
        {
            return;
        }

        if (IsInteractiveClickTarget(source) && !IsShellToggleTarget(source))
        {
            return;
        }

        try
        {
            DragMove();
            SyncWindowPlacementFromNativeRect();
        }
        catch
        {
        }
    }

    private void OnDragSurfaceMouseMove(object sender, MouseEventArgs e)
    {
        if (!_hasPendingShellGesture || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        if (DataContext is not MainWindowViewModel viewModel || viewModel.IsPanelOpen)
        {
            _hasPendingShellGesture = false;
            return;
        }

        var currentPosition = e.GetPosition(this);
        var movedX = Math.Abs(currentPosition.X - _pendingShellGestureStart.X);
        var movedY = Math.Abs(currentPosition.Y - _pendingShellGestureStart.Y);

        if (movedX < SystemParameters.MinimumHorizontalDragDistance
            && movedY < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        _hasPendingShellGesture = false;
        _suppressNextToggleClick = true;

        try
        {
            DragMove();
            SyncWindowPlacementFromNativeRect();
            LogWindowMetrics("shell:dragged");
        }
        catch
        {
        }
        finally
        {
            _ = Dispatcher.BeginInvoke(new Action(() => _suppressNextToggleClick = false));
        }

        e.Handled = true;
    }

    private void OnDragSurfaceMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _hasPendingShellGesture = false;
    }

    private void OnTogglePanelClicked(object sender, RoutedEventArgs e)
    {
        if (_suppressNextToggleClick)
        {
            _suppressNextToggleClick = false;
            return;
        }

        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        viewModel.TogglePanel();
        LogWindowMetrics(viewModel.IsPanelOpen ? "toggle:open" : "toggle:closed");
        AnimateShell(viewModel.IsPanelOpen);

        if (viewModel.IsPanelOpen)
        {
            _ = Dispatcher.BeginInvoke(FocusInputBox);
        }
    }

    private async void OnSubmitInputClicked(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        await viewModel.SendChatMessageAsync();
        _ = Dispatcher.BeginInvoke(FocusInputBox);
    }

    private async void OnSuggestionBubbleClicked(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel
            || sender is not Button button
            || button.Tag is not string suggestion)
        {
            return;
        }

        await viewModel.UseSuggestionAsync(suggestion);
        _ = Dispatcher.BeginInvoke(FocusInputBox);
    }

    private void OnReviewClicked(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        viewModel.ReviewTasks();
    }

    private void OnToggleSupervisionClicked(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        viewModel.ToggleSupervision();
    }

    private void OnStartFocusSprintClicked(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        viewModel.StartFocusSprint();
    }

    private void OnPermissionsMenuClicked(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        viewModel.ReopenPermissionSetup();
        LogWindowMetrics("menu:permissions");
        AnimateShell(viewModel.IsPanelOpen);
        _ = Dispatcher.BeginInvoke(FocusInputBox);
    }

    private void OnOpenRecentWorkspaceInVsCodeMenuClicked(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        MessageBox.Show(
            this,
            viewModel.OpenLatestWorkspaceInVsCode(),
            "团子 × VS Code",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void OnPermissionSummaryMenuClicked(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        MessageBox.Show(
            this,
            viewModel.GetPermissionSummary(),
            "团子权限",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void OnClearWorkspaceSourcesMenuClicked(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        viewModel.ClearWorkspaceAuthorizations();
        LogWindowMetrics("menu:clear-workspaces");
        AnimateShell(viewModel.IsPanelOpen);
    }

    private void OnRestartOnboardingMenuClicked(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        viewModel.RestartOnboarding();
        LogWindowMetrics("menu:restart-onboarding");
        AnimateShell(viewModel.IsPanelOpen);
        _ = Dispatcher.BeginInvoke(FocusInputBox);
    }

    private void OnExitMenuClicked(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private async void OnNaturalInputKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || Keyboard.Modifiers != ModifierKeys.None)
        {
            return;
        }

        e.Handled = true;
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        await viewModel.SendChatMessageAsync();
        _ = Dispatcher.BeginInvoke(FocusInputBox);
    }

    private void OnQuickComposerFocused(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel || viewModel.IsPanelOpen)
        {
            return;
        }

        viewModel.TogglePanel();
        LogWindowMetrics("composer:open");
        AnimateShell(viewModel.IsPanelOpen);
        _ = Dispatcher.BeginInvoke(FocusInputBox);
    }

    private void OnWindowDeactivated(object? sender, EventArgs e)
    {
        ClosePanelIfOpen("window:deactivated-close");
    }

    private void AnimateShell(bool isOpen)
    {
        _ = isOpen;
        SyncWindowPlacementFromNativeRect();
        BeginAnimation(WidthProperty, null);
        BeginAnimation(LeftProperty, null);
        Width = HostWidth;
        ApplyPanelVisualState(animated: false);
    }

    private void ApplyPanelVisualState(bool animated)
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        _ = animated;
        var opacityTarget = viewModel.IsPanelOpen ? 1d : 0d;
        var xTarget = viewModel.IsPanelOpen ? 0d : -24d;

        TaskPanel.IsHitTestVisible = viewModel.IsPanelOpen;
        ComposerBubble.IsHitTestVisible = viewModel.IsPanelOpen;
        ClosedShellClickCatcher.IsHitTestVisible = !viewModel.IsPanelOpen;

        TaskPanel.BeginAnimation(OpacityProperty, null);
        TaskPanelTransform.BeginAnimation(TranslateTransform.XProperty, null);
        ComposerBubble.BeginAnimation(OpacityProperty, null);

        TaskPanel.Opacity = opacityTarget;
        TaskPanelTransform.X = xTarget;
        TaskPanel.Visibility = viewModel.IsPanelOpen ? Visibility.Visible : Visibility.Hidden;

        ComposerBubble.Opacity = opacityTarget;
        ComposerBubble.Visibility = viewModel.IsPanelOpen ? Visibility.Visible : Visibility.Hidden;

        ClosedShellClickCatcher.Visibility = viewModel.IsPanelOpen ? Visibility.Hidden : Visibility.Visible;
    }

    private bool CanStartShellGesture(DependencyObject? source)
    {
        return IsShellToggleTarget(source) || !IsInteractiveClickTarget(source);
    }

    private static bool IsInteractiveClickTarget(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is ButtonBase
                || source is TextBoxBase
                || source is PasswordBox
                || source is ScrollBar
                || source is Slider
                || source is ComboBox
                || source is ListBoxItem)
            {
                return true;
            }

            source = VisualTreeHelper.GetParent(source);
        }

        return false;
    }

    private bool IsShellToggleTarget(DependencyObject? source)
    {
        while (source is not null)
        {
            if (ReferenceEquals(source, PetToggleButton) || ReferenceEquals(source, ClosedShellClickCatcher))
            {
                return true;
            }

            source = VisualTreeHelper.GetParent(source);
        }

        return false;
    }

    private void OnContentRendered(object? sender, EventArgs e)
    {
        LogWindowMetrics("content-rendered");
    }

    private void FocusInputBox()
    {
        if (!QuickComposerTextBox.IsVisible)
        {
            return;
        }

        QuickComposerTextBox.Focus();
        Keyboard.Focus(QuickComposerTextBox);
        QuickComposerTextBox.CaretIndex = QuickComposerTextBox.Text.Length;
    }

    private void OnWindowSizeChanged(object sender, SizeChangedEventArgs e)
    {
        LogWindowMetrics($"size-changed:{e.NewSize.Width:0.##}x{e.NewSize.Height:0.##}");
    }

    private void ClosePanelIfOpen(string stage)
    {
        if (DataContext is not MainWindowViewModel viewModel || !viewModel.IsPanelOpen)
        {
            return;
        }

        SyncWindowPlacementFromNativeRect();
        viewModel.TogglePanel();
        LogWindowMetrics(stage);
        AnimateShell(false);
    }

    private void SyncWindowPlacementFromNativeRect()
    {
        if (!TryGetCurrentWindowBounds(out var nativeLeft, out var nativeTop, out _, out _))
        {
            return;
        }

        Left = nativeLeft;
        Top = nativeTop;
    }

    private bool TryGetCurrentWindowBounds(out double left, out double top, out double width, out double height)
    {
        left = Left;
        top = Top;
        width = ActualWidth > 0 ? ActualWidth : Width;
        height = ActualHeight > 0 ? ActualHeight : Height;

        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero || !NativeWindowMethods.TryGetWindowRect(hwnd, out var rect))
        {
            return false;
        }

        var dpi = VisualTreeHelper.GetDpi(this);
        left = rect.Left / dpi.DpiScaleX;
        top = rect.Top / dpi.DpiScaleY;
        width = rect.Width / dpi.DpiScaleX;
        height = rect.Height / dpi.DpiScaleY;
        return true;
    }

    [Conditional("DEBUG")]
    private void LogWindowMetrics(string stage)
    {
        try
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            var handleText = hwnd == IntPtr.Zero ? "0" : hwnd.ToString();
            var rectText = "unavailable";

            if (hwnd != IntPtr.Zero && NativeWindowMethods.TryGetWindowRect(hwnd, out var rect))
            {
                rectText = $"{rect.Width}x{rect.Height} @ ({rect.Left},{rect.Top})";
            }

            var line =
                $"{DateTime.Now:O} | {stage} | Width={Width:0.##} Height={Height:0.##} | Actual={ActualWidth:0.##}x{ActualHeight:0.##} | Min={MinWidth:0.##}x{MinHeight:0.##} | Left={Left:0.##} Top={Top:0.##} | WindowState={WindowState} | Handle={handleText} | NativeRect={rectText}";

            File.AppendAllLines(DiagnosticLogPath, [line]);
            Debug.WriteLine(line);
        }
        catch
        {
        }
    }
}
