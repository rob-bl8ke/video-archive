namespace VideoArchive.Services;

public class ThumbnailService : IThumbnailService
{
    // TODO: Implement in Phase 5
    public Task GenerateAsync(int videoId, string videoPath, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public void CleanOrphaned() { }

    public Task RebuildAllAsync(IProgress<(int current, int total)>? progress = null, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}
