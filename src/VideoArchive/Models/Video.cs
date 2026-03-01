using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace VideoArchive.Models;

public class Video : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged(string name) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public int Id { get; set; }
    public required string FilePath { get; set; }
    public string? Title { get; set; }
    public TimeSpan Duration { get; set; }
    public string? Format { get; set; }
    public string? Resolution { get; set; }
    public string? Codec { get; set; }
    public string? ThumbnailPath { get; set; }
    public DateTime DateAdded { get; set; }
    public long FileSize { get; set; }

    private ObservableCollection<VideoTag> _videoTags;

    public Video()
    {
        _videoTags = [];
        _videoTags.CollectionChanged += VideoTags_CollectionChanged;
    }

    public ObservableCollection<VideoTag> VideoTags
    {
        get => _videoTags;
        set
        {
            if (_videoTags == value) return;
            if (_videoTags is not null)
                _videoTags.CollectionChanged -= VideoTags_CollectionChanged;
            _videoTags = value ?? [];
            _videoTags.CollectionChanged += VideoTags_CollectionChanged;
            OnPropertyChanged(nameof(VideoTags));
        }
    }

    private void VideoTags_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) =>
        OnPropertyChanged(nameof(VideoTags));

    public ICollection<VideoSegment> Segments { get; set; } = [];
}
