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
            // Re-add Segments tab
            TabPivot.Items.Add(SegmentsPivotItem);
            _segmentTabPresent = true;
        }
        else if (!isPlayerView && _segmentTabPresent)
        {
            // Switch to Tags tab first so the pivot doesn't land on a removed item
            TabPivot.SelectedItem = TagsPivotItem;

            // Remove Segments tab — state is preserved in the SegmentPanel control instance
            TabPivot.Items.Remove(SegmentsPivotItem);
            _segmentTabPresent = false;
        }
    }
}
