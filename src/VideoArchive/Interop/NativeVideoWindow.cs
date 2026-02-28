using System.Runtime.InteropServices;

namespace VideoArchive.Interop;

/// <summary>
/// Creates and manages an owned popup window that LibVLC can render video into.
/// WinUI 3 renders XAML via DirectComposition which sits above traditional Win32
/// child windows, so we must use WS_POPUP (not WS_CHILD) to appear on top.
/// The popup is owned by the main window so it stays in front and moves with it.
/// </summary>
public sealed class NativeVideoWindow : IDisposable
{
    private const string ClassName = "LibVLCVideoPopup";
    private static bool _classRegistered;
    private IntPtr _hwnd;
    private readonly WndProcDelegate _wndProcDelegate;
    private readonly IntPtr _ownerHwnd;

    // P/Invoke declarations
    private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern ushort RegisterClassW(ref WNDCLASS lpWndClass);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateWindowExW(
        uint dwExStyle, string lpClassName, string lpWindowName, uint dwStyle,
        int x, int y, int nWidth, int nHeight,
        IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
        int x, int y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern IntPtr DefWindowProcW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandleW(string? lpModuleName);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ClientToScreen(IntPtr hWnd, ref POINT lpPoint);

    // Window styles
    private const uint WS_POPUP = 0x80000000;
    private const uint WS_CLIPSIBLINGS = 0x04000000;
    private const uint WS_CLIPCHILDREN = 0x02000000;
    private const uint WS_EX_NOACTIVATE = 0x08000000;
    private const uint WS_EX_TOOLWINDOW = 0x00000080; // Hide from taskbar/Alt+Tab
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_NOZORDER = 0x0004;
    private const int SW_SHOWNOACTIVATE = 8;
    private const int SW_HIDE = 0;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASS
    {
        public uint style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        public string? lpszMenuName;
        public string lpszClassName;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X;
        public int Y;
    }

    public IntPtr Hwnd => _hwnd;

    public NativeVideoWindow(IntPtr ownerHwnd)
    {
        _ownerHwnd = ownerHwnd;
        // Must keep delegate alive to prevent GC collection
        _wndProcDelegate = WndProc;

        EnsureClassRegistered();

        // Create as an owned popup — appears above WinUI 3 composition layer
        _hwnd = CreateWindowExW(
            WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW,
            ClassName,
            "LibVLC Video Surface",
            WS_POPUP | WS_CLIPSIBLINGS | WS_CLIPCHILDREN,
            0, 0, 1, 1,
            ownerHwnd, // Owner window (not parent — this is a popup)
            IntPtr.Zero,
            GetModuleHandleW(null),
            IntPtr.Zero);
    }

    private void EnsureClassRegistered()
    {
        if (_classRegistered) return;

        var wc = new WNDCLASS
        {
            style = 0,
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProcDelegate),
            hInstance = GetModuleHandleW(null),
            hbrBackground = IntPtr.Zero,
            lpszClassName = ClassName,
        };

        RegisterClassW(ref wc);
        _classRegistered = true;
    }

    private static IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        return DefWindowProcW(hWnd, msg, wParam, lParam);
    }

    /// <summary>
    /// Reposition the popup window. Coordinates are in physical pixels
    /// relative to the owner window's client area — they will be converted
    /// to screen coordinates internally.
    /// </summary>
    public void SetBounds(int clientX, int clientY, int width, int height)
    {
        if (_hwnd == IntPtr.Zero) return;

        // Convert client-relative coordinates to screen coordinates
        var pt = new POINT { X = clientX, Y = clientY };
        ClientToScreen(_ownerHwnd, ref pt);

        SetWindowPos(_hwnd, IntPtr.Zero, pt.X, pt.Y, width, height, SWP_NOACTIVATE | SWP_NOZORDER);
    }

    public void Show() => ShowWindow(_hwnd, SW_SHOWNOACTIVATE);
    public void Hide() => ShowWindow(_hwnd, SW_HIDE);

    public void Dispose()
    {
        if (_hwnd != IntPtr.Zero)
        {
            DestroyWindow(_hwnd);
            _hwnd = IntPtr.Zero;
        }
    }
}
