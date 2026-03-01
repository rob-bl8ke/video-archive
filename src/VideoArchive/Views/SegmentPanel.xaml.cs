using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
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
                SegmentsList.ItemsSource = ViewModel.Segments;
        };
    }

    // ── Add / Delete ─────────────────────────────────────────────────

    private void AddSegment_Click(object sender, RoutedEventArgs e)
        => ViewModel.AddSegmentCommand.Execute(null);

    private void DeleteSegment_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is VideoSegment segment)
            ViewModel.DeleteSegmentCommand.Execute(segment);
    }

    // ── Rename ───────────────────────────────────────────────────────

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
            ViewModel.SetStartTime(segment);
    }

    private void SetEndFromPlayhead_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is VideoSegment segment)
            ViewModel.SetEndTime(segment);
    }

    // ── Start time adjustments ───────────────────────────────────────

    private void AdjustStartMinus5_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is VideoSegment segment)
            ViewModel.AdjustStartTimeCommand.Execute((segment, -5));
    }

    private void AdjustStartMinus1_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is VideoSegment segment)
            ViewModel.AdjustStartTimeCommand.Execute((segment, -1));
    }

    private void AdjustStartPlus1_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is VideoSegment segment)
            ViewModel.AdjustStartTimeCommand.Execute((segment, 1));
    }

    private void AdjustStartPlus5_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is VideoSegment segment)
            ViewModel.AdjustStartTimeCommand.Execute((segment, 5));
    }

    // ── End time adjustments ─────────────────────────────────────────

    private void AdjustEndMinus5_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is VideoSegment segment)
            ViewModel.AdjustEndTimeCommand.Execute((segment, -5));
    }

    private void AdjustEndMinus1_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is VideoSegment segment)
            ViewModel.AdjustEndTimeCommand.Execute((segment, -1));
    }

    private void AdjustEndPlus1_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is VideoSegment segment)
            ViewModel.AdjustEndTimeCommand.Execute((segment, 1));
    }

    private void AdjustEndPlus5_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is VideoSegment segment)
            ViewModel.AdjustEndTimeCommand.Execute((segment, 5));
    }
}
