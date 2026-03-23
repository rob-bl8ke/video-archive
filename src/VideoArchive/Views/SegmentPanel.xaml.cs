using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI;
using Microsoft.UI.Xaml;
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

        // Each time the panel re-enters the visual tree (e.g. when the Pivot item is
        // inserted back on entering PlayerView), queue a deferred refresh so containers
        // have a chance to render before we try to walk the visual tree.
        this.Loaded += (_, _) => SegmentsList.LayoutUpdated += OnSegmentsLayoutUpdated;

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
                                or nameof(VideoPlayerViewModel.IsLoopEnabled)
                                or nameof(VideoPlayerViewModel.State))
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
        var defaultBrush = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"];

        // Colour tokens:
        //   selected (editing focus) → SystemAccentColor border, 2px
        //   active + playing (segment playback) → SteelBlue border, 2px
        //   active + paused (segment paused) → SteelBlue border, 1px
        //   default → CardStrokeColor, 1px
        var selectedBrush = new SolidColorBrush(
            (Windows.UI.Color)Application.Current.Resources["SystemAccentColor"]);
        var playingBrush  = new SolidColorBrush(Colors.SteelBlue);

        bool anyMissed = false;

        foreach (var segment in ViewModel.Segments)
        {
            if (SegmentsList.ContainerFromItem(segment) is not ListViewItem container)
            {
                // Track if a container we care about highlighting is missing so we can retry.
                if (segment.Id == activeId || segment.Id == selectedId)
                    anyMissed = true;
                continue;
            }

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
                else if (isSelected || isActive)
                {
                    card.BorderBrush     = isSelected ? selectedBrush : playingBrush;
                    card.BorderThickness = new Thickness(2);
                }
                else
                {
                    card.BorderBrush     = defaultBrush;
                    card.BorderThickness = new Thickness(1);
                }
            }
        }

        // If a highlighted container wasn't ready (e.g. ListView inside a Collapsed
        // ancestor), re-arm so we retry as soon as the next layout pass occurs —
        // including when the tab panel is expanded and the ListView becomes visible.
        if (anyMissed)
        {
            SegmentsList.LayoutUpdated -= OnSegmentsLayoutUpdated;
            SegmentsList.LayoutUpdated += OnSegmentsLayoutUpdated;
        }
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

    private void SegmentCard_Tapped(object sender, TappedRoutedEventArgs e)
    {
        // Buttons inside the card handle their own logic and must not also
        // trigger segment activation (which would start playback).
        if (e.OriginalSource is DependencyObject src)
        {
            var node = (DependencyObject?)src;
            while (node is not null && !ReferenceEquals(node, sender))
            {
                if (node is ButtonBase) return;
                node = VisualTreeHelper.GetParent(node);
            }
        }

        if (sender is Border border && border.DataContext is VideoSegment segment)
            ViewModel.ActivateSegment(segment);
    }

    // ── Segment transport ────────────────────────────────────────────
    // Transport controls (Play/Pause, Stop, Loop) moved to PlayerPanel.
    // Segment selection via card tap or name focus pauses at segment start;
    // playback is driven by the main transport controls.
}
