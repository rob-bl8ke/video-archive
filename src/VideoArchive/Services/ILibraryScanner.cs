namespace VideoArchive.Services;

public record ScanResult(int NewVideos, int RemovedVideos, int Errors);

public interface ILibraryScanner
{
    Task<ScanResult> ScanAsync(IProgress<(int current, int total)>? progress = null, CancellationToken cancellationToken = default);
}
