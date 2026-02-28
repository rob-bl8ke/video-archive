namespace VideoArchive.Services;

public interface ILibraryScanner
{
    Task ScanAsync(IProgress<(int current, int total)>? progress = null, CancellationToken cancellationToken = default);
}
