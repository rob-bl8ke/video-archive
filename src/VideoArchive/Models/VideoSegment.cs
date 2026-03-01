using CommunityToolkit.Mvvm.ComponentModel;

namespace VideoArchive.Models;

/// <summary>
/// Represents a named time range within a video.
/// Inherits <see cref="ObservableObject"/> so that property changes
/// are reflected immediately in the UI (ListView bindings, etc.).
/// EF Core can still track instances — only key/FK properties are plain auto-props.
/// </summary>
#pragma warning disable MVVMTK0045
public partial class VideoSegment : ObservableObject
{
    // ── Key / FK — plain properties (EF Core needs simple get/set) ──
    public int Id { get; set; }
    public int VideoId { get; set; }
    public Video Video { get; set; } = null!;

    // ── Observable properties — UI-bound, raise PropertyChanged ──

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private TimeSpan _startTime;

    [ObservableProperty]
    private TimeSpan _endTime;

    [ObservableProperty]
    private string? _description;
}
#pragma warning restore MVVMTK0045
