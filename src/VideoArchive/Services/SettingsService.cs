namespace VideoArchive.Services;

public class SettingsService : ISettingsService
{
    // TODO: Implement persistence in Phase 3.2
    public string ViewMode { get; set; } = "Gallery";
    public string SortColumn { get; set; } = "Title";
    public string SortDirection { get; set; } = "Ascending";
    public string? LastFilterJson { get; set; }
    public double WindowWidth { get; set; } = 1280;
    public double WindowHeight { get; set; } = 800;
    public double WindowLeft { get; set; }
    public double WindowTop { get; set; }
}
