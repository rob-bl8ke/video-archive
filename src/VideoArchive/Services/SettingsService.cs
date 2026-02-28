using System.Text.Json;

namespace VideoArchive.Services;

public class SettingsService : ISettingsService
{
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "VideoArchive",
        "settings.json");

    private readonly Dictionary<string, string> _values = new();

    public SettingsService()
    {
        Load();
    }

    public string ViewMode
    {
        get => Get(nameof(ViewMode), "Gallery") ?? "Gallery";
        set => Set(nameof(ViewMode), value);
    }

    public string SortColumn
    {
        get => Get(nameof(SortColumn), "Title") ?? "Title";
        set => Set(nameof(SortColumn), value);
    }

    public string SortDirection
    {
        get => Get(nameof(SortDirection), "Ascending") ?? "Ascending";
        set => Set(nameof(SortDirection), value);
    }

    public string? LastFilterJson
    {
        get => Get(nameof(LastFilterJson), null);
        set => Set(nameof(LastFilterJson), value ?? string.Empty);
    }

    public double WindowWidth
    {
        get => double.TryParse(Get(nameof(WindowWidth), "1280"), out var v) ? v : 1280;
        set => Set(nameof(WindowWidth), value.ToString());
    }

    public double WindowHeight
    {
        get => double.TryParse(Get(nameof(WindowHeight), "800"), out var v) ? v : 800;
        set => Set(nameof(WindowHeight), value.ToString());
    }

    public double WindowLeft
    {
        get => double.TryParse(Get(nameof(WindowLeft), "0"), out var v) ? v : 0;
        set => Set(nameof(WindowLeft), value.ToString());
    }

    public double WindowTop
    {
        get => double.TryParse(Get(nameof(WindowTop), "0"), out var v) ? v : 0;
        set => Set(nameof(WindowTop), value.ToString());
    }

    private string? Get(string key, string? defaultValue)
        => _values.TryGetValue(key, out var value) ? value : defaultValue;

    private void Set(string key, string value)
    {
        _values[key] = value;
        Save();
    }

    private void Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                if (dict is not null)
                {
                    foreach (var kv in dict)
                        _values[kv.Key] = kv.Value;
                }
            }
        }
        catch
        {
            // Corrupted settings file — start fresh
        }
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            var json = JsonSerializer.Serialize(_values, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(SettingsPath, json);
        }
        catch
        {
            // Best-effort persistence
        }
    }
}
