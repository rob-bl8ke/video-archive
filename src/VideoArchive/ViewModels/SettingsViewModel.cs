using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VideoArchive.Data;
using VideoArchive.Models;
using VideoArchive.Services;

namespace VideoArchive.ViewModels;

#pragma warning disable MVVMTK0045
public partial class SettingsViewModel : ObservableObject
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ISettingsService _settings;

    public SettingsViewModel(IServiceScopeFactory scopeFactory, ISettingsService settings)
    {
        _scopeFactory = scopeFactory;
        _settings = settings;
    }

    [ObservableProperty]
    private ObservableCollection<LibraryFolder> _folders = [];

    [ObservableProperty]
    private string _statusText = string.Empty;

    [RelayCommand]
    private async Task LoadFoldersAsync()
    {
        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<VideoArchiveContext>();
        var folders = await context.LibraryFolders.OrderBy(f => f.Path).ToListAsync();
        Folders = new ObservableCollection<LibraryFolder>(folders);
    }

    [RelayCommand]
    private async Task RemoveFolderAsync(LibraryFolder? folder)
    {
        if (folder is null) return;

        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<VideoArchiveContext>();
        var entity = await context.LibraryFolders.FindAsync(folder.Id);
        if (entity is not null)
        {
            context.LibraryFolders.Remove(entity);
            await context.SaveChangesAsync();
        }
        Folders.Remove(folder);
        StatusText = $"Removed: {folder.Path}";
    }

    public ISettingsService Settings => _settings;
}
#pragma warning restore MVVMTK0045
