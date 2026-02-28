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
}
