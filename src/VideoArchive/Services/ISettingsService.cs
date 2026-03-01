namespace VideoArchive.Services;

public interface ISettingsService
{
    string ViewMode { get; set; }
    string SortColumn { get; set; }
    string SortDirection { get; set; }
    string? LastFilterJson { get; set; }
    double WindowWidth { get; set; }
    double WindowHeight { get; set; }
    double WindowLeft { get; set; }
    double WindowTop { get; set; }

    /// <summary>Minimum allowed segment duration in seconds (default: 5).</summary>
    int MinSegmentDurationSeconds { get; set; }

    /// <summary>Default end-time offset (seconds from start) for new segments (default: 10).</summary>
    int DefaultSegmentDurationSeconds { get; set; }
}
