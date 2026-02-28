using System.Runtime.InteropServices;

namespace VideoArchive.Interop;

/// <summary>
/// Creates and manages a Win32 child window that LibVLC can render video into.
/// This is necessary because WinUI 3 does not expose per-element HWNDs,
/// so we create a native child window and position it over a placeholder element.
/// </summary>
public sealed class NativeVideoWindow : IDisposable
{
    private const string ClassName = "LibVLCVideoChild";
    private static bool _classRegistered;
    private IntPtr _hwnd;
    private readonly WndProcDelegate _wndProcDelegate;

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

    // Window styles
    private const uint WS_CHILD = 0x40000000;
    private const uint WS_VISIBLE = 0x10000000;
    private const uint WS_CLIPSIBLINGS = 0x04000000;
    private const uint WS_CLIPCHILDREN = 0x02000000;
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const int SW_SHOW = 5;
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

    public IntPtr Hwnd => _hwnd;

    public NativeVideoWindow(IntPtr parentHwnd)
    {
        // Must keep delegate alive to prevent GC collection
        _wndProcDelegate = WndProc;

        EnsureClassRegistered();

        _hwnd = CreateWindowExW(
            0,
            ClassName,
            "LibVLC Video Surface",
            WS_CHILD | WS_CLIPSIBLINGS | WS_CLIPCHILDREN,
            0, 0, 1, 1, // Initial size — will be repositioned
            parentHwnd,
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
            hbrBackground = IntPtr.Zero, // Black background (NULL brush = no erase)
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
    /// Reposition the child window to match the layout of a XAML element.
    /// Coordinates are in physical pixels relative to the parent window client area.
    /// </summary>
    public void SetBounds(int x, int y, int width, int height)
    {
        if (_hwnd == IntPtr.Zero) return;
        SetWindowPos(_hwnd, IntPtr.Zero, x, y, width, height, SWP_NOZORDER | SWP_NOACTIVATE);
    }

    public void Show() => ShowWindow(_hwnd, SW_SHOW);
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
