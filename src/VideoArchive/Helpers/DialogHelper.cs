using Microsoft.UI.Xaml.Controls;
using VideoArchive.Views;

namespace VideoArchive.Helpers;

/// <summary>
/// Wraps ContentDialog.ShowAsync to temporarily hide the native video overlay
/// so it doesn't render on top of the dialog.
/// </summary>
public static class DialogHelper
{
    /// <summary>
    /// Shows a ContentDialog while hiding the native video popup overlay.
    /// </summary>
    public static async Task<ContentDialogResult> ShowWithOverlayHiddenAsync(ContentDialog dialog)
    {
        var mainWindow = GetMainWindow();
        mainWindow?.HideVideoOverlay();
        try
        {
            return await dialog.ShowAsync();
        }
        finally
        {
            mainWindow?.ShowVideoOverlay();
        }
    }

    /// <summary>Hide the video overlay (for fire-and-forget dialog patterns).</summary>
    public static void HideOverlay() => GetMainWindow()?.HideVideoOverlay();

    /// <summary>Show the video overlay (call after a fire-and-forget dialog is hidden).</summary>
    public static void ShowOverlay() => GetMainWindow()?.ShowVideoOverlay();

    private static MainWindow? GetMainWindow()
    {
        foreach (var window in WindowHelper.ActiveWindows)
        {
            if (window is MainWindow mw)
                return mw;
        }
        return null;
    }
}
