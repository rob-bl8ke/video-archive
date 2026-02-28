namespace VideoArchive.Models;

public class VideoSegment
{
    public int Id { get; set; }
    public int VideoId { get; set; }
    public Video Video { get; set; } = null!;
    public required string Name { get; set; }
    public TimeSpan StartTime { get; set; }
    public TimeSpan EndTime { get; set; }
    public string? Description { get; set; }
}
