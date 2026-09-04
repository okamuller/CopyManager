using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using CopyManager.Models;
using Microsoft.Extensions.Logging;

namespace CopyManager.Services;

public interface IJobQueueService
{
    ObservableCollection<CopyJob> Jobs { get; }
    event Action<CopyJob>? JobStatusChanged;
    event Action? AllJobsCompleted;
    void AddJob(CopyJob job);
    void RemoveJob(Guid jobId);
    void RetryJob(Guid jobId);
    void CancelJob(Guid jobId);
    void PauseJob(Guid jobId);
    void ResumeJob(Guid jobId);
    void StartAll();
    void PauseAll();
    Task ProcessQueueAsync();
    Task LoadJobsAsync();
    Task SaveJobsAsync();
    Task CalculateTotalSizeAsync(CopyJob job, CancellationToken cancellationToken = default);
}

public class JobQueueService : IJobQueueService
{
    private readonly IFastCopyService _fastCopyService;
    private readonly ISettingsService _settingsService;
    private readonly ILogger<JobQueueService> _logger;
    private readonly string _jobsFilePath;
    private readonly SemaphoreSlim _queueLock = new(1, 1);
    private readonly Dictionary<Guid, CancellationTokenSource> _runningJobs = new();
    private int _nextQueueOrder;
    private bool _isStarted;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public ObservableCollection<CopyJob> Jobs { get; } = new();
    public event Action<CopyJob>? JobStatusChanged;
    public event Action? AllJobsCompleted;

    public JobQueueService(
        IFastCopyService fastCopyService,
        ISettingsService settingsService,
        ILogger<JobQueueService> logger)
    {
        _fastCopyService = fastCopyService;
        _settingsService = settingsService;
        _logger = logger;
        _jobsFilePath = Path.Combine(AppSettings.GetAppDataDirectory(), "jobs.json");
    }

    public void AddJob(CopyJob job)
    {
        job.QueueOrder = _nextQueueOrder++;
        job.MaxRetryCount = _settingsService.Settings.MaxRetryCount;
        job.Name = CopyJob.DeriveName(job.SourcePaths);

        // Set log file path
        job.LogFilePath = Path.Combine(
            _settingsService.Settings.LogDirectory, "jobs", $"{job.Id}.log");

        InsertByPriority(job);

        _logger.LogInformation("Job added: {Name} (ID: {Id}, Priority: {Priority})", job.Name, job.Id, job.Priority);

        _ = Task.Run(async () =>
        {
            await CalculateTotalSizeAsync(job);
            await ProcessQueueAsync();
        });
    }

    public void RemoveJob(Guid jobId)
    {
        var job = Jobs.FirstOrDefault(j => j.Id == jobId);
        if (job is null) return;

        if (job.Status == JobStatus.Running)
        {
            CancelJob(jobId);
        }

        Jobs.Remove(job);
        _logger.LogInformation("Job removed: {Name} (ID: {Id})", job.Name, job.Id);
        _ = SaveJobsAsync();
    }

    public void RetryJob(Guid jobId)
    {
        var job = Jobs.FirstOrDefault(j => j.Id == jobId);
        if (job is null || job.Status != JobStatus.Failed) return;

        job.Status = JobStatus.Pending;
        job.RetryCount = 0;
        job.LastErrorMessage = null;
        job.TransferredBytes = 0;
        job.QueueOrder = _nextQueueOrder++;
        ResortJobs();
        JobStatusChanged?.Invoke(job);
        _logger.LogInformation("Job manually retried: {Name} (ID: {Id})", job.Name, job.Id);

        _ = ProcessQueueAsync();
    }

    public void CancelJob(Guid jobId)
    {
        var job = Jobs.FirstOrDefault(j => j.Id == jobId);
        if (job is null) return;

        if (_runningJobs.TryGetValue(jobId, out var cts))
        {
            cts.Cancel();
            _runningJobs.Remove(jobId);
        }

        job.Status = JobStatus.Cancelled;
        JobStatusChanged?.Invoke(job);
        _logger.LogInformation("Job cancelled: {Name} (ID: {Id})", job.Name, job.Id);
        _ = SaveJobsAsync();
    }

