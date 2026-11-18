using System;
using System.IO;
using System.Text.Json;

namespace NeuralTask.App.Services;

public class JsonSettingsService : ISettingsService
{
    private readonly string _filePath;
    private AppSettings _currentSettings;
    private readonly object _lock = new();

    public AppSettings CurrentSettings
    {
        get
        {
            lock (_lock)
            {
                return _currentSettings;
            }
        }
    }

    public event EventHandler<AppSettings>? SettingsChanged;

    public JsonSettingsService(string? customPath = null)
    {
        if (customPath != null)
        {
            _filePath = customPath;
        }
        else
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var dir = Path.Combine(appData, "NeuralTask");
            Directory.CreateDirectory(dir);
            _filePath = Path.Combine(dir, "settings.json");
        }

        _currentSettings = LoadSettings();
    }

    private AppSettings LoadSettings()
    {
        try
        {
            if (File.Exists(_filePath))
            {
                var json = File.ReadAllText(_filePath);
                var loaded = JsonSerializer.Deserialize<AppSettings>(json);
                if (loaded != null)
                {
                    return loaded;
                }
            }
        }
        catch
        {
            // Corrupt or inaccessible file, return default
        }

        return new AppSettings();
    }

    public void SaveSettings(AppSettings settings)
    {
        lock (_lock)
        {
            _currentSettings = settings;
            try
            {
                var dir = Path.GetDirectoryName(_filePath);
                if (!string.IsNullOrEmpty(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_filePath, json);
            }
            catch
            {
                // File write failures must not crash the application
            }
        }

        SettingsChanged?.Invoke(this, settings);
    }
}
