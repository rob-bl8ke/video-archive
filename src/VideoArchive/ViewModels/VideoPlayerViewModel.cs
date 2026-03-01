using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LibVLCSharp.Shared;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Dispatching;
using VideoArchive.Data;
using VideoArchive.Models;

namespace VideoArchive.ViewModels;

/// <summary>
/// Defines the discrete states the video player can be in.
/// Transitions:
///   Idle ──► Playing  (Play a video)
///   Playing ──► Paused  (Pause)
///   Paused ──► Playing  (Resume)
///   Playing ──► Stopped  (Stop / EndReached)
///   Paused ──► Stopped  (Stop)
///   Stopped ──► Playing  (Play a new video)
///   Stopped ──► Idle  (media cleared)
/// </summary>
public enum PlaybackState
{
    /// <summary>No media loaded — initial state or after media is cleared.</summary>
    Idle,
    /// <summary>Media is actively playing.</summary>
    Playing,
    /// <summary>Media is loaded but paused.</summary>
    Paused,
    /// <summary>Playback ended or was stopped; media reference may still exist.</summary>
    Stopped,
}

#pragma warning disable MVVMTK0045
public partial class VideoPlayerViewModel : ObservableObject, IDisposable
{
    private readonly LibVLC _libVLC;
    private readonly MediaPlayer _mediaPlayer;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly DispatcherQueue _dispatcher;
    private Media? _currentMedia;
    private volatile bool _disposed;
    private volatile bool _isPreviewing;

