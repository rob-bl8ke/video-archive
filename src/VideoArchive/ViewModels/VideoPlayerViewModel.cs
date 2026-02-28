using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LibVLCSharp.Shared;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Dispatching;
using VideoArchive.Data;
using VideoArchive.Models;

namespace VideoArchive.ViewModels;

#pragma warning disable MVVMTK0045
public partial class VideoPlayerViewModel : ObservableObject, IDisposable
{
    private readonly LibVLC _libVLC;
    private readonly MediaPlayer _mediaPlayer;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly DispatcherQueue _dispatcher;
    private Media? _currentMedia;

    public VideoPlayerViewModel(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
        _dispatcher = DispatcherQueue.GetForCurrentThread();
        _libVLC = new LibVLC("--no-video-title-show");
        _mediaPlayer = new MediaPlayer(_libVLC);

        // LibVLC events fire on background threads — must marshal to UI thread
        _mediaPlayer.Playing += (_, _) => _dispatcher.TryEnqueue(() => IsPlaying = true);
        _mediaPlayer.Paused += (_, _) => _dispatcher.TryEnqueue(() => IsPlaying = false);
        _mediaPlayer.Stopped += (_, _) => _dispatcher.TryEnqueue(() =>
        {
            IsPlaying = false;
            Position = 0;
            CurrentTimeText = "--:--";
        });
        _mediaPlayer.EndReached += (_, _) => _dispatcher.TryEnqueue(() =>
        {
            IsPlaying = false;
            Position = 0;
        });
    }

    public MediaPlayer MediaPlayer => _mediaPlayer;

    [ObservableProperty]
    private bool _isPlaying;

    [ObservableProperty]
    private double _position; // 0.0 – 1.0

    [ObservableProperty]
    private int _volume = 80;

    [ObservableProperty]
    private string _currentTimeText = "--:--";

    [ObservableProperty]
    private string _totalTimeText = "--:--";

    [ObservableProperty]
    private string _nowPlayingTitle = string.Empty;

    [ObservableProperty]
    private Video? _currentVideo;

    [ObservableProperty]
    private ObservableCollection<VideoSegment> _segments = [];

    private bool _isSeeking;

    /// <summary>
    /// Called by the DispatcherQueue timer to update position/time display.
    /// </summary>
    public void UpdateTimeline()
    {
        if (_isSeeking || !_mediaPlayer.IsPlaying) return;

        Position = _mediaPlayer.Position;

        var current = TimeSpan.FromMilliseconds(_mediaPlayer.Time);
        var total = TimeSpan.FromMilliseconds(_mediaPlayer.Length);

        CurrentTimeText = FormatTime(current);
        TotalTimeText = FormatTime(total);
    }

    /// <summary>
    /// Begin seeking — pauses timeline updates so the slider doesn't fight the user.
    /// </summary>
    public void BeginSeek() => _isSeeking = true;

    /// <summary>
    /// Commit seek position to the media player.
    /// </summary>
    public void EndSeek()
    {
        _mediaPlayer.Position = (float)Position;
        _isSeeking = false;
    }

    partial void OnVolumeChanged(int value)
    {
        _mediaPlayer.Volume = value;
    }

    [RelayCommand]
    private void PlayPause()
    {
        // LibVLC Play/Pause must run off the UI thread to avoid deadlock.
        if (_mediaPlayer.IsPlaying)
        {
            Task.Run(() => _mediaPlayer.Pause());
        }
        else if (_currentMedia is not null)
        {
            // Resume existing media
            Task.Run(() => _mediaPlayer.Play());
        }
        else
        {
            // No media loaded yet — play the currently selected video
            var mainVm = App.Services.GetRequiredService<MainViewModel>();
            if (mainVm.SelectedVideo is not null)
                Play(mainVm.SelectedVideo);
        }
    }

    [RelayCommand]
    private void Stop()
    {
        // Stop must be called from a thread pool thread to avoid deadlock
        Task.Run(() => _mediaPlayer.Stop());
    }

    public void Play(Video video)
    {
        CurrentVideo = video;
        NowPlayingTitle = video.Title ?? Path.GetFileNameWithoutExtension(video.FilePath);

        _currentMedia?.Dispose();
        _currentMedia = new Media(_libVLC, video.FilePath, FromType.FromPath);
        _mediaPlayer.Media = _currentMedia;
        _mediaPlayer.Volume = Volume;

        // Play on thread pool — LibVLC can block the UI thread while opening media
        Task.Run(() => _mediaPlayer.Play());

        LoadSegments(video.Id);
    }

    private async void LoadSegments(int videoId)
    {
        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<VideoArchiveContext>();
        var segments = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions
            .ToListAsync(context.VideoSegments.Where(s => s.VideoId == videoId));
        Segments = new ObservableCollection<VideoSegment>(segments);
    }

    [RelayCommand]
    private async Task AddSegmentAsync()
    {
        if (CurrentVideo is null) return;

        var currentTime = TimeSpan.FromMilliseconds(_mediaPlayer.Time);
        var segment = new VideoSegment
        {
            VideoId = CurrentVideo.Id,
            Name = $"Segment {Segments.Count + 1}",
            StartTime = currentTime,
            EndTime = currentTime + TimeSpan.FromSeconds(10),
        };

        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<VideoArchiveContext>();
        context.VideoSegments.Add(segment);
        await context.SaveChangesAsync();

        Segments.Add(segment);
    }

    [RelayCommand]
    private async Task DeleteSegmentAsync(VideoSegment? segment)
    {
        if (segment is null) return;

        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<VideoArchiveContext>();
        var entity = await context.VideoSegments.FindAsync(segment.Id);
        if (entity is not null)
        {
            context.VideoSegments.Remove(entity);
            await context.SaveChangesAsync();
        }

        Segments.Remove(segment);
    }

    public void SetStartTime(VideoSegment segment)
    {
        segment.StartTime = TimeSpan.FromMilliseconds(_mediaPlayer.Time);
        SaveSegment(segment);
    }

    public void SetEndTime(VideoSegment segment)
    {
        segment.EndTime = TimeSpan.FromMilliseconds(_mediaPlayer.Time);
        SaveSegment(segment);
    }

    private async void SaveSegment(VideoSegment segment)
    {
        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<VideoArchiveContext>();
        context.VideoSegments.Update(segment);
        await context.SaveChangesAsync();
    }

    private static string FormatTime(TimeSpan ts)
    {
        return ts.TotalHours >= 1
            ? ts.ToString(@"h\:mm\:ss")
            : ts.ToString(@"m\:ss");
    }

    public void Dispose()
    {
        _mediaPlayer.Stop();
        _mediaPlayer.Dispose();
        _currentMedia?.Dispose();
        _libVLC.Dispose();
        GC.SuppressFinalize(this);
    }
}
#pragma warning restore MVVMTK0045
