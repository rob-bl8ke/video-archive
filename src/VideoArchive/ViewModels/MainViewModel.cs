using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VideoArchive.Models;
using VideoArchive.Services;

namespace VideoArchive.ViewModels;

public enum MainPanel { SearchPlayback, TagManagement, Settings }
public enum PlaybackView { SearchView, PlayerView }

#pragma warning disable MVVMTK0045 // Field-based [ObservableProperty] in WinUI — AOT not required for this app
public partial class MainViewModel : ObservableObject
{
    private readonly IVideoRepository _videoRepository;
    private readonly ISettingsService _settings;
    private List<Video> _allVideos = [];

    public MainViewModel(IVideoRepository videoRepository, ISettingsService settings)
    {
        _videoRepository = videoRepository;
        _settings = settings;
        _isGalleryView = settings.ViewMode == "Gallery";
    }

    [ObservableProperty]
    private ObservableCollection<Video> _videos = [];

    [ObservableProperty]
    private Video? _selectedVideo;

    /// <summary>All currently-selected videos — updated by the active view's selection handler.</summary>
    private List<Video> _selectedVideos = [];
    public List<Video> SelectedVideos
    {
        get => _selectedVideos;
        set { _selectedVideos = value; OnPropertyChanged(); }
    }

    /// <summary>True when a video is selected in the gallery/details view.</summary>
    public bool HasSelection => SelectedVideo is not null;

    partial void OnSelectedVideoChanged(Video? value)
    {
        OnPropertyChanged(nameof(HasSelection));
    }

    /// <summary>Controls which top-level panel is visible in MainWindow.</summary>
    [ObservableProperty]
    private MainPanel _activePanel = MainPanel.SearchPlayback;

    /// <summary>Controls which sub-view is active within the Search & Playback panel.</summary>
    [ObservableProperty]
    private PlaybackView _activePlaybackView = PlaybackView.SearchView;

    /// <summary>True when the right-side TabsPanel is collapsed to its minimal width.</summary>
    [ObservableProperty]
    private bool _isTabsPanelCollapsed;

    /// <summary>Controls Gallery vs Details sub-view inside SearchPanel.</summary>
    [ObservableProperty]
    private bool _isGalleryView;

    [ObservableProperty]
    private string _searchText = string.Empty;

    partial void OnSearchTextChanged(string value)
    {
        ApplySearchFilter();
    }

    [RelayCommand]
    private void ToggleView()
    {
        IsGalleryView = !IsGalleryView;
        _settings.ViewMode = IsGalleryView ? "Gallery" : "Details";
    }

    /// <summary>Switch to PlayerPanel and load the given video for playback.</summary>
    [RelayCommand]
    private void NavigateToPlayer(Video? video = null)
    {
        if (video is not null)
            SelectedVideo = video;
        ActivePanel = MainPanel.SearchPlayback;
        ActivePlaybackView = PlaybackView.PlayerView;
    }

    /// <summary>Switch back to SearchPanel.</summary>
    [RelayCommand]
    private void NavigateToSearch()
    {
        ActivePlaybackView = PlaybackView.SearchView;
    }

    /// <summary>Toggle the collapsed state of the TabsPanel.</summary>
    [RelayCommand]
    private void ToggleTabsPanel()
    {
        IsTabsPanelCollapsed = !IsTabsPanelCollapsed;
    }

    [RelayCommand]
    private async Task LoadVideosAsync()
    {
        var videos = await _videoRepository.GetAllAsync();
        _allVideos = [.. videos];
        ApplySearchFilter();
    }

    [RelayCommand]
    private async Task RefreshLibraryAsync()
    {
        await LoadVideosAsync();
    }

    private void ApplySearchFilter()
    {
        IEnumerable<Video> filtered = _allVideos;

        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            var term = SearchText.Trim();
            filtered = _allVideos.Where(v =>
                (v.Title?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (v.FilePath?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (v.Format?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false));
        }

        // Clear and repopulate instead of replacing the collection
        // so that ItemsSource bindings and CollectionChanged handlers remain valid.
        Videos.Clear();
        foreach (var v in filtered)
            Videos.Add(v);

        // Auto-select first video if nothing is selected
        if (SelectedVideo is null && Videos.Count > 0)
            SelectedVideo = Videos[0];
    }
}
#pragma warning restore MVVMTK0045
