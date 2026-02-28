using System.Diagnostics;
using Microsoft.UI.Xaml;

namespace VideoArchive;

public partial class App : Application
{
    private Window? _window;

    public App()
    {
        this.InitializeComponent();
        this.UnhandledException += (s, e) =>
        {
            Debug.WriteLine($"[UnhandledException] {e.Exception}");
            e.Handled = true; // prevent silent exit so debugger can inspect
        };
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window = new MainWindow();
        _window.Activate();
    }
}
