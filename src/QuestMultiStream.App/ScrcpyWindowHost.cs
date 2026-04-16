using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace QuestMultiStream.App;

public sealed class ScrcpyWindowHost : HwndHost
{
    private const int GwlpHwndParent = -8;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpFrameChanged = 0x0020;
    private const uint SwpShowWindow = 0x0040;
    private static readonly nint SsBlackRect = 0x00000004;
    private static readonly nint WsChild = unchecked((nint)0x40000000);
    private static readonly nint WsVisible = unchecked((nint)0x10000000);
    private static readonly nint WsClipSiblings = unchecked((nint)0x04000000);
    private static readonly nint WsClipChildren = unchecked((nint)0x02000000);

    private IntPtr _hostHandle;
    private IntPtr _attachedWindow;
    private nint _originalOwnerHandle;
    private bool _ownerCaptured;

    public bool HasAttachedWindow => _attachedWindow != IntPtr.Zero && NativeMethods.IsWindow(_attachedWindow);

    public void RefreshAttachedWindowBounds()
        => DockAttachedWindow();

    public void AttachExternalWindow(IntPtr windowHandle)
    {
        if (_hostHandle == IntPtr.Zero ||
            windowHandle == IntPtr.Zero ||
            !NativeMethods.IsWindow(windowHandle))
        {
            return;
        }

        if (windowHandle != _attachedWindow)
        {
            DetachExternalWindow();
            _attachedWindow = windowHandle;
        }

        DockAttachedWindow();
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(RefreshAttachedWindowBounds));
        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(RefreshAttachedWindowBounds));
    }

    public void DetachExternalWindow()
    {
        if (_attachedWindow != IntPtr.Zero && NativeMethods.IsWindow(_attachedWindow))
        {
            if (_ownerCaptured)
            {
                NativeMethods.SetWindowLongPtr(_attachedWindow, GwlpHwndParent, _originalOwnerHandle);
                NativeMethods.SetWindowPos(
                    _attachedWindow,
                    IntPtr.Zero,
                    0,
                    0,
                    0,
                    0,
                    SwpNoZOrder | SwpNoActivate | SwpFrameChanged);
            }

            NativeMethods.ShowWindow(_attachedWindow, NativeMethods.SwShow);
        }

        _attachedWindow = IntPtr.Zero;
        _originalOwnerHandle = IntPtr.Zero;
        _ownerCaptured = false;
    }

    protected override HandleRef BuildWindowCore(HandleRef hwndParent)
    {
        _hostHandle = NativeMethods.CreateWindowEx(
            IntPtr.Zero,
            "static",
            string.Empty,
            WsChild | WsVisible | WsClipSiblings | WsClipChildren | SsBlackRect,
            0,
            0,
            1,
            1,
            hwndParent.Handle,
            IntPtr.Zero,
            IntPtr.Zero,
            IntPtr.Zero);

        return new HandleRef(this, _hostHandle);
    }

    protected override void DestroyWindowCore(HandleRef hwnd)
    {
        DetachExternalWindow();

        if (hwnd.Handle != IntPtr.Zero)
        {
            NativeMethods.DestroyWindow(hwnd.Handle);
        }

        _hostHandle = IntPtr.Zero;
    }

    protected override void OnWindowPositionChanged(Rect rcBoundingBox)
    {
        base.OnWindowPositionChanged(rcBoundingBox);
        DockAttachedWindow();
    }

    private void DockAttachedWindow()
    {
        if (_hostHandle == IntPtr.Zero ||
            _attachedWindow == IntPtr.Zero ||
            !NativeMethods.IsWindow(_attachedWindow))
        {
            return;
        }

        var ownerWindow = Window.GetWindow(this);
        if (ownerWindow is not null &&
            (ownerWindow.WindowState == WindowState.Minimized || !ownerWindow.IsVisible))
        {
            NativeMethods.ShowWindow(_attachedWindow, NativeMethods.SwHide);
            return;
        }

        var ownerHandle = GetOwnerWindowHandle();
        if (ownerHandle != IntPtr.Zero)
        {
            if (!_ownerCaptured)
            {
                _originalOwnerHandle = NativeMethods.GetWindowLongPtr(_attachedWindow, GwlpHwndParent);
                _ownerCaptured = true;
            }

            var currentOwnerHandle = NativeMethods.GetWindowLongPtr(_attachedWindow, GwlpHwndParent);
            if (currentOwnerHandle != ownerHandle)
            {
                NativeMethods.SetWindowLongPtr(_attachedWindow, GwlpHwndParent, ownerHandle);
            }
        }

        if (!NativeMethods.GetWindowRect(_hostHandle, out var hostRect))
        {
            return;
        }

        var width = Math.Max(1, hostRect.Right - hostRect.Left);
        var height = Math.Max(1, hostRect.Bottom - hostRect.Top);

        NativeMethods.ShowWindow(_attachedWindow, NativeMethods.SwShowNa);
        NativeMethods.SetWindowPos(
            _attachedWindow,
            IntPtr.Zero,
            hostRect.Left,
            hostRect.Top,
            width,
            height,
            SwpNoZOrder | SwpNoActivate | SwpFrameChanged | SwpShowWindow);
    }

    private IntPtr GetOwnerWindowHandle()
    {
        var window = Window.GetWindow(this);
        if (window is not null)
        {
            return new WindowInteropHelper(window).Handle;
        }

        return PresentationSource.FromVisual(this) is HwndSource source
            ? source.Handle
            : IntPtr.Zero;
    }

    private static class NativeMethods
    {
        public const int SwHide = 0;
        public const int SwShow = 5;
        public const int SwShowNa = 8;

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern IntPtr CreateWindowEx(
            IntPtr exStyle,
            string className,
            string windowName,
            nint style,
            int x,
            int y,
            int width,
            int height,
            IntPtr parentHandle,
            IntPtr menuHandle,
            IntPtr instanceHandle,
            IntPtr parameter);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool DestroyWindow(IntPtr windowHandle);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetWindowRect(IntPtr windowHandle, out RECT rect);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IsWindow(IntPtr windowHandle);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetWindowPos(
            IntPtr windowHandle,
            IntPtr insertAfter,
            int x,
            int y,
            int width,
            int height,
            uint flags);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool ShowWindow(IntPtr windowHandle, int command);

        public static nint GetWindowLongPtr(IntPtr windowHandle, int index)
            => IntPtr.Size == 8
                ? GetWindowLongPtr64(windowHandle, index)
                : GetWindowLong32(windowHandle, index);

        public static nint SetWindowLongPtr(IntPtr windowHandle, int index, nint newValue)
            => IntPtr.Size == 8
                ? SetWindowLongPtr64(windowHandle, index, newValue)
                : SetWindowLong32(windowHandle, index, newValue);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
        private static extern nint GetWindowLongPtr64(IntPtr windowHandle, int index);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)]
        private static extern nint GetWindowLong32(IntPtr windowHandle, int index);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
        private static extern nint SetWindowLongPtr64(IntPtr windowHandle, int index, nint newValue);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
        private static extern nint SetWindowLong32(IntPtr windowHandle, int index, nint newValue);
    }
}
