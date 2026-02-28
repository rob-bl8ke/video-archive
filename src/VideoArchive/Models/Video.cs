namespace VideoArchive.Models;

public class Video
{
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

    public ICollection<VideoTag> VideoTags { get; set; } = [];
    public ICollection<VideoSegment> Segments { get; set; } = [];
}
