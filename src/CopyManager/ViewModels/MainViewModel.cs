using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CopyManager.Models;
using CopyManager.Services;
using Microsoft.Extensions.Logging;

namespace CopyManager.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly IJobQueueService _jobQueueService;
    private readonly ISettingsService _settingsService;
    private readonly ILogger<MainViewModel> _logger;

    public ObservableCollection<JobViewModel> Jobs { get; } = new();

    [ObservableProperty]
    private JobViewModel? _selectedJob;

    [ObservableProperty]
    private double _currentSpeed;

    [ObservableProperty]
    private int _runningCount;

    [ObservableProperty]
    private int _pendingCount;

    [ObservableProperty]
    private int _completedCount;

    [ObservableProperty]
    private int _failedCount;

    [ObservableProperty]
    private string _statusMessage = "Ready";

    public MainViewModel(
        IJobQueueService jobQueueService,
        ISettingsService settingsService,
        ILogger<MainViewModel> logger)
    {
        _jobQueueService = jobQueueService;
        _settingsService = settingsService;
        _logger = logger;

        _jobQueueService.JobStatusChanged += OnJobStatusChanged;
        _jobQueueService.AllJobsCompleted += OnAllJobsCompleted;
    }

    public async Task InitializeAsync()
    {
        await _settingsService.LoadAsync();
        await _jobQueueService.LoadJobsAsync();

        // Sync loaded jobs to ViewModels
        foreach (var job in _jobQueueService.Jobs)
        {
            Jobs.Add(new JobViewModel(job, HandleJobAction));
        }

        UpdateCounts();
    }

    [RelayCommand]
    private void AddJob()
    {
        // This will be called from the view to open the dialog
        // The view's code-behind handles the dialog and calls AddJobFromDialog
    }

    public void AddJobFromDialog(CopyJob job)
    {
        _jobQueueService.AddJob(job);
        var vm = new JobViewModel(job, HandleJobAction) { IsCalculatingSize = true };
        Jobs.Add(vm);
        UpdateCounts();

        // Size calculation happens in the service; we just need to refresh when done
        _ = Task.Run(async () =>
        {
            // Wait a bit for size calculation to complete
            await Task.Delay(100);
            Application.Current?.Dispatcher.Invoke(() =>
            {
                vm.TotalSizeBytes = job.TotalSizeBytes;
                vm.IsCalculatingSize = false;
            });
        });
    }

    [RelayCommand]
    private void StartAll()
    {
        _jobQueueService.StartAll();
        StatusMessage = "Queue started";
        UpdateCounts();
    }

    [RelayCommand]
    private void PauseAll()
    {
        _jobQueueService.PauseAll();
        StatusMessage = "All jobs paused";
        UpdateCounts();
    }

    [RelayCommand]
    private void OpenSettings()
    {
        // View handles opening the dialog
    }

    private void HandleJobAction(JobViewModel jobVm, string action)
    {
        switch (action)
        {
            case "Cancel":
                _jobQueueService.CancelJob(jobVm.Id);
                jobVm.Refresh();
                break;
            case "Retry":
                _jobQueueService.RetryJob(jobVm.Id);
                jobVm.Refresh();
                break;
            case "Pause":
                _jobQueueService.PauseJob(jobVm.Id);
                jobVm.Refresh();
                break;
            case "Resume":
                _jobQueueService.ResumeJob(jobVm.Id);
                jobVm.Refresh();
                break;
            case "Delete":
                _jobQueueService.RemoveJob(jobVm.Id);
                Jobs.Remove(jobVm);
                break;
            case "ViewLog":
                if (!string.IsNullOrEmpty(jobVm.LogFilePath) && File.Exists(jobVm.LogFilePath))
                {
                    try
                    {
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = jobVm.LogFilePath,
                            UseShellExecute = true
                        });
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to open log file");
                    }
                }
                break;
            case "PriorityChanged":
                // Re-sort handled by the queue service
                break;
        }

        UpdateCounts();
    }

    private void OnJobStatusChanged(CopyJob job)
    {
        Application.Current?.Dispatcher.Invoke(() =>
        {
            var vm = Jobs.FirstOrDefault(j => j.Id == job.Id);
            if (vm is null) return;

            vm.Refresh();

            if (job.Status == JobStatus.Running)
            {
                CurrentSpeed = job.TransferredBytes > 0
                    ? job.TransferredBytes / 1024.0 / 1024.0 // Simplified; real speed comes from progress
                    : 0;
            }

            UpdateCounts();
        });
    }

    private void OnAllJobsCompleted()
    {
        Application.Current?.Dispatcher.Invoke(() =>
        {
            StatusMessage = "All jobs completed";
            UpdateCounts();
        });
    }

    private void UpdateCounts()
    {
        RunningCount = Jobs.Count(j => j.Status == JobStatus.Running);
        PendingCount = Jobs.Count(j => j.Status == JobStatus.Pending);
        CompletedCount = Jobs.Count(j => j.Status == JobStatus.Completed);
        FailedCount = Jobs.Count(j => j.Status == JobStatus.Failed);
    }
}
