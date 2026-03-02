using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using VideoArchive.ViewModels;

namespace VideoArchive.Views;

public sealed partial class SearchPanel : UserControl
{
    public MainViewModel ViewModel { get; }

    public SearchPanel()
    {
        ViewModel = App.Services.GetRequiredService<MainViewModel>();
        this.InitializeComponent();

        UpdateViewVisibility();
        DetailsToggle.IsChecked = !ViewModel.IsGalleryView;

        ViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.IsGalleryView))
            {
                UpdateViewVisibility();
                DetailsToggle.IsChecked = !ViewModel.IsGalleryView;
            }
        };
    }

    private void UpdateViewVisibility()
    {
        GalleryViewControl.Visibility = ViewModel.IsGalleryView ? Visibility.Visible : Visibility.Collapsed;
        DetailsViewControl.Visibility = ViewModel.IsGalleryView ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>
    /// Scrolls the active sub-view to the currently selected video.
    /// </summary>
    public void ScrollToSelected()
    {
        if (ViewModel.IsGalleryView)
            GalleryViewControl.ScrollToSelected();
        else
            DetailsViewControl.ScrollToSelected();
    }

    private void DetailsToggle_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.IsGalleryView = false;
        DetailsToggle.IsChecked = true;
        GalleryToggle.IsChecked = false;
    }

    private void SearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
            ViewModel.SearchText = sender.Text;
    }

    private void SearchBox_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        ViewModel.SearchText = args.QueryText ?? string.Empty;
    }

}
