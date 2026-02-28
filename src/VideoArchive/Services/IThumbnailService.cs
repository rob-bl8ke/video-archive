namespace VideoArchive.Services;

public interface IThumbnailService
{
    Task GenerateAsync(int videoId, string videoPath, CancellationToken cancellationToken = default);
    void CleanOrphaned();
    Task RebuildAllAsync(IProgress<(int current, int total)>? progress = null, CancellationToken cancellationToken = default);
}
