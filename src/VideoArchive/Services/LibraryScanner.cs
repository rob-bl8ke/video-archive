namespace VideoArchive.Services;

public class LibraryScanner : ILibraryScanner
{
    // TODO: Implement in Phase 5
    public Task ScanAsync(IProgress<(int current, int total)>? progress = null, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}
