using System.Runtime.InteropServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using VideoArchive.Interop;
using VideoArchive.Models;
using VideoArchive.ViewModels;

namespace VideoArchive.Views;

public sealed partial class PlayerPanel : UserControl
{
    private VideoPlayerViewModel ViewModel { get; }
    private NativeVideoWindow? _videoWindow;
    private DispatcherQueueTimer? _timer;
    private bool _sliderDragging;
    private bool _isFullscreen;

    // Win32 subclass for immediate popup repositioning on window move/resize
    private static IntPtr _originalWndProc;
    private static WndProcDelegate? _subclassDelegate;
    private static PlayerPanel? _activeInstance;

    private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SetWindowLongPtrW(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr CallWindowProcW(IntPtr lpPrevWndFunc, IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool GetMonitorInfoW(IntPtr hMonitor, ref MONITORINFO lpmi);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    private const int VK_ESCAPE = 0x1B;
    private const int GWLP_WNDPROC = -4;
    private const uint WM_WINDOWPOSCHANGED = 0x0047;
    private const uint WM_ACTIVATE = 0x0006;
    private const uint MONITOR_DEFAULTTONEAREST = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    public PlayerPanel()
    {
        ViewModel = App.Services.GetRequiredService<VideoPlayerViewModel>();
        this.InitializeComponent();

        SegmentsList.ItemsSource = ViewModel.Segments;

        // Bind segments collection changes
        ViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(VideoPlayerViewModel.Segments))
                SegmentsList.ItemsSource = ViewModel.Segments;
            if (e.PropertyName == nameof(VideoPlayerViewModel.NowPlayingTitle))
                NowPlayingText.Text = ViewModel.NowPlayingTitle;
            if (e.PropertyName == nameof(VideoPlayerViewModel.IsPlaying))
                PlayPauseIcon.Glyph = ViewModel.IsPlaying ? "\uE769" : "\uE768"; // Pause : Play
            if (e.PropertyName == nameof(VideoPlayerViewModel.CanPlay))
                PlayPauseButton.IsEnabled = ViewModel.CanPlay;
        };

        this.Loaded += PlayerPanel_Loaded;
        this.Unloaded += PlayerPanel_Unloaded;

        // When the panel toggles from Collapsed → Visible, reposition after layout
        this.RegisterPropertyChangedCallback(VisibilityProperty, (_, _) =>
        {
            if (Visibility == Visibility.Visible)
            {
                DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
                {
                    EnsureVideoWindow();
                    PositionVideoWindow();

                    // Preview the selected video's first frame when the panel becomes visible
                    var mainVm = App.Services.GetRequiredService<MainViewModel>();
                    if (mainVm.SelectedVideo is not null
                        && mainVm.SelectedVideo.Id != ViewModel.CurrentVideo?.Id)
                    {
                        ViewModel.Preview(mainVm.SelectedVideo);
                    }
                });
            }
            else
            {
                _videoWindow?.Hide();
            }
        });
    }

    private void EnsureVideoWindow()
    {
        if (_videoWindow is not null) return;

        var parentHwnd = GetMainWindowHwnd();
        if (parentHwnd == IntPtr.Zero) return;

        _videoWindow = new NativeVideoWindow(parentHwnd);
        ViewModel.MediaPlayer.Hwnd = _videoWindow.Hwnd;

        // Subclass the main window for immediate repositioning
        InstallSubclass(parentHwnd);
    }

    private void InstallSubclass(IntPtr hwnd)
    {
        if (_originalWndProc != IntPtr.Zero) return; // Already installed

        _activeInstance = this;
        _subclassDelegate = SubclassWndProc;
        _originalWndProc = SetWindowLongPtrW(hwnd, GWLP_WNDPROC,
            Marshal.GetFunctionPointerForDelegate(_subclassDelegate));
    }

    private static IntPtr SubclassWndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        // When the main window moves or resizes, immediately reposition the popup
        if (msg == WM_WINDOWPOSCHANGED && _activeInstance is not null)
        {
            _activeInstance.DispatcherQueue.TryEnqueue(DispatcherQueuePriority.High, () =>
            {
                _activeInstance.PositionVideoWindow();
            });
        }

        return CallWindowProcW(_originalWndProc, hWnd, msg, wParam, lParam);
    }

    private void PlayerPanel_Loaded(object sender, RoutedEventArgs e)
    {
        EnsureVideoWindow();

        _timer = DispatcherQueue.CreateTimer();
        _timer.Interval = TimeSpan.FromMilliseconds(100);
        _timer.Tick += Timer_Tick;
        _timer.Start();

        PositionVideoWindow();

        // The Slider marks PointerPressed/Released as handled internally, so XAML
        // attribute handlers are never invoked. Use AddHandler with handledEventsToo
        // so we can detect when the user starts and finishes dragging the thumb.
        SeekSlider.AddHandler(PointerPressedEvent,
            new PointerEventHandler(SeekSlider_PointerPressed), handledEventsToo: true);
        SeekSlider.AddHandler(PointerReleasedEvent,
            new PointerEventHandler(SeekSlider_PointerReleased), handledEventsToo: true);
        SeekSlider.AddHandler(PointerCaptureLostEvent,
            new PointerEventHandler(SeekSlider_PointerCaptureLost), handledEventsToo: true);
    }

    private void PlayerPanel_Unloaded(object sender, RoutedEventArgs e)
    {
        _timer?.Stop();
        _videoWindow?.Hide();
    }

    /// <summary>
    /// Stop the update timer — must be called before disposing the ViewModel on shutdown.
    /// </summary>
    public void Shutdown()
    {
        _timer?.Stop();
        _timer = null;
        _videoWindow?.Dispose();
        _videoWindow = null;
    }

    /// <summary>
    /// Temporarily hide the native video overlay (e.g. while a dialog is open).
    /// </summary>
    public void HideOverlay() => _videoWindow?.Hide();

    /// <summary>
    /// Restore the native video overlay after a dialog has closed.
    /// </summary>
    public void ShowOverlay()
    {
        if (Visibility == Visibility.Visible)
            _videoWindow?.Show();
    }

    private static IntPtr GetMainWindowHwnd()
    {
        foreach (var window in WindowHelper.ActiveWindows)
            return WinRT.Interop.WindowNative.GetWindowHandle(window);
        return IntPtr.Zero;
    }

    private void Timer_Tick(DispatcherQueueTimer sender, object args)
    {
        // Poll Escape key to exit fullscreen (works regardless of which window has focus)
        if (_isFullscreen && (GetAsyncKeyState(VK_ESCAPE) & 0x8000) != 0)
        {
            ToggleFullscreen();
            return;
        }

        ViewModel.UpdateTimeline();

        if (!_sliderDragging)
            SeekSlider.Value = ViewModel.Position;

        CurrentTimeText.Text = ViewModel.CurrentTimeText;
        TotalTimeText.Text = ViewModel.TotalTimeText;
    }

    private void VideoSurface_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!_isFullscreen)
            PositionVideoWindow();
    }

    private void PositionVideoWindow()
    {
        if (_videoWindow is null || _isFullscreen) return;

        try
        {
            var transform = VideoSurface.TransformToVisual(null);
            var point = transform.TransformPoint(new Windows.Foundation.Point(0, 0));

            var scale = GetDpiScale();
            var x = (int)(point.X * scale);
            var y = (int)(point.Y * scale);
            var w = (int)(VideoSurface.ActualWidth * scale);
            var h = (int)(VideoSurface.ActualHeight * scale);

            if (w > 0 && h > 0)
            {
                _videoWindow.SetBounds(x, y, w, h);
                _videoWindow.Show();
                NoVideoPlaceholder.Visibility = ViewModel.CurrentVideo is not null
                    ? Visibility.Collapsed : Visibility.Visible;
            }
        }
        catch { /* Transform may fail during layout transitions */ }
    }

    // ── Fullscreen ───────────────────────────────────────────────────
    private void Fullscreen_Click(object sender, RoutedEventArgs e) => ToggleFullscreen();

    private void VideoSurface_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e) => ToggleFullscreen();

    public void ToggleFullscreen()
    {
        if (_videoWindow is null) return;

        _isFullscreen = !_isFullscreen;

        if (_isFullscreen)
        {
            // Get the monitor rect for the main window
            var hwnd = GetMainWindowHwnd();
            var monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
            var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
            GetMonitorInfoW(monitor, ref mi);

            _videoWindow.SetScreenBounds(
                mi.rcMonitor.Left, mi.rcMonitor.Top,
                mi.rcMonitor.Right - mi.rcMonitor.Left,
                mi.rcMonitor.Bottom - mi.rcMonitor.Top);
            _videoWindow.Show();

            FullscreenIcon.Glyph = "\uE73F"; // Exit fullscreen icon

            // Grab focus so OnKeyDown also works as a second path
            this.IsTabStop = true;
            this.Focus(FocusState.Programmatic);
        }
        else
        {
            FullscreenIcon.Glyph = "\uE740"; // Enter fullscreen icon
            PositionVideoWindow();
        }
    }

    // Handle Escape key to exit fullscreen
    protected override void OnKeyDown(KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Escape && _isFullscreen)
        {
            ToggleFullscreen();
            e.Handled = true;
        }
        base.OnKeyDown(e);
    }

    private double GetDpiScale()
    {
        var hwnd = GetMainWindowHwnd();
        if (hwnd != IntPtr.Zero)
        {
            var dpi = NativeMethods.GetDpiForWindow(hwnd);
            return dpi / 96.0;
        }
        return 1.0;
    }

    // Transport control handlers
    private void PlayPause_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.PlayPauseCommand.Execute(null);
        PositionVideoWindow();
    }

    private void Stop_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.StopCommand.Execute(null);
    }

    private void SeekSlider_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        _sliderDragging = true;
        ViewModel.BeginSeek();
    }

    private void SeekSlider_PointerReleased(object sender, PointerRoutedEventArgs e) => CommitSeek();

    private void SeekSlider_PointerCaptureLost(object sender, PointerRoutedEventArgs e) => CommitSeek();

    private void CommitSeek()
    {
        if (!_sliderDragging) return;
        _sliderDragging = false;
        ViewModel.Position = SeekSlider.Value;
        ViewModel.EndSeek();
    }

    private void SeekSlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (_sliderDragging)
            ViewModel.Position = e.NewValue;
    }

    private void VolumeSlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        ViewModel.Volume = (int)e.NewValue;
    }

    // Segment handlers
    private void AddSegment_Click(object sender, RoutedEventArgs e) => ViewModel.AddSegmentCommand.Execute(null);

    private void SetStartTime_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is VideoSegment segment)
            ViewModel.SetStartTime(segment);
    }

    private void SetEndTime_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is VideoSegment segment)
            ViewModel.SetEndTime(segment);
    }

    private void DeleteSegment_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is VideoSegment segment)
            ViewModel.DeleteSegmentCommand.Execute(segment);
    }

    /// <summary>
    /// Called from MainWindow to start playback of a video.
    /// </summary>
    public void PlayVideo(Video video)
    {
        NoVideoPlaceholder.Visibility = Visibility.Collapsed;
        EnsureVideoWindow();
        ViewModel.Play(video);

        DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            PositionVideoWindow();
        });
    }

    private static class NativeMethods
    {
        [DllImport("user32.dll")]
        public static extern uint GetDpiForWindow(IntPtr hwnd);
    }
}

/// <summary>
/// Tracks active windows for HWND resolution.
/// </summary>
public static class WindowHelper
{
    public static List<Window> ActiveWindows { get; } = [];

    public static void TrackWindow(Window window)
    {
        ActiveWindows.Add(window);
        window.Closed += (_, _) => ActiveWindows.Remove(window);
    }
}
