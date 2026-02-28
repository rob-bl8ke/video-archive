using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using VideoArchive.ViewModels;

namespace VideoArchive;

public sealed partial class MainWindow : Window
{
    public MainViewModel ViewModel { get; }

    public MainWindow()
    {
        ViewModel = App.Services.GetRequiredService<MainViewModel>();
        this.InitializeComponent();

        // React to view mode changes
        ViewModel.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.IsGalleryView))
            {
                UpdateViewVisibility();
                // Keep toggle buttons in sync
                DetailsToggle.IsChecked = !ViewModel.IsGalleryView;
            }
        };

        // Select Library nav item by default
        NavView.SelectedItem = NavView.MenuItems[0];
        DetailsToggle.IsChecked = !ViewModel.IsGalleryView;
        UpdateViewVisibility();
    }

    private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItemContainer?.Tag is string tag)
        {
            var isLibrary = tag == "Library";
            SettingsPageControl.Visibility = isLibrary ? Visibility.Collapsed : Visibility.Visible;
            GalleryViewControl.Visibility = isLibrary && ViewModel.IsGalleryView ? Visibility.Visible : Visibility.Collapsed;
            DetailsViewControl.Visibility = isLibrary && !ViewModel.IsGalleryView ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private void DetailsToggle_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.IsGalleryView = false;
        UpdateViewVisibility();
    }

    private void UpdateViewVisibility()
    {
        var isLibrary = SettingsPageControl.Visibility == Visibility.Collapsed;
        if (isLibrary)
        {
            GalleryViewControl.Visibility = ViewModel.IsGalleryView ? Visibility.Visible : Visibility.Collapsed;
            DetailsViewControl.Visibility = ViewModel.IsGalleryView ? Visibility.Collapsed : Visibility.Visible;
        }
    }
}
