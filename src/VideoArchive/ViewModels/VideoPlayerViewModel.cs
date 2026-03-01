using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LibVLCSharp.Shared;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Dispatching;
using VideoArchive.Data;
using VideoArchive.Models;
using VideoArchive.Services;

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

    /// <summary>The segment currently being played (null when not in segment playback mode).</summary>
    [ObservableProperty]
    private VideoSegment? _activeSegment;

    /// <summary>
    /// The segment currently selected for editing/focus. Independent of playback.
    /// Cleared when the main global Play/Pause starts free (non-segment) playback.
    /// </summary>
    [ObservableProperty]
    private VideoSegment? _selectedSegment;

    /// <summary>When true, segment playback loops back to StartTime on reaching EndTime.</summary>
    [ObservableProperty]
    private bool _isSegmentLooping;

    /// <summary>Frames per second of the currently loaded video; 0 when unknown.</summary>
    [ObservableProperty]
    private float _videoFps;

    private bool _isSeeking;

    /// <summary>
    /// Called by the DispatcherQueue timer to update position/time display.
    /// Also enforces segment playback boundaries.
    /// </summary>
    public void UpdateTimeline()
    {
        if (_disposed || _isSeeking || State != PlaybackState.Playing) return;

        Position = _mediaPlayer.Position;

        var current = TimeSpan.FromMilliseconds(_mediaPlayer.Time);
        var total = TimeSpan.FromMilliseconds(_mediaPlayer.Length);

        CurrentTimeText = FormatTime(current);
        TotalTimeText = FormatTime(total);

        // Keep VideoFps in sync — only update when value changes meaningfully
        if (_mediaPlayer.Fps > 0f && Math.Abs(VideoFps - _mediaPlayer.Fps) > 0.01f)
            VideoFps = _mediaPlayer.Fps;

        // Segment boundary enforcement
        if (ActiveSegment is not null && _mediaPlayer.Time >= (long)ActiveSegment.EndTime.TotalMilliseconds)
        {
            if (IsSegmentLooping)
            {
                SeekToTime(ActiveSegment.StartTime);
            }
            else
            {
                Task.Run(() => _mediaPlayer.Pause());
                StopSegmentPlayback();
            }
        }
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

    /// <summary>Seek the media player to the specified time.</summary>
    private void SeekToTime(TimeSpan time)
    {
        Task.Run(() => _mediaPlayer.Time = (long)time.TotalMilliseconds);
    }

    /// <summary>
    /// Seek to the given time so the user can see the frame — but only when
    /// not actively playing (paused/stopped). While playing, the seek would
    /// fight the normal playback position and should be skipped.
    /// </summary>
    private void PreviewTime(TimeSpan time)
    {
        if (State != PlaybackState.Playing && _currentMedia is not null)
            SeekToTime(time);
    }

    /// <summary>Exit segment playback mode without affecting the media player state.</summary>
    private void StopSegmentPlayback()
    {
        ActiveSegment = null;
        IsSegmentLooping = false;
    }

    /// <summary>
    /// Selects a segment, seeks to its StartTime, and begins playback.
    /// Called when a segment card is tapped or its name field receives focus.
    /// </summary>
    public void ActivateSegment(VideoSegment segment)
    {
        SelectedSegment = segment;
        ActiveSegment   = segment;
        SeekToTime(segment.StartTime);
        if (State != PlaybackState.Playing)
            Task.Run(() => _mediaPlayer.Play());
    }

    [RelayCommand]
    private void SegmentPlayPause(VideoSegment? segment)
    {
        if (_disposed || segment is null) return;

        if (ActiveSegment?.Id == segment.Id)
        {
            // Same segment — toggle play/pause
            if (State == PlaybackState.Playing)
                Task.Run(() => _mediaPlayer.Pause());
            else
                Task.Run(() => _mediaPlayer.Play());
            return;
        }

        // Different segment (or no active segment) — start playing this one
        ActiveSegment = segment;
        SeekToTime(segment.StartTime);

        if (State != PlaybackState.Playing)
            Task.Run(() => _mediaPlayer.Play());
    }

    [RelayCommand]
    private void SegmentStop()
    {
        if (ActiveSegment is null) return;
        Task.Run(() => _mediaPlayer.Pause());
        StopSegmentPlayback();
    }

    [RelayCommand]
    private void SegmentToggleLoop()
    {
        IsSegmentLooping = !IsSegmentLooping;
    }

    [RelayCommand]
    private void PlayPause()
    {
        if (_disposed) return;

        StopSegmentPlayback();
        SelectedSegment = null;   // Main controls override any editing selection

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

        StopSegmentPlayback();

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
        VideoFps = 0f;
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
        VideoFps = 0f;

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
            .ToListAsync(context.VideoSegments
                .Where(s => s.VideoId == videoId));
        // SQLite doesn't support OrderBy on TimeSpan — sort client-side
        segments.Sort((a, b) => a.StartTime.CompareTo(b.StartTime));
        Segments = new ObservableCollection<VideoSegment>(segments);
    }

    // ── Segment helpers ──────────────────────────────────────────────

    /// <summary>Read the configurable minimum segment duration.</summary>
    private TimeSpan MinDuration
    {
        get
        {
            var settings = App.Services.GetRequiredService<ISettingsService>();
            return TimeSpan.FromSeconds(Math.Max(1, settings.MinSegmentDurationSeconds));
        }
    }

    /// <summary>Read the configurable default segment duration for new segments.</summary>
    private TimeSpan DefaultDuration
    {
        get
        {
            var settings = App.Services.GetRequiredService<ISettingsService>();
            return TimeSpan.FromSeconds(Math.Max(1, settings.DefaultSegmentDurationSeconds));
        }
    }

    /// <summary>Get the total duration of the currently loaded media.</summary>
    private TimeSpan VideoDuration =>
        _mediaPlayer.Length > 0 ? TimeSpan.FromMilliseconds(_mediaPlayer.Length) : TimeSpan.MaxValue;

    [RelayCommand]
    private async Task AddSegmentAsync()
    {
        if (CurrentVideo is null) return;

        var currentTime = TimeSpan.FromMilliseconds(_mediaPlayer.Time);
        var videoDuration = VideoDuration;

        // Compute a unique auto-name based on existing segment names
        var nextNumber = 1;
        foreach (var s in Segments)
        {
            if (s.Name.StartsWith("Segment ", StringComparison.Ordinal)
                && int.TryParse(s.Name.AsSpan("Segment ".Length), out var n)
                && n >= nextNumber)
            {
                nextNumber = n + 1;
            }
        }

        var endTime = currentTime + DefaultDuration;
        if (endTime > videoDuration) endTime = videoDuration;
        // Ensure minimum duration if possible
        if (endTime - currentTime < MinDuration && currentTime + MinDuration <= videoDuration)
            endTime = currentTime + MinDuration;

        var segment = new VideoSegment
        {
            VideoId = CurrentVideo.Id,
            Name = $"Segment {nextNumber}",
            StartTime = currentTime,
            EndTime = endTime,
        };

        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<VideoArchiveContext>();
        context.VideoSegments.Add(segment);
        await context.SaveChangesAsync();

        // Insert sorted by StartTime
        var index = 0;
        while (index < Segments.Count && Segments[index].StartTime <= segment.StartTime)
            index++;
        Segments.Insert(index, segment);
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

    /// <summary>Rename a segment. Empty names are rejected.</summary>
    [RelayCommand]
    private async Task RenameSegmentAsync(VideoSegment? segment)
    {
        if (segment is null || string.IsNullOrWhiteSpace(segment.Name)) return;
        await SaveSegmentAsync(segment);
    }

    /// <summary>Set segment start time from the current playhead position.</summary>
    public void SetStartTime(VideoSegment segment)
    {
        var newStart = TimeSpan.FromMilliseconds(_mediaPlayer.Time);
        var maxStart = segment.EndTime - MinDuration;
        if (maxStart < TimeSpan.Zero) maxStart = TimeSpan.Zero;
        if (newStart > maxStart) newStart = maxStart;
        if (newStart < TimeSpan.Zero) newStart = TimeSpan.Zero;

        segment.StartTime = newStart;
        _ = SaveSegmentAsync(segment);
        PreviewTime(newStart);
    }

    /// <summary>Set segment end time from the current playhead position.</summary>
    public void SetEndTime(VideoSegment segment)
    {
        var newEnd = TimeSpan.FromMilliseconds(_mediaPlayer.Time);
        var minEnd = segment.StartTime + MinDuration;
        if (newEnd < minEnd) newEnd = minEnd;
        var videoDuration = VideoDuration;
        if (newEnd > videoDuration) newEnd = videoDuration;

        segment.EndTime = newEnd;
        _ = SaveSegmentAsync(segment);
        PreviewTime(newEnd);
    }

    /// <summary>Adjust the start time by the given number of seconds (positive or negative).</summary>
    [RelayCommand]
    private async Task AdjustStartTimeAsync((VideoSegment segment, double seconds) args)
    {
        var (segment, seconds) = args;
        var newStart = segment.StartTime + TimeSpan.FromSeconds(seconds);

        // Clamp: cannot go below zero
        if (newStart < TimeSpan.Zero) newStart = TimeSpan.Zero;

        // Clamp: must maintain minimum duration
        var maxStart = segment.EndTime - MinDuration;
        if (maxStart < TimeSpan.Zero) maxStart = TimeSpan.Zero;
        if (newStart > maxStart) newStart = maxStart;

        segment.StartTime = newStart;
        await SaveSegmentAsync(segment);
        PreviewTime(newStart);
    }

    /// <summary>Adjust the end time by the given number of seconds (positive or negative).</summary>
    [RelayCommand]
    private async Task AdjustEndTimeAsync((VideoSegment segment, double seconds) args)
    {
        var (segment, seconds) = args;
        var newEnd = segment.EndTime + TimeSpan.FromSeconds(seconds);

        // Clamp: must maintain minimum duration
        var minEnd = segment.StartTime + MinDuration;
        if (newEnd < minEnd) newEnd = minEnd;

        // Clamp: cannot exceed video duration
        var videoDuration = VideoDuration;
        if (newEnd > videoDuration) newEnd = videoDuration;

        segment.EndTime = newEnd;
        await SaveSegmentAsync(segment);
        PreviewTime(newEnd);
    }

    /// <summary>Adjust the start time by the given number of frames (positive or negative).</summary>
    [RelayCommand]
    private async Task AdjustStartFrameAsync((VideoSegment segment, int frames) args)
    {
        var (segment, frames) = args;
        if (VideoFps <= 0f) return;
        var newStart = segment.StartTime + TimeSpan.FromSeconds(frames / (double)VideoFps);

        if (newStart < TimeSpan.Zero) newStart = TimeSpan.Zero;
        var maxStart = segment.EndTime - MinDuration;
        if (maxStart < TimeSpan.Zero) maxStart = TimeSpan.Zero;
        if (newStart > maxStart) newStart = maxStart;

        segment.StartTime = newStart;
        await SaveSegmentAsync(segment);
        PreviewTime(newStart);
    }

    /// <summary>Adjust the end time by the given number of frames (positive or negative).</summary>
    [RelayCommand]
    private async Task AdjustEndFrameAsync((VideoSegment segment, int frames) args)
    {
        var (segment, frames) = args;
        if (VideoFps <= 0f) return;
        var newEnd = segment.EndTime + TimeSpan.FromSeconds(frames / (double)VideoFps);

        var minEnd = segment.StartTime + MinDuration;
        if (newEnd < minEnd) newEnd = minEnd;
        var videoDuration = VideoDuration;
        if (newEnd > videoDuration) newEnd = videoDuration;

        segment.EndTime = newEnd;
        await SaveSegmentAsync(segment);
        PreviewTime(newEnd);
    }

    /// <summary>Persist the segment to the database.</summary>
    private async Task SaveSegmentAsync(VideoSegment segment)
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
