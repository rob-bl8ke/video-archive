using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using VideoArchive.Data;
using VideoArchive.Services;
using VideoArchive.ViewModels;

namespace VideoArchive;

public partial class App : Application
{
    private Window? _window;

    public static IServiceProvider Services { get; private set; } = null!;

    public App()
    {
        this.InitializeComponent();

        var services = new ServiceCollection();

        // Data
        services.AddDbContext<VideoArchiveContext>();

        // Services
        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<ILibraryScanner, LibraryScanner>();
        services.AddSingleton<IThumbnailService, ThumbnailService>();
        services.AddScoped<IVideoRepository, VideoRepository>();
        services.AddScoped<ITagService, TagService>();

        // ViewModels
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<VideoPlayerViewModel>();
        services.AddTransient<SettingsViewModel>();

        Services = services.BuildServiceProvider();

        // Ensure database is up to date
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VideoArchiveContext>();
        db.Database.Migrate();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window = new MainWindow();
        VideoArchive.Views.WindowHelper.TrackWindow(_window);
        _window.Closed += (_, _) =>
        {
            // Stop the player timer before disposing LibVLC to prevent accessing freed objects
            if (_window is MainWindow mw)
                mw.ShutdownPlayer();

            // Dispose LibVLC resources on shutdown
            if (Services.GetService<VideoPlayerViewModel>() is IDisposable playerVm)
                playerVm.Dispose();
            if (Services.GetService<IThumbnailService>() is IDisposable thumbSvc)
                thumbSvc.Dispose();
        };
        _window.Activate();
    }
}
