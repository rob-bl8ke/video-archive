using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using VideoArchive.ViewModels;

namespace VideoArchive.Views;

public sealed partial class TabsPanel : UserControl
{
    public MainViewModel ViewModel { get; }
    private bool _segmentTabPresent = true;

    public TabsPanel()
    {
        ViewModel = App.Services.GetRequiredService<MainViewModel>();
        this.InitializeComponent();

        ApplyCollapsedState();
        ApplyPlaybackViewState();

        ViewModel.PropertyChanged += (_, e) =>
        {
            switch (e.PropertyName)
            {
                case nameof(MainViewModel.IsTabsPanelCollapsed):
                    ApplyCollapsedState();
                    break;
                case nameof(MainViewModel.ActivePlaybackView):
                    ApplyPlaybackViewState();
                    break;
            }
        };
    }

    private void ApplyCollapsedState()
    {
        // Show or hide the tab content column
        TabPivot.Visibility = ViewModel.IsTabsPanelCollapsed
            ? Visibility.Collapsed
            : Visibility.Visible;

        // Flip the chevron icon direction
        CollapseIcon.Glyph = ViewModel.IsTabsPanelCollapsed
            ? "\uE76B"   // ChevronLeft  → expand arrow
            : "\uE76C";  // ChevronRight → collapse arrow
    }

    private void ApplyPlaybackViewState()
    {
        var isPlayerView = ViewModel.ActivePlaybackView == PlaybackView.PlayerView;

        if (isPlayerView && !_segmentTabPresent)
        {
            // Re-add Segments tab as first tab and make it active
            TabPivot.Items.Insert(0, SegmentsPivotItem);
            TabPivot.SelectedItem = SegmentsPivotItem;
            _segmentTabPresent = true;
        }
        else if (!isPlayerView && _segmentTabPresent)
        {
            // Remove Segments tab first, then select Tags (now the only item at index 0).
            // Doing it this order avoids setting SelectedItem on a Collapsed Pivot targeting
            // an item at index 1, which can silently fail and corrupt the selection state.
            TabPivot.Items.Remove(SegmentsPivotItem);
            TabPivot.SelectedIndex = 0;
            _segmentTabPresent = false;
        }
    }
}
