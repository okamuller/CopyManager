namespace CopyManager.Models;

public class AppSettings
{
    public string FastCopyExePath { get; set; } = string.Empty;
    public int DefaultBufferSizeMb { get; set; } = 256;
    public string DefaultSpeed { get; set; } = "full";
    public int MaxConcurrentJobs { get; set; } = 1;
    public int MaxRetryCount { get; set; } = 3;
    public int RetryIntervalSeconds { get; set; } = 30;
    public string LogDirectory { get; set; } = string.Empty;
    public bool EnableToastNotification { get; set; } = true;

    /// <summary>
    /// Returns the default log directory: %APPDATA%\CopyManager\logs
    /// </summary>
    public static string GetDefaultLogDirectory()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "CopyManager", "logs");
    }

    /// <summary>
    /// Returns the app data root: %APPDATA%\CopyManager
    /// </summary>
    public static string GetAppDataDirectory()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "CopyManager");
    }
}
