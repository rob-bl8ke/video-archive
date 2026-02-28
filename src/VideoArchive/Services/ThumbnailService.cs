using LibVLCSharp.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VideoArchive.Data;

namespace VideoArchive.Services;

public class ThumbnailService : IThumbnailService, IDisposable
{
    private static readonly string ThumbnailDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "VideoArchive", "Thumbnails");

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly LibVLC _libVLC;

    public ThumbnailService(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
        Directory.CreateDirectory(ThumbnailDir);
        _libVLC = new LibVLC("--no-audio", "--no-video-title-show");
    }

    public async Task GenerateAsync(int videoId, string videoPath, CancellationToken cancellationToken = default)
    {
        var outputPath = Path.Combine(ThumbnailDir, $"{videoId}.jpg");
        if (File.Exists(outputPath)) return;

        bool generated = false;
        try
        {
            using var media = new Media(_libVLC, videoPath, FromType.FromPath);
            using var player = new MediaPlayer(_libVLC) { Media = media };

            var playingTcs = new TaskCompletionSource<bool>();
            var snapshotTcs = new TaskCompletionSource<bool>();

            player.Playing += (_, _) => playingTcs.TrySetResult(true);
            player.EncounteredError += (_, _) =>
            {
                playingTcs.TrySetResult(false);
                snapshotTcs.TrySetResult(false);
            };
            player.SnapshotTaken += (_, _) => snapshotTcs.TrySetResult(true);

            if (!player.Play())
                return;

            // Wait for playback to start
            var isPlaying = await playingTcs.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
            if (!isPlaying) return;

            // Seek to 10% of duration
            player.Position = 0.1f;
            await Task.Delay(1000, cancellationToken);

            // Take snapshot (320×180)
            player.TakeSnapshot(0, outputPath, 320, 180);

            generated = await snapshotTcs.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);

            player.Stop();
        }
        catch (OperationCanceledException) { throw; }
        catch { /* thumbnail generation failed — skip */ }

        // Update video record with thumbnail path
        if (generated && File.Exists(outputPath))
        {
            using var scope = _scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<VideoArchiveContext>();
            var video = await context.Videos.FindAsync([videoId], cancellationToken);
            if (video is not null)
            {
                video.ThumbnailPath = outputPath;
                await context.SaveChangesAsync(cancellationToken);
            }
        }
    }

    public void CleanOrphaned()
    {
        if (!Directory.Exists(ThumbnailDir)) return;

        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<VideoArchiveContext>();
        var validIds = context.Videos.Select(v => v.Id).ToHashSet();

        foreach (var file in Directory.EnumerateFiles(ThumbnailDir, "*.jpg"))
        {
            var name = Path.GetFileNameWithoutExtension(file);
            if (int.TryParse(name, out var id) && !validIds.Contains(id))
            {
                try { File.Delete(file); } catch { /* best-effort cleanup */ }
            }
        }
    }

    public async Task RebuildAllAsync(IProgress<(int current, int total)>? progress = null, CancellationToken cancellationToken = default)
    {
        // Delete existing thumbnails
        if (Directory.Exists(ThumbnailDir))
        {
            foreach (var file in Directory.EnumerateFiles(ThumbnailDir, "*.jpg"))
            {
                try { File.Delete(file); } catch { }
            }
        }

        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<VideoArchiveContext>();
        var videos = await context.Videos.ToListAsync(cancellationToken);

        for (int i = 0; i < videos.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await GenerateAsync(videos[i].Id, videos[i].FilePath, cancellationToken);
            progress?.Report((i + 1, videos.Count));
        }
    }

    public void Dispose()
    {
        _libVLC.Dispose();
        GC.SuppressFinalize(this);
    }
}
