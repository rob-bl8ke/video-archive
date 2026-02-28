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
        services.AddTransient<MainViewModel>();
        services.AddTransient<VideoPlayerViewModel>();
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
        _window.Activate();
    }
}