    public VideoPlayerViewModel(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
        _dispatcher = DispatcherQueue.GetForCurrentThread();
        _libVLC = new LibVLC("--no-video-title-show");
        _mediaPlayer = new MediaPlayer(_libVLC);

        // LibVLC events fire on background threads — marshal state transitions to UI thread
        _mediaPlayer.Playing += (_, _) =>
        {
            // If we're previewing, pause after a brief delay so LibVLC has time
            // to decode and render the first frame. Pausing immediately in the
            // Playing event results in a blank surface because no frame has been
            // pushed to the video output yet.
            if (_isPreviewing)
            {
                _isPreviewing = false;
                Task.Run(async () =>
                {
                    await Task.Delay(200);
                    if (!_disposed)
                        _mediaPlayer.Pause();
                });
                return;
            }
            _dispatcher.TryEnqueue(() => State = PlaybackState.Playing);
        };
        _mediaPlayer.Paused += (_, _) => _dispatcher.TryEnqueue(() => State = PlaybackState.Paused);
        _mediaPlayer.Stopped += (_, _) => _dispatcher.TryEnqueue(() =>
        {
            State = PlaybackState.Stopped;
            Position = 0;
            CurrentTimeText = "--:--";
        });
        _mediaPlayer.EndReached += (_, _) => _dispatcher.TryEnqueue(() =>
        {
            State = PlaybackState.Stopped;
            Position = 0;
        });

        // Re-evaluate CanPlay when the gallery selection changes,
        // and stop playback when a different video is selected mid-play.
        var mainVm = App.Services.GetRequiredService<MainViewModel>();
        mainVm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.HasSelection))
                OnPropertyChanged(nameof(CanPlay));

            if (e.PropertyName == nameof(MainViewModel.SelectedVideo)
                && mainVm.IsPlayerVisible
                && mainVm.SelectedVideo is not null
                && mainVm.SelectedVideo.Id != CurrentVideo?.Id)
            {
                // Different video selected while player is visible → preview first frame
                Preview(mainVm.SelectedVideo);
            }
        };
    }

    public MediaPlayer MediaPlayer => _mediaPlayer;

    // ── Playback state machine ───────────────────────────────────────

    [ObservableProperty]
    private PlaybackState _state = PlaybackState.Idle;

    /// <summary>Raised whenever State changes — updates all derived boolean properties.</summary>
    partial void OnStateChanged(PlaybackState value)
    {
        OnPropertyChanged(nameof(IsPlaying));
        OnPropertyChanged(nameof(IsPaused));
        OnPropertyChanged(nameof(IsIdle));
        OnPropertyChanged(nameof(HasMedia));
        OnPropertyChanged(nameof(CanInteract));
        OnPropertyChanged(nameof(CanPlay));
    }

    /// <summary>True when actively playing.</summary>
    public bool IsPlaying => State == PlaybackState.Playing;

    /// <summary>True when paused (media still loaded).</summary>
    public bool IsPaused => State == PlaybackState.Paused;

    /// <summary>True when no media is loaded.</summary>
    public bool IsIdle => State == PlaybackState.Idle;

    /// <summary>True when media is loaded (playing, paused, or stopped with media).</summary>
    public bool HasMedia => _currentMedia is not null;

    /// <summary>True when NOT playing — safe to open dialogs, change selection, etc.</summary>
    public bool CanInteract => State != PlaybackState.Playing;

    /// <summary>
    /// True when the Play/Pause button can do something meaningful:
    /// - Playing or Paused: always actionable (pause/resume)
    /// - Idle or Stopped: only if a video is selected in the gallery
    /// </summary>
    public bool CanPlay => State switch
    {
        PlaybackState.Playing => true,
        PlaybackState.Paused => true,
        _ => App.Services.GetRequiredService<MainViewModel>().HasSelection,
    };

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
        if (_disposed || _isSeeking || State != PlaybackState.Playing) return;

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
        var pos = (float)Position;
        Task.Run(() => _mediaPlayer.Position = pos);
        _isSeeking = false;
    }

    partial void OnVolumeChanged(int value)
    {
        _mediaPlayer.Volume = value;
    }

    [RelayCommand]
    private void PlayPause()
    {
        if (_disposed) return;

        switch (State)
        {
            case PlaybackState.Playing:
                // Currently playing → pause
                Task.Run(() => _mediaPlayer.Pause());
                break;

            case PlaybackState.Paused:
                // Paused with media loaded → resume
                Task.Run(() => _mediaPlayer.Play());
                break;

            case PlaybackState.Stopped:
            case PlaybackState.Idle:
                // No active playback → play the currently selected video
                if (_currentMedia is not null)
                {
                    // Stopped but media still assigned (shouldn't normally happen after Stop clears it)
                    Task.Run(() => _mediaPlayer.Play());
                }
                else
                {
                    var mainVm = App.Services.GetRequiredService<MainViewModel>();
                    if (mainVm.SelectedVideo is not null)
                        Play(mainVm.SelectedVideo);
                }
                break;
        }
    }

    [RelayCommand]
    private void Stop()
    {
        if (State == PlaybackState.Idle) return;

        // Stop must be called from a thread pool thread to avoid deadlock
        Task.Run(() => _mediaPlayer.Stop());

        // Clear the current media and transition to Idle
        _currentMedia?.Dispose();
        _currentMedia = null;
        CurrentVideo = null;
        State = PlaybackState.Idle;
        OnPropertyChanged(nameof(HasMedia));
    }

    public void Play(Video video)
    {
        _isPreviewing = false;
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

    /// <summary>
    /// Loads a video and pauses on the first frame (preview mode).
    /// Shows the first frame without starting continuous playback.
    /// </summary>
    public void Preview(Video video)
    {
        if (_disposed) return;

        // Cancel any in-flight preview before starting a new one
        _isPreviewing = false;

        CurrentVideo = video;
        NowPlayingTitle = video.Title ?? Path.GetFileNameWithoutExtension(video.FilePath);

        _currentMedia?.Dispose();
        _currentMedia = new Media(_libVLC, video.FilePath, FromType.FromPath);
        _mediaPlayer.Media = _currentMedia;
        _mediaPlayer.Volume = Volume;

        // Set flag so the Playing event handler will auto-pause on first frame.
        // Assigning new Media + calling Play() causes LibVLC to stop internally,
        // so no explicit Stop() call is needed.
        _isPreviewing = true;
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
        if (_disposed) return;
        _disposed = true;

        // Stop on a thread-pool thread and wait, so the native player
        // is idle before we tear down the objects it depends on.
        try { Task.Run(() => _mediaPlayer.Stop()).Wait(TimeSpan.FromSeconds(2)); }
        catch { /* timeout or already stopped */ }

        _currentMedia?.Dispose();
        _currentMedia = null;
        _mediaPlayer.Dispose();
        _libVLC.Dispose();
        GC.SuppressFinalize(this);
    }
}
#pragma warning restore MVVMTK0045
