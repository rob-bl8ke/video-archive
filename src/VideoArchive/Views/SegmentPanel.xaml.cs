using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using VideoArchive.Models;
using VideoArchive.ViewModels;

namespace VideoArchive.Views;

/// <summary>
/// A side panel for managing video segments: add, rename, adjust times, delete.
/// </summary>
public sealed partial class SegmentPanel : UserControl
{
    private VideoPlayerViewModel ViewModel { get; }

    public SegmentPanel()
    {
        ViewModel = App.Services.GetRequiredService<VideoPlayerViewModel>();
        this.InitializeComponent();

        SegmentsList.ItemsSource = ViewModel.Segments;

        ViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(VideoPlayerViewModel.Segments))
            {
                SegmentsList.ItemsSource = ViewModel.Segments;
                // Defer refresh until the new containers have rendered
                SegmentsList.LayoutUpdated += OnSegmentsLayoutUpdated;
            }

            if (e.PropertyName is nameof(VideoPlayerViewModel.ActiveSegment)
                                or nameof(VideoPlayerViewModel.SelectedSegment)
                                or nameof(VideoPlayerViewModel.IsSegmentLooping)
                                or nameof(VideoPlayerViewModel.State)
                                or nameof(VideoPlayerViewModel.VideoFps))
            {
                RefreshSegmentPlayStates();
            }
        };
    }

    // ── Visual state refresh ──────────────────────────────────────────

    private void OnSegmentsLayoutUpdated(object? sender, object e)
    {
        SegmentsList.LayoutUpdated -= OnSegmentsLayoutUpdated;
        RefreshSegmentPlayStates();
    }

    /// <summary>
    /// Updates the play-button glyph/style, loop-toggle checked state, and card
    /// border highlight for every visible segment container.
    /// </summary>
    private void RefreshSegmentPlayStates()
    {
        var activeId     = ViewModel.ActiveSegment?.Id;
        var selectedId   = ViewModel.SelectedSegment?.Id;
        var isPlaying    = ViewModel.State == PlaybackState.Playing;
        var isLooping    = ViewModel.IsSegmentLooping;
        var fps          = ViewModel.VideoFps;
        var defaultBrush = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"];
        var accentStyle  = (Style)Application.Current.Resources["AccentButtonStyle"];

        // Colour tokens:
        //   selected (editing focus) → SystemAccentColor border, 2px
        //   active + playing (segment playback) → SteelBlue border, 2px
        //   active + paused (segment paused) → SteelBlue border, 1px
        //   default → CardStrokeColor, 1px
        var selectedBrush = new SolidColorBrush(
            (Windows.UI.Color)Application.Current.Resources["SystemAccentColor"]);
        var playingBrush  = new SolidColorBrush(Colors.SteelBlue);

        foreach (var segment in ViewModel.Segments)
        {
            if (SegmentsList.ContainerFromItem(segment) is not ListViewItem container)
                continue;

            bool isActive           = segment.Id == activeId;
            bool isSelected         = segment.Id == selectedId;
            bool isActiveAndPlaying = isActive && isPlaying;

            // Card border — priority: playing > selected > default
            if (FindFirstDescendant<Border>(container) is Border card)
            {
                if (isActiveAndPlaying)
                {
                    card.BorderBrush     = playingBrush;
                    card.BorderThickness = new Thickness(2);
                }
                else if (isSelected)
                {
                    card.BorderBrush     = selectedBrush;
                    card.BorderThickness = new Thickness(2);
                }
                else if (isActive) // active but paused mid-segment
                {
                    card.BorderBrush     = playingBrush;
                    card.BorderThickness = new Thickness(1);
                }
                else
                {
                    card.BorderBrush     = defaultBrush;
                    card.BorderThickness = new Thickness(1);
                }
            }

            // Play/Pause button — accent style + pause glyph when this segment is playing
            var playBtn = FindDescendants<Button>(container)
                          .FirstOrDefault(b => AutomationProperties.GetName(b) == "segment-play");
            if (playBtn is not null)
            {
                playBtn.Style = isActiveAndPlaying ? accentStyle : null;
                if (playBtn.Content is FontIcon fi)
                    fi.Glyph = isActiveAndPlaying ? "\uE769" : "\uE768"; // Pause : Play
            }

            // Loop ToggleButton checked state
            if (FindFirstDescendant<ToggleButton>(container) is ToggleButton loopBtn)
                loopBtn.IsChecked = isLooping;

            // Timecode TextBlocks — updated imperatively so FPS changes are reflected immediately
            var startTc = FindDescendants<TextBlock>(container)
                          .FirstOrDefault(tb => AutomationProperties.GetName(tb) == "start-timecode");
            if (startTc is not null)
                startTc.Text = FormatTimecode(segment.StartTime, fps);

            var endTc = FindDescendants<TextBlock>(container)
                        .FirstOrDefault(tb => AutomationProperties.GetName(tb) == "end-timecode");
            if (endTc is not null)
                endTc.Text = FormatTimecode(segment.EndTime, fps);
        }
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

    // ── Visual tree helpers ───────────────────────────────────────────

    private static T? FindFirstDescendant<T>(DependencyObject parent) where T : DependencyObject
    {
        int count = VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match) return match;
            var result = FindFirstDescendant<T>(child);
            if (result is not null) return result;
        }
        return null;
    }

    private static IEnumerable<T> FindDescendants<T>(DependencyObject parent) where T : DependencyObject
    {
        int count = VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match) yield return match;
            foreach (var d in FindDescendants<T>(child)) yield return d;
        }
    }

    // ── Add / Delete ─────────────────────────────────────────────────

    private void AddSegment_Click(object sender, RoutedEventArgs e)
        => ViewModel.AddSegmentCommand.Execute(null);

    private void DeleteSegment_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is VideoSegment segment)
            ViewModel.DeleteSegmentCommand.Execute(segment);
    }
    // ── Card activation (non-button areas: TextBlocks, grid space) ──────

    private void SegmentCard_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (sender is Border border && border.DataContext is VideoSegment segment)
            ViewModel.ActivateSegment(segment);
    }

    // ── Rename ───────────────────────────────────────────────────────

    private void SegmentName_GotFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox tb && tb.Tag is VideoSegment segment)
            ViewModel.ActivateSegment(segment);
    }

    private void SegmentName_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox tb && tb.Tag is VideoSegment segment)
            ViewModel.RenameSegmentCommand.Execute(segment);
    }

    private void SegmentName_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter && sender is TextBox tb && tb.Tag is VideoSegment segment)
        {
            ViewModel.RenameSegmentCommand.Execute(segment);
            e.Handled = true;
        }
    }

    // ── Set from playhead ────────────────────────────────────────────

    private void SetStartFromPlayhead_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is VideoSegment segment)
        {
            ViewModel.SelectedSegment = segment;
            ViewModel.SetStartTime(segment);
        }
    }

    private void SetEndFromPlayhead_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is VideoSegment segment)
        {
            ViewModel.SelectedSegment = segment;
            ViewModel.SetEndTime(segment);
        }
    }

    // ── Start time adjustments ───────────────────────────────────────

    private void AdjustStartMinus5_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is VideoSegment segment)
        {
            ViewModel.SelectedSegment = segment;
            ViewModel.AdjustStartTimeCommand.Execute((segment, -5.0));
        }
    }

    private void AdjustStartMinus1_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is VideoSegment segment)
        {
            ViewModel.SelectedSegment = segment;
            ViewModel.AdjustStartTimeCommand.Execute((segment, -1.0));
        }
    }

    private void AdjustStartMinusTenth_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is VideoSegment segment)
        {
            ViewModel.SelectedSegment = segment;
            ViewModel.AdjustStartTimeCommand.Execute((segment, -0.1));
        }
    }

    private void AdjustStartMinus1Frame_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is VideoSegment segment)
        {
            ViewModel.SelectedSegment = segment;
            ViewModel.AdjustStartFrameCommand.Execute((segment, -1));
        }
    }

    private void AdjustStartPlus1Frame_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is VideoSegment segment)
        {
            ViewModel.SelectedSegment = segment;
            ViewModel.AdjustStartFrameCommand.Execute((segment, 1));
        }
    }

    private void AdjustStartPlusTenth_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is VideoSegment segment)
        {
            ViewModel.SelectedSegment = segment;
            ViewModel.AdjustStartTimeCommand.Execute((segment, 0.1));
        }
    }

    private void AdjustStartPlus1_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is VideoSegment segment)
        {
            ViewModel.SelectedSegment = segment;
            ViewModel.AdjustStartTimeCommand.Execute((segment, 1.0));
        }
    }

    private void AdjustStartPlus5_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is VideoSegment segment)
        {
            ViewModel.SelectedSegment = segment;
            ViewModel.AdjustStartTimeCommand.Execute((segment, 5.0));
        }
    }

    // ── End time adjustments ─────────────────────────────────────────

    private void AdjustEndMinus5_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is VideoSegment segment)
        {
            ViewModel.SelectedSegment = segment;
            ViewModel.AdjustEndTimeCommand.Execute((segment, -5.0));
        }
    }

    private void AdjustEndMinus1_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is VideoSegment segment)
        {
            ViewModel.SelectedSegment = segment;
            ViewModel.AdjustEndTimeCommand.Execute((segment, -1.0));
        }
    }

    private void AdjustEndMinusTenth_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is VideoSegment segment)
        {
            ViewModel.SelectedSegment = segment;
            ViewModel.AdjustEndTimeCommand.Execute((segment, -0.1));
        }
    }

    private void AdjustEndMinus1Frame_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is VideoSegment segment)
        {
            ViewModel.SelectedSegment = segment;
            ViewModel.AdjustEndFrameCommand.Execute((segment, -1));
        }
    }

    private void AdjustEndPlus1Frame_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is VideoSegment segment)
        {
            ViewModel.SelectedSegment = segment;
            ViewModel.AdjustEndFrameCommand.Execute((segment, 1));
        }
    }

    private void AdjustEndPlusTenth_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is VideoSegment segment)
        {
            ViewModel.SelectedSegment = segment;
            ViewModel.AdjustEndTimeCommand.Execute((segment, 0.1));
        }
    }

    private void AdjustEndPlus1_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is VideoSegment segment)
        {
            ViewModel.SelectedSegment = segment;
            ViewModel.AdjustEndTimeCommand.Execute((segment, 1.0));
        }
    }

    private void AdjustEndPlus5_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is VideoSegment segment)
        {
            ViewModel.SelectedSegment = segment;
            ViewModel.AdjustEndTimeCommand.Execute((segment, 5.0));
        }
    }

    // ── Segment transport ────────────────────────────────────────────

    private void SegmentPlayPause_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is VideoSegment segment)
        {
            ViewModel.SelectedSegment = segment;
            ViewModel.SegmentPlayPauseCommand.Execute(segment);
        }
    }

    // Stop: select the segment but do NOT seek/play
    private void SegmentStop_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is VideoSegment segment)
            ViewModel.SelectedSegment = segment;
        ViewModel.SegmentStopCommand.Execute(null);
    }

    private void SegmentToggleLoop_Click(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleButton tb && tb.Tag is VideoSegment segment)
            ViewModel.SelectedSegment = segment;
        ViewModel.SegmentToggleLoopCommand.Execute(null);
    }
}
