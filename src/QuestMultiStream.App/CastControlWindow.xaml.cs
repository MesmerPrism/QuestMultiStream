using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using QuestMultiStream.App.ViewModels;
using QuestMultiStream.Core.Models;
using QuestMultiStream.Core.Services;

namespace QuestMultiStream.App;

public partial class CastControlWindow : Window
{
    private const int WmNcHitTest = 0x0084;
    private static readonly IntPtr HtTransparent = new(-1);

    private readonly DispatcherTimer _followTimer;
    private DeviceRowViewModel _row;
    private QuestCastSession _session;
    private WindowLayoutBounds? _pendingSessionBounds;
    private bool _managerClosing;
    private bool _closeRequested;
    private bool _syncingFromSession;
    private bool _applyingOverlayBounds;
    private bool _isFullscreen;
    private bool _isResizingWithGrip;
    private IntPtr _windowHandle;
    private IntPtr _ownedSessionHandle;
    private WindowLayoutBounds? _restoreBounds;

    public CastControlWindow(DeviceRowViewModel row, QuestCastSession session)
    {
        InitializeComponent();
        WindowThemeHelper.Attach(this);

        _row = row ?? throw new ArgumentNullException(nameof(row));
        _session = session ?? throw new ArgumentNullException(nameof(session));
        DataContext = row;
        Left = -10000;
        Top = -10000;

        _followTimer = new DispatcherTimer(
            TimeSpan.FromMilliseconds(120),
            DispatcherPriority.Background,
            OnFollowTimerTick,
            Dispatcher);

        SubscribeRow(_row);

        SourceInitialized += OnSourceInitialized;
        Loaded += OnLoaded;
        LocationChanged += OnOverlayBoundsChanged;
        SizeChanged += OnOverlayBoundsChanged;
        Closing += OnClosing;
        Closed += OnClosed;
    }

    public event EventHandler<string>? CloseRequested;
    public event EventHandler<WindowLayoutBounds>? ResizeRequested;

    public string Serial => _row.Serial;

    public void Attach(DeviceRowViewModel row, QuestCastSession session)
    {
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(session);

        var previousSession = _session;
        if (!ReferenceEquals(_row, row))
        {
            UnsubscribeRow(_row);
            _row = row;
            SubscribeRow(_row);
            DataContext = row;
        }

        if (!ReferenceEquals(previousSession, session) &&
            previousSession.TryGetBounds(out var previousBounds))
        {
            _pendingSessionBounds = previousBounds;
        }

        _session = session;
        UpdateWindowState();
        SyncToSessionWindow();
        _followTimer.Start();
    }

    public void CloseFromManager()
    {
        _managerClosing = true;
        Close();
    }

    public void ApplyBounds(WindowLayoutBounds bounds)
    {
        _pendingSessionBounds = bounds;
        SyncToSessionWindow();
    }

    public bool TryMove(WindowLayoutBounds bounds)
    {
        _pendingSessionBounds = bounds;
        return _session.TryMove(bounds);
    }

    public bool TryGetWindowBounds(out WindowLayoutBounds bounds)
        => _session.TryGetBounds(out bounds);

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        _windowHandle = new WindowInteropHelper(this).Handle;
        if (HwndSource.FromHwnd(_windowHandle) is { } source)
        {
            source.AddHook(WndProc);
        }

