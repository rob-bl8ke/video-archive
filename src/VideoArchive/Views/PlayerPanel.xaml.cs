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
        };

        this.Loaded += PlayerPanel_Loaded;
        this.Unloaded += PlayerPanel_Unloaded;

        // When the panel toggles from Collapsed → Visible, we need to
        // re-create/reposition the native video window after layout completes.
        this.RegisterPropertyChangedCallback(VisibilityProperty, (_, _) =>
        {
            if (Visibility == Visibility.Visible)
            {
                // Defer to next layout pass so ActualWidth/Height are populated
                DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
                {
                    EnsureVideoWindow();
                    PositionVideoWindow();
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
    }

    private void PlayerPanel_Loaded(object sender, RoutedEventArgs e)
    {
        // Create native child window for video rendering (may be collapsed, that's OK)
        EnsureVideoWindow();

        // Start timeline update timer (10fps)
        _timer = DispatcherQueue.CreateTimer();
        _timer.Interval = TimeSpan.FromMilliseconds(100);
        _timer.Tick += Timer_Tick;
        _timer.Start();

        PositionVideoWindow();
    }

    private void PlayerPanel_Unloaded(object sender, RoutedEventArgs e)
    {
        _timer?.Stop();
        _videoWindow?.Hide();
    }

    private static IntPtr GetMainWindowHwnd()
    {
        // Get HWND from the first active window
        foreach (var window in WindowHelper.ActiveWindows)
        {
            return WinRT.Interop.WindowNative.GetWindowHandle(window);
        }
        return IntPtr.Zero;
    }

    private void Timer_Tick(DispatcherQueueTimer sender, object args)
    {
        ViewModel.UpdateTimeline();

        if (!_sliderDragging)
        {
            SeekSlider.Value = ViewModel.Position;
        }

        CurrentTimeText.Text = ViewModel.CurrentTimeText;
        TotalTimeText.Text = ViewModel.TotalTimeText;

        // Popup windows don't auto-follow the main window,
        // so reposition every tick to track window moves/resizes
        if (ViewModel.CurrentVideo is not null && Visibility == Visibility.Visible)
            PositionVideoWindow();
    }

    private void VideoSurface_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        PositionVideoWindow();
    }

    private void PositionVideoWindow()
    {
        if (_videoWindow is null) return;

        try
        {
            // Get the position of VideoSurface relative to the window
            var transform = VideoSurface.TransformToVisual(null);
            var point = transform.TransformPoint(new Windows.Foundation.Point(0, 0));

            // Account for DPI scaling
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
        PositionVideoWindow(); // Ensure video window is visible
    }

    private void Stop_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.StopCommand.Execute(null);
    }

    private void SeekSlider_ManipulationStarting(object sender, ManipulationStartingRoutedEventArgs e)
    {
        _sliderDragging = true;
        ViewModel.BeginSeek();
    }

    private void SeekSlider_ManipulationCompleted(object sender, ManipulationCompletedRoutedEventArgs e)
    {
        ViewModel.Position = SeekSlider.Value;
        ViewModel.EndSeek();
        _sliderDragging = false;
    }

    private void SeekSlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (_sliderDragging)
        {
            ViewModel.Position = e.NewValue;
        }
    }

    private void VolumeSlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        ViewModel.Volume = (int)e.NewValue;
    }

    // Segment handlers
    private void AddSegment_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.AddSegmentCommand.Execute(null);
    }

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

        // Defer positioning to after the layout pass so VideoSurface has real dimensions
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            PositionVideoWindow();
        });
    }

    private static class NativeMethods
    {
        [System.Runtime.InteropServices.DllImport("user32.dll")]
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