    public void PauseJob(Guid jobId)
    {
        var job = Jobs.FirstOrDefault(j => j.Id == jobId);
        if (job is null || job.Status != JobStatus.Running) return;

        if (_runningJobs.TryGetValue(jobId, out var cts))
        {
            cts.Cancel();
            _runningJobs.Remove(jobId);
        }

        job.Status = JobStatus.Paused;
        JobStatusChanged?.Invoke(job);
        _logger.LogInformation("Job paused: {Name} (ID: {Id})", job.Name, job.Id);
        _ = SaveJobsAsync();
    }

    public void ResumeJob(Guid jobId)
    {
        var job = Jobs.FirstOrDefault(j => j.Id == jobId);
        if (job is null || job.Status != JobStatus.Paused) return;

        job.Status = JobStatus.Pending;
        JobStatusChanged?.Invoke(job);
        _logger.LogInformation("Job resumed: {Name} (ID: {Id})", job.Name, job.Id);

        _ = ProcessQueueAsync();
    }

    public void StartAll()
    {
        _isStarted = true;
        _logger.LogInformation("Start All requested");
        _ = ProcessQueueAsync();
    }

    public void PauseAll()
    {
        _isStarted = false;
        _logger.LogInformation("Pause All requested");

        foreach (var jobId in _runningJobs.Keys.ToList())
        {
            PauseJob(jobId);
        }
    }

    public async Task ProcessQueueAsync()
    {
        if (!_isStarted) return;

        await _queueLock.WaitAsync();
        try
        {
            var maxConcurrent = _settingsService.Settings.MaxConcurrentJobs;
            var runningCount = Jobs.Count(j => j.Status == JobStatus.Running);

            while (runningCount < maxConcurrent)
            {
                var next = Jobs.FirstOrDefault(j => j.Status == JobStatus.Pending);
                if (next is null) break;

                runningCount++;
                _ = ExecuteJobAsync(next);
            }

            // Check if all jobs are done
            if (Jobs.All(j => j.Status is JobStatus.Completed or JobStatus.Failed or JobStatus.Cancelled)
                && Jobs.Count > 0)
            {
                AllJobsCompleted?.Invoke();
            }
        }
        finally
        {
            _queueLock.Release();
        }
    }

