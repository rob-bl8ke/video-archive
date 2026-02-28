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
        Videos = new ObservableCollection<Video>(videos);
    }

    [RelayCommand]
    private async Task RefreshLibraryAsync()
    {
        await LoadVideosAsync();
    }
}
#pragma warning restore MVVMTK0045
