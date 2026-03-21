using System.Windows;
using CopyManager.Services;
using CopyManager.ViewModels;
using Microsoft.Extensions.Logging;

namespace CopyManager;

public partial class App : Application
{
    private ISettingsService? _settingsService;
    private IJobQueueService? _jobQueueService;
    private MainViewModel? _mainViewModel;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Set up logging
        using var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Information);
        });

        // Create services
        var settingsLogger = loggerFactory.CreateLogger<SettingsService>();
        _settingsService = new SettingsService(settingsLogger);

        var fastCopyLogger = loggerFactory.CreateLogger<FastCopyService>();
        var fastCopyService = new FastCopyService(fastCopyLogger);

        var queueLogger = loggerFactory.CreateLogger<JobQueueService>();
        _jobQueueService = new JobQueueService(fastCopyService, _settingsService, queueLogger);

        var mainLogger = loggerFactory.CreateLogger<MainViewModel>();
        _mainViewModel = new MainViewModel(_jobQueueService, _settingsService, mainLogger);

        // Initialize
        await _mainViewModel.InitializeAsync();

        // Set up main window
        var mainWindow = new MainWindow
        {
            DataContext = _mainViewModel
        };
        mainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // Save jobs on exit
        _jobQueueService?.SaveJobsAsync().GetAwaiter().GetResult();
        base.OnExit(e);
    }
}