        TrySetOwner();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        UpdateWindowState();
        SyncToSessionWindow();
        _followTimer.Start();
    }

    private void OnOverlayBoundsChanged(object? sender, EventArgs e)
    {
        if (_syncingFromSession || _applyingOverlayBounds || !IsLoaded || _isResizingWithGrip)
        {
            return;
        }

        _isFullscreen = false;
        ApplyOverlayBoundsToSession();
        UpdateWindowState();
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_managerClosing || !_session.IsActive)
        {
            return;
        }

        e.Cancel = true;
        if (_closeRequested)
        {
            return;
        }

        _closeRequested = true;
        IsEnabled = false;
        CloseRequested?.Invoke(this, _row.Serial);
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _followTimer.Stop();
        UnsubscribeRow(_row);

        SourceInitialized -= OnSourceInitialized;
        Loaded -= OnLoaded;
        LocationChanged -= OnOverlayBoundsChanged;
        SizeChanged -= OnOverlayBoundsChanged;
        Closing -= OnClosing;
        Closed -= OnClosed;
    }

    private void OnFollowTimerTick(object? sender, EventArgs e)
        => SyncToSessionWindow();

    private void SyncToSessionWindow()
    {
        if (!IsLoaded || _isResizingWithGrip)
        {
            return;
        }

        if (!_session.IsActive || !_session.RefreshWindowHandle() || _session.WindowHandle == IntPtr.Zero)
        {
            if (IsVisible)
            {
                Hide();
            }

            return;
        }

        TrySetOwner();

        if (_pendingSessionBounds is { } pendingBounds && _session.TryMove(pendingBounds))
        {
            _pendingSessionBounds = null;
        }

        if (_session.IsWindowMinimized() || !_session.IsWindowVisible())
        {
            if (IsVisible)
            {
                Hide();
            }

            return;
        }

        if (!_session.TryGetBounds(out var sessionBounds))
        {
            return;
        }

        var overlayBounds = ConvertDevicePixelsToDipBounds(sessionBounds);
        UpdateWindowState();

        _syncingFromSession = true;
        try
        {
            Left = overlayBounds.X;
            Top = overlayBounds.Y;
            Width = Math.Max(MinWidth, overlayBounds.Width);
            Height = Math.Max(MinHeight, overlayBounds.Height);
        }
        finally
        {
            _syncingFromSession = false;
        }

        if (!IsVisible)
        {
            Show();
        }
    }

    private void ApplyOverlayBoundsToSession()
    {
        if (!_session.IsActive)
        {
            return;
        }

        var bounds = new WindowLayoutBounds(
            (int)Math.Round(Left),
            (int)Math.Round(Top),
            Math.Max((int)MinWidth, (int)Math.Round(Width)),
            Math.Max((int)MinHeight, (int)Math.Round(Height)));
        var deviceBounds = ConvertDipBoundsToDevicePixels(bounds);

        if (!_isFullscreen)
        {
            _restoreBounds = deviceBounds;
        }

        _applyingOverlayBounds = true;
        try
        {
            _session.TryMove(deviceBounds);
            _session.TrySetTopmost(_row.IsCastWindowPinned);
        }
        finally
        {
            _applyingOverlayBounds = false;
        }
    }

    private void TrySetOwner()
    {
        if (_windowHandle == IntPtr.Zero ||
            _session.WindowHandle == IntPtr.Zero ||
            _session.WindowHandle == _ownedSessionHandle)
        {
            return;
        }

        SetWindowLongPtr(_windowHandle, GwlpHwndParent, _session.WindowHandle);
        _ownedSessionHandle = _session.WindowHandle;
    }

    private void SubscribeRow(DeviceRowViewModel row)
        => row.PropertyChanged += OnRowPropertyChanged;

    private void UnsubscribeRow(DeviceRowViewModel row)
        => row.PropertyChanged -= OnRowPropertyChanged;

    private void OnRowPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(DeviceRowViewModel.IsCastWindowPinned) or
            nameof(DeviceRowViewModel.DisplayName) or
            nameof(DeviceRowViewModel.SelectedCaptureTargetText) or
            nameof(DeviceRowViewModel.SessionDetailText) or
            nameof(DeviceRowViewModel.ProximityStatusText))
        {
            UpdateWindowState();
            if (e.PropertyName == nameof(DeviceRowViewModel.IsCastWindowPinned))
            {
                _session.TrySetTopmost(_row.IsCastWindowPinned);
            }
        }
    }

    private void UpdateWindowState()
    {
        Title = $"{_row.DisplayName} · {_row.SelectedCaptureTargetText}";
        Topmost = _row.IsCastWindowPinned;
        if (ToggleMaximizeWindowButton is not null)
        {
            ToggleMaximizeWindowButton.Content = _isFullscreen ? "\u2750" : "\u25A1";
            ToggleMaximizeWindowButton.ToolTip = _isFullscreen ? "Restore cast window" : "Full size cast window";
        }
    }

    private void OnMinimizeWindowClicked(object sender, RoutedEventArgs e)
    {
        _session.TryMinimizeWindow();
        Hide();
    }

    private void OnToggleMaximizeWindowClicked(object sender, RoutedEventArgs e)
    {
        if (_isFullscreen)
        {
            _session.TryRestoreWindow();
            if (_restoreBounds is { } restoreBounds)
            {
                ResizeRequested?.Invoke(this, restoreBounds);
            }

            _isFullscreen = false;
            UpdateWindowState();
            return;
        }

        if (_session.TryGetBounds(out var currentBounds))
        {
            _restoreBounds = currentBounds;
        }

        var workArea = SystemParameters.WorkArea;
        var deviceWorkArea = ConvertDipBoundsToDevicePixels(new WindowLayoutBounds(
            (int)Math.Round(workArea.Left),
            (int)Math.Round(workArea.Top),
            Math.Max((int)MinWidth, (int)Math.Round(workArea.Width)),
            Math.Max((int)MinHeight, (int)Math.Round(workArea.Height))));
        _session.TryRestoreWindow();
        ResizeRequested?.Invoke(this, deviceWorkArea);
        _isFullscreen = true;
        UpdateWindowState();
    }

    private void OnCloseWindowClicked(object sender, RoutedEventArgs e)
    {
        if (_row.StopCastCommand.CanExecute(null))
        {
            _row.StopCastCommand.Execute(null);
        }
    }

    private void OnHeaderMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || IsInteractiveHeaderSource(e.OriginalSource as DependencyObject))
        {
            return;
        }

        try
        {
            DragMove();
        }
        catch (InvalidOperationException)
        {
        }
    }

    private bool IsInteractiveHeaderSource(DependencyObject? source)
        => FindAncestor<Button>(source) is not null ||
           FindAncestor<ComboBox>(source) is not null ||
           FindAncestor<TextBox>(source) is not null;

    private static TAncestor? FindAncestor<TAncestor>(DependencyObject? source)
        where TAncestor : DependencyObject
    {
        while (source is not null)
        {
            if (source is TAncestor matched)
            {
                return matched;
            }

            source = VisualTreeHelper.GetParent(source);
        }

        return null;
    }

    private void OnResizeGripDragDelta(object sender, DragDeltaEventArgs e)
    {
        if (sender is not Thumb thumb)
        {
            return;
        }

        var left = Left;
        var top = Top;
        var width = Width;
        var height = Height;

        switch (thumb.Name)
        {
            case nameof(LeftGrip):
                left += e.HorizontalChange;
                width -= e.HorizontalChange;
                break;
            case nameof(RightGrip):
                width += e.HorizontalChange;
                break;
            case nameof(TopGrip):
                top += e.VerticalChange;
                height -= e.VerticalChange;
                break;
            case nameof(BottomGrip):
                height += e.VerticalChange;
                break;
            case nameof(TopLeftGrip):
                left += e.HorizontalChange;
                width -= e.HorizontalChange;
                top += e.VerticalChange;
                height -= e.VerticalChange;
                break;
            case nameof(TopRightGrip):
                width += e.HorizontalChange;
                top += e.VerticalChange;
                height -= e.VerticalChange;
                break;
            case nameof(BottomLeftGrip):
                left += e.HorizontalChange;
                width -= e.HorizontalChange;
                height += e.VerticalChange;
                break;
            case nameof(BottomRightGrip):
                width += e.HorizontalChange;
                height += e.VerticalChange;
                break;
        }

        if (width < MinWidth)
        {
            if (thumb.Name.Contains("Left", StringComparison.Ordinal))
            {
                left -= MinWidth - width;
            }

            width = MinWidth;
        }

        if (height < MinHeight)
        {
            if (thumb.Name.Contains("Top", StringComparison.Ordinal))
            {
                top -= MinHeight - height;
            }

            height = MinHeight;
        }

        _syncingFromSession = true;
        try
        {
            Left = left;
            Top = top;
            Width = width;
            Height = height;
        }
        finally
        {
            _syncingFromSession = false;
        }
    }

    private void OnResizeGripDragStarted(object sender, DragStartedEventArgs e)
    {
        _isResizingWithGrip = true;
        if (_session.TryGetBounds(out var currentBounds))
        {
            _restoreBounds = currentBounds;
        }
    }

    private void OnResizeGripDragCompleted(object sender, DragCompletedEventArgs e)
    {
        _isResizingWithGrip = false;
        _isFullscreen = false;
        var bounds = ConvertDipBoundsToDevicePixels(new WindowLayoutBounds(
            (int)Math.Round(Left),
            (int)Math.Round(Top),
            Math.Max((int)MinWidth, (int)Math.Round(Width)),
            Math.Max((int)MinHeight, (int)Math.Round(Height))));
        _restoreBounds = bounds;
        ResizeRequested?.Invoke(this, bounds);
        UpdateWindowState();
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != WmNcHitTest)
        {
            return IntPtr.Zero;
        }

        var x = unchecked((short)(lParam.ToInt64() & 0xFFFF));
        var y = unchecked((short)((lParam.ToInt64() >> 16) & 0xFFFF));
        var point = PointFromScreen(new Point(x, y));

        if (IsOverlayInteractivePoint(point))
        {
            return IntPtr.Zero;
        }

        handled = true;
        return HtTransparent;
    }

    private bool IsOverlayInteractivePoint(Point point)
    {
        const double edgeBand = 12;

        if (point.X <= edgeBand || point.Y <= edgeBand || point.X >= ActualWidth - edgeBand || point.Y >= ActualHeight - edgeBand)
        {
            return true;
        }

        var headerBounds = HeaderChrome.TransformToAncestor(this)
            .TransformBounds(new Rect(new Point(0, 0), HeaderChrome.RenderSize));

        return headerBounds.Contains(point);
    }

    private WindowLayoutBounds ConvertDevicePixelsToDipBounds(WindowLayoutBounds bounds)
    {
        if (PresentationSource.FromVisual(this) is not HwndSource source || source.CompositionTarget is null)
        {
            return bounds;
        }

        var transform = source.CompositionTarget.TransformFromDevice;
        var topLeft = transform.Transform(new Point(bounds.X, bounds.Y));
        var bottomRight = transform.Transform(new Point(bounds.X + bounds.Width, bounds.Y + bounds.Height));

        return new WindowLayoutBounds(
            (int)Math.Round(topLeft.X),
            (int)Math.Round(topLeft.Y),
            Math.Max(1, (int)Math.Round(bottomRight.X - topLeft.X)),
            Math.Max(1, (int)Math.Round(bottomRight.Y - topLeft.Y)));
    }

    private WindowLayoutBounds ConvertDipBoundsToDevicePixels(WindowLayoutBounds bounds)
    {
        if (PresentationSource.FromVisual(this) is not HwndSource source || source.CompositionTarget is null)
        {
            return bounds;
        }

        var transform = source.CompositionTarget.TransformToDevice;
        var topLeft = transform.Transform(new Point(bounds.X, bounds.Y));
        var bottomRight = transform.Transform(new Point(bounds.X + bounds.Width, bounds.Y + bounds.Height));

        return new WindowLayoutBounds(
            (int)Math.Round(topLeft.X),
            (int)Math.Round(topLeft.Y),
            Math.Max(1, (int)Math.Round(bottomRight.X - topLeft.X)),
            Math.Max(1, (int)Math.Round(bottomRight.Y - topLeft.Y)));
    }

    private const int GwlpHwndParent = -8;

    private static nint SetWindowLongPtr(IntPtr windowHandle, int index, nint newValue)
        => IntPtr.Size == 8
            ? SetWindowLongPtr64(windowHandle, index, newValue)
            : SetWindowLong32(windowHandle, index, newValue);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern nint SetWindowLongPtr64(IntPtr windowHandle, int index, nint newValue);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
    private static extern nint SetWindowLong32(IntPtr windowHandle, int index, nint newValue);
}
