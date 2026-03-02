using System.Runtime.InteropServices;
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

    // ── Minimal Win32 surface so LibVLC never creates a visible window ──────────
    private const string HiddenClassName = "VLCThumbSurface";
    private static bool _wndClassRegistered;
    private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    private static readonly WndProcDelegate _wndProc = (h, m, w, l) =>
        DefWindowProcW(h, m, w, l);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASS
    {
        public uint style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra, cbWndExtra;
        public IntPtr hInstance, hIcon, hCursor, hbrBackground;
        public string? lpszMenuName;
        public string lpszClassName;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern ushort RegisterClassW(ref WNDCLASS wc);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr CreateWindowExW(uint exStyle, string cls, string? title, uint style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr inst, IntPtr param);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern IntPtr DefWindowProcW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandleW(string? name);

    private const uint WS_POPUP         = 0x80000000;
    private const uint WS_EX_NOACTIVATE = 0x08000000;
    private const uint WS_EX_TOOLWINDOW = 0x00000080;

    private static void EnsureWndClass()
    {
        if (_wndClassRegistered) return;
        var wc = new WNDCLASS
        {
            lpfnWndProc  = Marshal.GetFunctionPointerForDelegate(_wndProc),
            hInstance    = GetModuleHandleW(null),
            lpszClassName = HiddenClassName,
        };
        RegisterClassW(ref wc);
        _wndClassRegistered = true;
    }

    /// <summary>
    /// Creates a 1×1 hidden, non-activating Win32 window that LibVLC can render
    /// into. Returns <see cref="IntPtr.Zero"/> on failure.
    /// </summary>
    private static IntPtr CreateHiddenSurface() =>
        CreateWindowExW(
            WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW,
            HiddenClassName, null,
            WS_POPUP,
            0, 0, 1, 1,
            IntPtr.Zero, IntPtr.Zero,
            GetModuleHandleW(null), IntPtr.Zero);
    // ────────────────────────────────────────────────────────────────────────────

    public ThumbnailService(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
        Directory.CreateDirectory(ThumbnailDir);
        _libVLC = new LibVLC("--no-audio", "--no-video-title-show");
        EnsureWndClass();
    }

    public async Task GenerateAsync(int videoId, string videoPath, CancellationToken cancellationToken = default)
    {
        var outputPath = Path.Combine(ThumbnailDir, $"{videoId}.jpg");
        if (File.Exists(outputPath)) return;

        bool generated = false;
        var surface = CreateHiddenSurface();
        try
        {
            using var media = new Media(_libVLC, videoPath, FromType.FromPath);
            using var player = new MediaPlayer(_libVLC) { Media = media };

            // Render into the hidden surface so LibVLC never opens a visible window.
            if (surface != IntPtr.Zero)
                player.Hwnd = surface;

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
        finally
        {
            if (surface != IntPtr.Zero)
                DestroyWindow(surface);
        }

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
