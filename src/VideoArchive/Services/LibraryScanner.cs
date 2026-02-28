using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VideoArchive.Data;
using VideoArchive.Models;

namespace VideoArchive.Services;

public class LibraryScanner(IServiceScopeFactory scopeFactory, IThumbnailService thumbnailService) : ILibraryScanner
{
    private static readonly string[] VideoExtensions = [".mp4", ".mkv"];
    private readonly SemaphoreSlim _scanLock = new(1, 1);

    public async Task ScanAsync(IProgress<(int current, int total)>? progress = null, CancellationToken cancellationToken = default)
    {
        if (!await _scanLock.WaitAsync(0, cancellationToken))
            return; // Already scanning

        try
        {
            using var scope = scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<VideoArchiveContext>();

            // Get active library folders
            var folders = await context.LibraryFolders
                .Where(f => f.IsActive)
                .ToListAsync(cancellationToken);

            if (folders.Count == 0) return;

            // Collect all video files from all folders
            var allFiles = new List<string>();
            foreach (var folder in folders)
            {
                if (!Directory.Exists(folder.Path)) continue;
                try
                {
                    var files = Directory.EnumerateFiles(folder.Path, "*.*", SearchOption.AllDirectories)
                        .Where(f => VideoExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()));
                    allFiles.AddRange(files);
                }
                catch (UnauthorizedAccessException) { /* skip inaccessible folders */ }
            }

            // Load existing videos for comparison
            var existingVideos = await context.Videos.ToListAsync(cancellationToken);
            var existingByPath = new HashSet<string>(
                existingVideos.Select(v => v.FilePath), StringComparer.OrdinalIgnoreCase);

            // New files on disk not yet in DB
            var newFiles = allFiles.Where(f => !existingByPath.Contains(f)).ToList();

            // Videos in DB whose files no longer exist on disk
            var allFileSet = new HashSet<string>(allFiles, StringComparer.OrdinalIgnoreCase);
            var removedVideos = existingVideos.Where(v => !allFileSet.Contains(v.FilePath)).ToList();

            if (removedVideos.Count > 0)
            {
                context.Videos.RemoveRange(removedVideos);
                await context.SaveChangesAsync(cancellationToken);
            }

            // Process new videos
            var total = newFiles.Count;
            for (int i = 0; i < newFiles.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var video = ExtractMetadata(newFiles[i]);
                context.Videos.Add(video);
                await context.SaveChangesAsync(cancellationToken);

                // Generate thumbnail (video.Id is set after SaveChanges)
                try
                {
                    await thumbnailService.GenerateAsync(video.Id, video.FilePath, cancellationToken);
                }
                catch (OperationCanceledException) { throw; }
                catch { /* thumbnail failed — continue without */ }

                progress?.Report((i + 1, total));
            }

            // Update LastScanned timestamp
            foreach (var folder in folders)
            {
                folder.LastScanned = DateTime.UtcNow;
            }
            await context.SaveChangesAsync(cancellationToken);
        }
        finally
        {
            _scanLock.Release();
        }
    }

    private static Video ExtractMetadata(string filePath)
    {
        var fileInfo = new FileInfo(filePath);
        var video = new Video
        {
            FilePath = filePath,
            Title = Path.GetFileNameWithoutExtension(filePath),
            DateAdded = DateTime.UtcNow,
            FileSize = fileInfo.Length,
            Format = Path.GetExtension(filePath).TrimStart('.').ToUpperInvariant(),
        };

        try
        {
            using var tagFile = TagLib.File.Create(filePath);
            video.Duration = tagFile.Properties.Duration;

            if (tagFile.Properties.VideoWidth > 0 && tagFile.Properties.VideoHeight > 0)
                video.Resolution = $"{tagFile.Properties.VideoWidth}x{tagFile.Properties.VideoHeight}";

            var videoCodec = tagFile.Properties.Codecs
                .OfType<TagLib.IVideoCodec>()
                .FirstOrDefault();
            if (videoCodec is not null)
                video.Codec = videoCodec.Description;
        }
        catch
        {
            // File may be corrupted or format unsupported — keep basic metadata
        }

        return video;
    }
}
