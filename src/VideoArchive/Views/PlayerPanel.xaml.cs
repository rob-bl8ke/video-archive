using System.Runtime.InteropServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using VideoArchive.Interop;
using VideoArchive.ViewModels;

namespace VideoArchive.Views;

public sealed partial class PlayerPanel : UserControl
{
    private VideoPlayerViewModel ViewModel { get; }
    private MainViewModel MainViewModel { get; }
    private NativeVideoWindow? _videoWindow;
    private DispatcherQueueTimer? _timer;
    private bool _sliderDragging;
    private bool _isFullscreen;
    private bool _overlayVisible;
    private bool _navPaneAnimating;
    private bool _editingSegmentName;
    private string _nameBeforeEdit = string.Empty;

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
        MainViewModel = App.Services.GetRequiredService<MainViewModel>();
        this.InitializeComponent();

        // Bind VM property changes to UI elements
        ViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(VideoPlayerViewModel.NowPlayingTitle))
                NowPlayingText.Text = ViewModel.NowPlayingTitle;
            if (e.PropertyName == nameof(VideoPlayerViewModel.IsPlaying))
                PlayPauseIcon.Glyph = ViewModel.IsPlaying ? "\uE769" : "\uE768"; // Pause : Play
            if (e.PropertyName == nameof(VideoPlayerViewModel.CanPlay))
                PlayPauseButton.IsEnabled = ViewModel.CanPlay;
            if (e.PropertyName == nameof(VideoPlayerViewModel.IsLoopEnabled))
                LoopButton.IsChecked = ViewModel.IsLoopEnabled;
            if (e.PropertyName is nameof(VideoPlayerViewModel.SelectedSegment)
                                or nameof(VideoPlayerViewModel.VideoFps))
                RefreshAdjustPanel();
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
                _overlayVisible = false;
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
        this.SizeChanged += PlayerPanel_SizeChanged;

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
        this.SizeChanged -= PlayerPanel_SizeChanged;
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
    public void HideOverlay()
    {
        _overlayVisible = false;
        _videoWindow?.Hide();
    }

    /// <summary>
    /// Restore the native video overlay after a dialog has closed.
    /// </summary>
    public void ShowOverlay()
    {
        if (Visibility == Visibility.Visible)
        {
            _overlayVisible = true;
            _videoWindow?.Show();
        }
    }

    /// <summary>
    /// Called when the NavigationView pane begins opening or closing so the timer
    /// repositions the native popup on every tick during the slide animation.
    /// </summary>
    public void BeginNavPaneTransition() => _navPaneAnimating = true;

    /// <summary>
    /// Called when the NavigationView pane has fully opened or closed.
    /// Does a final authoritative reposition after layout has settled.
    /// </summary>
    public void EndNavPaneTransition()
    {
        _navPaneAnimating = false;
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, PositionVideoWindow);
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

        // Reposition the native popup on every tick while the nav pane is animating
        // so the Win32 window tracks the XAML surface through the slide transition.
        if (_navPaneAnimating)
            PositionVideoWindow();

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

    private void PlayerPanel_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        string state = e.NewSize.Width switch
        {
            >= 500 => "AdjustWide",
            >= 380 => "AdjustNarrow",
            _      => "AdjustCompact",
        };
        VisualStateManager.GoToState(this, state, false);
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
                // Only show the native popup when it has been explicitly enabled via
                // ShowOverlay(). This prevents a stale WM_WINDOWPOSCHANGED triggered
                // by ALT+TAB activation from unhiding the window over the wrong panel.
                if (_overlayVisible)
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

    private void BackToSearch_Click(object sender, RoutedEventArgs e)
    {
        // Stop playback before returning to the search view
        ViewModel.StopCommand.Execute(null);
        MainViewModel.NavigateToSearchCommand.Execute(null);
    }

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

    private void LoopToggle_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.ToggleLoopCommand.Execute(null);
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
        {
            ViewModel.Position = e.NewValue;
            ViewModel.UpdateSeekTimeDisplay(e.NewValue);
        }
    }

    private void VolumeSlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        ViewModel.Volume = (int)e.NewValue;
    }

    // ── Segment adjustment panel ─────────────────────────────────────

    private static string FormatPreciseDuration(TimeSpan ts)
    {
        int h = (int)ts.TotalHours;
        int m = ts.Minutes;
        int s = ts.Seconds;
        int ms = ts.Milliseconds;
        return h > 0 ? $"{h}:{m:D2}:{s:D2}.{ms:D3}" : $"{m}:{s:D2}.{ms:D3}";
    }

    private static string FormatTimecode(TimeSpan ts, float fps)
    {
        if (fps <= 0f) return "--:--:--:--";
        int h = (int)ts.TotalHours;
        int m = ts.Minutes;
        int s = ts.Seconds;
        int f = (int)Math.Floor(ts.Milliseconds / 1000.0 * fps);
        return $"{h:D2}:{m:D2}:{s:D2}:{f:D2}";
    }

    private void RefreshAdjustPanel()
    {
        var seg = ViewModel.SelectedSegment;
        if (seg is null)
        {
            SegmentAdjustPanel.Visibility = Visibility.Collapsed;
            return;
        }

        var fps = ViewModel.VideoFps;
        if (!_editingSegmentName)
            SegmentNameLabel.Text = seg.Name;
        AdjStartTime.Text = FormatPreciseDuration(seg.StartTime);
        AdjStartTimecode.Text = FormatTimecode(seg.StartTime, fps);
        AdjEndTime.Text = FormatPreciseDuration(seg.EndTime);
        AdjEndTimecode.Text = FormatTimecode(seg.EndTime, fps);
        SegmentAdjustPanel.Visibility = Visibility.Visible;
    }

    private void EditSegmentName_Click(object sender, RoutedEventArgs e)
    {
        var seg = ViewModel.SelectedSegment;
        if (seg is null) return;
        _nameBeforeEdit = seg.Name;
        _editingSegmentName = true;
        SegmentNameEditor.Text = seg.Name;
        SegmentNameLabelPanel.Visibility = Visibility.Collapsed;
        SegmentNameEditor.Visibility = Visibility.Visible;
        SegmentNameEditor.Focus(FocusState.Programmatic);
        SegmentNameEditor.SelectAll();
    }

    private void CommitSegmentNameEdit()
    {
        if (!_editingSegmentName) return;
        _editingSegmentName = false;
        var seg = ViewModel.SelectedSegment;
        if (seg is not null)
        {
            var newName = SegmentNameEditor.Text.Trim();
            seg.Name = string.IsNullOrWhiteSpace(newName) ? _nameBeforeEdit : newName;
            ViewModel.RenameSegmentCommand.Execute(seg);
        }
        SegmentNameEditor.Visibility = Visibility.Collapsed;
        SegmentNameLabelPanel.Visibility = Visibility.Visible;
        RefreshAdjustPanel();
    }

    private void SegmentNameEditor_LostFocus(object sender, RoutedEventArgs e)
        => CommitSegmentNameEdit();

    private void SegmentNameEditor_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter)
        {
            CommitSegmentNameEdit();
            e.Handled = true;
        }
        else if (e.Key == Windows.System.VirtualKey.Escape)
        {
            _editingSegmentName = false;
            SegmentNameEditor.Visibility = Visibility.Collapsed;
            SegmentNameLabelPanel.Visibility = Visibility.Visible;
            RefreshAdjustPanel();
            e.Handled = true;
        }
    }

    // ── Set from playhead ────────────────────────────────────────────

    private void SetStartFromPlayhead_Click(object sender, RoutedEventArgs e)
    {
        var seg = ViewModel.SelectedSegment;
        if (seg is null) return;
        ViewModel.SetStartTime(seg);
        RefreshAdjustPanel();
    }

    private void SetEndFromPlayhead_Click(object sender, RoutedEventArgs e)
    {
        var seg = ViewModel.SelectedSegment;
        if (seg is null) return;
        ViewModel.SetEndTime(seg);
        RefreshAdjustPanel();
    }

    // ── Start time adjustments ───────────────────────────────────────

    private void AdjustStartMinus5_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedSegment is { } seg) { ViewModel.AdjustStartTimeCommand.Execute((seg, -5.0)); RefreshAdjustPanel(); }
    }

    private void AdjustStartMinus1_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedSegment is { } seg) { ViewModel.AdjustStartTimeCommand.Execute((seg, -1.0)); RefreshAdjustPanel(); }
    }

    private void AdjustStartMinusTenth_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedSegment is { } seg) { ViewModel.AdjustStartTimeCommand.Execute((seg, -0.1)); RefreshAdjustPanel(); }
    }

    private void AdjustStartMinus1Frame_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedSegment is { } seg) { ViewModel.AdjustStartFrameCommand.Execute((seg, -1)); RefreshAdjustPanel(); }
    }

    private void AdjustStartPlus1Frame_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedSegment is { } seg) { ViewModel.AdjustStartFrameCommand.Execute((seg, 1)); RefreshAdjustPanel(); }
    }

    private void AdjustStartPlusTenth_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedSegment is { } seg) { ViewModel.AdjustStartTimeCommand.Execute((seg, 0.1)); RefreshAdjustPanel(); }
    }

    private void AdjustStartPlus1_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedSegment is { } seg) { ViewModel.AdjustStartTimeCommand.Execute((seg, 1.0)); RefreshAdjustPanel(); }
    }

    private void AdjustStartPlus5_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedSegment is { } seg) { ViewModel.AdjustStartTimeCommand.Execute((seg, 5.0)); RefreshAdjustPanel(); }
    }

    // ── End time adjustments ─────────────────────────────────────────

    private void AdjustEndMinus5_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedSegment is { } seg) { ViewModel.AdjustEndTimeCommand.Execute((seg, -5.0)); RefreshAdjustPanel(); }
    }

    private void AdjustEndMinus1_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedSegment is { } seg) { ViewModel.AdjustEndTimeCommand.Execute((seg, -1.0)); RefreshAdjustPanel(); }
    }

    private void AdjustEndMinusTenth_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedSegment is { } seg) { ViewModel.AdjustEndTimeCommand.Execute((seg, -0.1)); RefreshAdjustPanel(); }
    }

    private void AdjustEndMinus1Frame_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedSegment is { } seg) { ViewModel.AdjustEndFrameCommand.Execute((seg, -1)); RefreshAdjustPanel(); }
    }

    private void AdjustEndPlus1Frame_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedSegment is { } seg) { ViewModel.AdjustEndFrameCommand.Execute((seg, 1)); RefreshAdjustPanel(); }
    }

    private void AdjustEndPlusTenth_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedSegment is { } seg) { ViewModel.AdjustEndTimeCommand.Execute((seg, 0.1)); RefreshAdjustPanel(); }
    }

    private void AdjustEndPlus1_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedSegment is { } seg) { ViewModel.AdjustEndTimeCommand.Execute((seg, 1.0)); RefreshAdjustPanel(); }
    }

    private void AdjustEndPlus5_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedSegment is { } seg) { ViewModel.AdjustEndTimeCommand.Execute((seg, 5.0)); RefreshAdjustPanel(); }
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