    private async Task ExecuteJobAsync(CopyJob job)
    {
        var cts = new CancellationTokenSource();
        _runningJobs[job.Id] = cts;

        job.Status = JobStatus.Running;
        job.StartedAt = DateTime.Now;
        JobStatusChanged?.Invoke(job);
        await SaveJobsAsync();

        _logger.LogInformation("Job started: {Name} (ID: {Id})", job.Name, job.Id);

        var progress = new Progress<FastCopyProgress>(p =>
        {
            job.TransferredBytes = p.TransferredBytes;
            if (p.TotalBytes > 0)
                job.TotalSizeBytes = p.TotalBytes;
            JobStatusChanged?.Invoke(job);
        });

        try
        {
            var exitCode = await _fastCopyService.RunAsync(
                job, _settingsService.Settings, progress, cts.Token);

            if (exitCode == 0)
            {
                job.Status = JobStatus.Completed;
                job.CompletedAt = DateTime.Now;
                _logger.LogInformation("Job completed: {Name} (ID: {Id})", job.Name, job.Id);
            }
            else
            {
                job.LastErrorMessage = $"FastCopy exited with code {exitCode}";
                await HandleJobFailureAsync(job);
            }
        }
        catch (OperationCanceledException)
        {
            // Job was cancelled or paused — status already set
            _logger.LogInformation("Job execution cancelled: {Name} (ID: {Id})", job.Name, job.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Job execution failed: {Name} (ID: {Id})", job.Name, job.Id);
            job.LastErrorMessage = ex.Message;
            await HandleJobFailureAsync(job);
        }
        finally
        {
            _runningJobs.Remove(job.Id);
            JobStatusChanged?.Invoke(job);
            await SaveJobsAsync();
            await ProcessQueueAsync();
        }
    }

    private async Task HandleJobFailureAsync(CopyJob job)
    {
        job.RetryCount++;

        if (job.RetryCount <= job.MaxRetryCount)
        {
            _logger.LogWarning(
                "Job failed, retrying ({RetryCount}/{MaxRetry}) after {Interval}s: {Name}",
                job.RetryCount, job.MaxRetryCount,
                _settingsService.Settings.RetryIntervalSeconds, job.Name);

            job.Status = JobStatus.Pending;
            job.QueueOrder = _nextQueueOrder++;
            ResortJobs();
            JobStatusChanged?.Invoke(job);

            // Wait for retry interval then process queue
            await Task.Delay(TimeSpan.FromSeconds(_settingsService.Settings.RetryIntervalSeconds));
            await ProcessQueueAsync();
        }
        else
        {
            job.Status = JobStatus.Failed;
            _logger.LogError(
                "Job failed permanently after {RetryCount} retries: {Name} (ID: {Id})",
                job.RetryCount, job.Name, job.Id);
        }
    }

    public async Task LoadJobsAsync()
    {
        try
        {
            if (!File.Exists(_jobsFilePath))
            {
                _logger.LogInformation("No jobs file found at {Path}", _jobsFilePath);
                return;
            }

            var json = await File.ReadAllTextAsync(_jobsFilePath);
            var jobs = JsonSerializer.Deserialize<List<CopyJob>>(json, JsonOptions);

            if (jobs is null) return;

            Jobs.Clear();
            foreach (var job in jobs)
            {
                // Reset unfinished jobs to Pending on startup (spec §4 persistence)
                if (job.Status is JobStatus.Running or JobStatus.Paused)
                {
                    job.Status = JobStatus.Pending;
                    job.TransferredBytes = 0;
                }

                Jobs.Add(job);
                _nextQueueOrder = Math.Max(_nextQueueOrder, job.QueueOrder + 1);
            }

            _logger.LogInformation("Loaded {Count} jobs from {Path}", jobs.Count, _jobsFilePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load jobs from {Path}", _jobsFilePath);
        }
    }

    public async Task SaveJobsAsync()
    {
        try
        {
            var directory = Path.GetDirectoryName(_jobsFilePath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            var json = JsonSerializer.Serialize(Jobs.ToList(), JsonOptions);
            await File.WriteAllTextAsync(_jobsFilePath, json);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save jobs to {Path}", _jobsFilePath);
        }
    }

    public async Task CalculateTotalSizeAsync(CopyJob job, CancellationToken cancellationToken = default)
    {
        try
        {
            long totalSize = 0;

            foreach (var sourcePath in job.SourcePaths)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (File.Exists(sourcePath))
                {
                    totalSize += new FileInfo(sourcePath).Length;
                }
                else if (Directory.Exists(sourcePath))
                {
                    foreach (var file in Directory.EnumerateFiles(sourcePath, "*", SearchOption.AllDirectories))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        try
                        {
                            totalSize += new FileInfo(file).Length;
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Failed to get size of {File}", file);
                        }
                    }
                }
            }

            job.TotalSizeBytes = totalSize;
            JobStatusChanged?.Invoke(job);
            _logger.LogInformation("Total size calculated for {Name}: {Size} bytes", job.Name, totalSize);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Size calculation cancelled for {Name}", job.Name);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to calculate total size for {Name}", job.Name);
        }
    }

    private void InsertByPriority(CopyJob job)
    {
        // Find correct position: after all jobs of same or higher priority
        var index = Jobs.Count;
        for (var i = 0; i < Jobs.Count; i++)
        {
            if (Jobs[i].Priority > job.Priority && Jobs[i].Status == JobStatus.Pending)
            {
                index = i;
                break;
            }
        }
        Jobs.Insert(index, job);
    }

    private void ResortJobs()
    {
        var sorted = Jobs
            .OrderBy(j => j.Status switch
            {
                JobStatus.Running => 0,
                JobStatus.Pending => 1,
                _ => 2
            })
            .ThenBy(j => j.Priority)
            .ThenBy(j => j.QueueOrder)
            .ToList();

        for (var i = 0; i < sorted.Count; i++)
        {
            var currentIndex = Jobs.IndexOf(sorted[i]);
            if (currentIndex != i)
                Jobs.Move(currentIndex, i);
        }
    }
}
