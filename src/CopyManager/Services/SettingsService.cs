using System.IO;
using System.Text.Json;
using CopyManager.Models;
using Microsoft.Extensions.Logging;

namespace CopyManager.Services;

public interface ISettingsService
{
    AppSettings Settings { get; }
    Task LoadAsync();
    Task SaveAsync();
}

public class SettingsService : ISettingsService
{
    private readonly ILogger<SettingsService> _logger;
    private readonly string _settingsFilePath;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public AppSettings Settings { get; private set; } = new();

    public SettingsService(ILogger<SettingsService> logger)
    {
        _logger = logger;
        _settingsFilePath = Path.Combine(AppSettings.GetAppDataDirectory(), "settings.json");
    }

    public async Task LoadAsync()
    {
        try
        {
            if (File.Exists(_settingsFilePath))
            {
                var json = await File.ReadAllTextAsync(_settingsFilePath);
                Settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
                _logger.LogInformation("Settings loaded from {Path}", _settingsFilePath);
            }
            else
            {
                Settings = new AppSettings();
                _logger.LogInformation("No settings file found, using defaults");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load settings from {Path}", _settingsFilePath);
            Settings = new AppSettings();
        }

        // Apply defaults for empty fields
        if (string.IsNullOrEmpty(Settings.LogDirectory))
            Settings.LogDirectory = AppSettings.GetDefaultLogDirectory();

        if (string.IsNullOrEmpty(Settings.FastCopyExePath))
            Settings.FastCopyExePath = AutoDetectFastCopy();

        // Ensure directories exist
        EnsureDirectories();
    }

    public async Task SaveAsync()
    {
        try
        {
            var directory = Path.GetDirectoryName(_settingsFilePath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            var json = JsonSerializer.Serialize(Settings, JsonOptions);
            await File.WriteAllTextAsync(_settingsFilePath, json);
            _logger.LogInformation("Settings saved to {Path}", _settingsFilePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save settings to {Path}", _settingsFilePath);
        }
    }

    private void EnsureDirectories()
    {
        try
        {
            Directory.CreateDirectory(AppSettings.GetAppDataDirectory());
            Directory.CreateDirectory(Settings.LogDirectory);
            Directory.CreateDirectory(Path.Combine(Settings.LogDirectory, "jobs"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create application directories");
        }
    }

    private string AutoDetectFastCopy()
    {
        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "FastCopy", "FastCopy.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "FastCopy", "FastCopy.exe"),
        };

        foreach (var path in candidates)
        {
            if (File.Exists(path))
            {
                _logger.LogInformation("FastCopy auto-detected at {Path}", path);
                return path;
            }
        }

        // Check PATH
        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var dir in pathEnv.Split(Path.PathSeparator))
        {
            var candidate = Path.Combine(dir, "FastCopy.exe");
            if (File.Exists(candidate))
            {
                _logger.LogInformation("FastCopy found in PATH at {Path}", candidate);
                return candidate;
            }
        }

        _logger.LogWarning("FastCopy.exe not found. Please configure the path in Settings.");
        return string.Empty;
    }
}
