using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VideoArchive.Models;
using VideoArchive.Services;

namespace VideoArchive.ViewModels;

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

    [ObservableProperty]
    private bool _isGalleryView;

    [ObservableProperty]
    private bool _isPlayerVisible;

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

    [RelayCommand]
    private void TogglePlayer()
    {
        IsPlayerVisible = !IsPlayerVisible;
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

        Videos = new ObservableCollection<Video>(filtered);
    }
}
#pragma warning restore MVVMTK0045
