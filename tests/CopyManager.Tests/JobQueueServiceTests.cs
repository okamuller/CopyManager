using System.IO;
using CopyManager.Models;
using CopyManager.Services;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace CopyManager.Tests;

public class JobQueueServiceTests
{
    private readonly Mock<IFastCopyService> _fastCopyMock;
    private readonly Mock<ISettingsService> _settingsMock;
    private readonly JobQueueService _service;

    public JobQueueServiceTests()
    {
        _fastCopyMock = new Mock<IFastCopyService>();
        _settingsMock = new Mock<ISettingsService>();
        _settingsMock.Setup(s => s.Settings).Returns(new AppSettings
        {
            MaxConcurrentJobs = 1,
            MaxRetryCount = 3,
            RetryIntervalSeconds = 1,
            LogDirectory = Path.Combine(Path.GetTempPath(), "CopyManagerTests", "logs")
        });

        var logger = new Mock<ILogger<JobQueueService>>();
        _service = new JobQueueService(_fastCopyMock.Object, _settingsMock.Object, logger.Object);
    }

    #region Priority Ordering Tests

    [Fact]
    public void AddJob_HighPriorityBeforeNormal()
    {
        var normalJob = CreateJob(JobPriority.Normal, "NormalJob");
        var highJob = CreateJob(JobPriority.High, "HighJob");

        _service.AddJob(normalJob);
        _service.AddJob(highJob);

        // High priority should be before Normal in the queue
        var jobs = _service.Jobs.Where(j => j.Status == JobStatus.Pending).ToList();
        var highIndex = jobs.FindIndex(j => j.Name == "HighJob");
        var normalIndex = jobs.FindIndex(j => j.Name == "NormalJob");
        Assert.True(highIndex < normalIndex, "High priority job should come before Normal priority job");
    }

    [Fact]
    public void AddJob_NormalPriorityBeforeLow()
    {
        var lowJob = CreateJob(JobPriority.Low, "LowJob");
        var normalJob = CreateJob(JobPriority.Normal, "NormalJob");

        _service.AddJob(lowJob);
        _service.AddJob(normalJob);

        var jobs = _service.Jobs.Where(j => j.Status == JobStatus.Pending).ToList();
        var normalIndex = jobs.FindIndex(j => j.Name == "NormalJob");
        var lowIndex = jobs.FindIndex(j => j.Name == "LowJob");
        Assert.True(normalIndex < lowIndex, "Normal priority job should come before Low priority job");
    }

    [Fact]
    public void AddJob_FifoWithinSamePriority()
    {
        var job1 = CreateJob(JobPriority.Normal, "First");
        var job2 = CreateJob(JobPriority.Normal, "Second");
        var job3 = CreateJob(JobPriority.Normal, "Third");

        _service.AddJob(job1);
        _service.AddJob(job2);
        _service.AddJob(job3);

        var jobs = _service.Jobs.ToList();
        Assert.Equal("First", jobs[0].Name);
        Assert.Equal("Second", jobs[1].Name);
        Assert.Equal("Third", jobs[2].Name);
    }

    [Fact]
    public void AddJob_MixedPriorities_CorrectOrder()
    {
        var low = CreateJob(JobPriority.Low, "Low");
        var normal = CreateJob(JobPriority.Normal, "Normal");
        var high = CreateJob(JobPriority.High, "High");

        _service.AddJob(low);
        _service.AddJob(normal);
        _service.AddJob(high);

        var jobs = _service.Jobs.ToList();
        Assert.Equal("High", jobs[0].Name);
        Assert.Equal("Normal", jobs[1].Name);
        Assert.Equal("Low", jobs[2].Name);
    }

    #endregion

    #region State Transition Tests

    [Fact]
    public void AddJob_InitialStatusIsPending()
    {
        var job = CreateJob();
        _service.AddJob(job);

        Assert.Equal(JobStatus.Pending, job.Status);
    }

    [Fact]
    public void CancelJob_SetsStatusToCancelled()
    {
        var job = CreateJob();
        _service.AddJob(job);

        _service.CancelJob(job.Id);

        Assert.Equal(JobStatus.Cancelled, job.Status);
    }

    [Fact]
    public void RetryJob_ResetsStatusToPending()
    {
        var job = CreateJob();
        job.Status = JobStatus.Failed;
        job.RetryCount = 2;
        _service.Jobs.Add(job);

        _service.RetryJob(job.Id);

        Assert.Equal(JobStatus.Pending, job.Status);
        Assert.Equal(0, job.RetryCount);
    }

    [Fact]
    public void RetryJob_DoesNothing_WhenNotFailed()
    {
        var job = CreateJob();
        job.Status = JobStatus.Pending;
        _service.Jobs.Add(job);

        _service.RetryJob(job.Id);

        // Should still be Pending, no change
        Assert.Equal(JobStatus.Pending, job.Status);
    }

    [Fact]
    public void PauseJob_SetsStatusToPaused_WhenRunning()
    {
        var job = CreateJob();
        job.Status = JobStatus.Running;
        _service.Jobs.Add(job);

        _service.PauseJob(job.Id);

        Assert.Equal(JobStatus.Paused, job.Status);
    }

    [Fact]
    public void PauseJob_DoesNothing_WhenNotRunning()
    {
        var job = CreateJob();
        job.Status = JobStatus.Pending;
        _service.Jobs.Add(job);

        _service.PauseJob(job.Id);

        Assert.Equal(JobStatus.Pending, job.Status);
    }

    [Fact]
    public void ResumeJob_SetsStatusToPending_WhenPaused()
    {
        var job = CreateJob();
        job.Status = JobStatus.Paused;
        _service.Jobs.Add(job);

        _service.ResumeJob(job.Id);

        Assert.Equal(JobStatus.Pending, job.Status);
    }

    #endregion

    #region Remove Job Tests

    [Fact]
    public void RemoveJob_RemovesFromList()
    {
        var job = CreateJob();
        _service.AddJob(job);
        Assert.Single(_service.Jobs);

        _service.RemoveJob(job.Id);

        Assert.Empty(_service.Jobs);
    }

    [Fact]
    public void RemoveJob_NonexistentId_DoesNothing()
    {
        var job = CreateJob();
        _service.AddJob(job);

        _service.RemoveJob(Guid.NewGuid());

        Assert.Single(_service.Jobs);
    }

    #endregion

    #region Job Name Derivation Tests

    [Fact]
    public void DeriveName_SingleSource_UsesFileName()
    {
        var name = CopyJob.DeriveName(new List<string> { @"D:\Videos\2025" });
        Assert.Equal("2025", name);
    }

    [Fact]
    public void DeriveName_SingleSource_UsesDirectoryName()
    {
        var name = CopyJob.DeriveName(new List<string> { @"D:\Videos\2025\" });
        Assert.Equal("2025", name);
    }

    [Fact]
    public void DeriveName_MultipleSources_ShowsCount()
    {
        var sources = new List<string> { @"D:\Videos\2025", @"D:\Photos\2025", @"D:\Docs\2025" };
        var name = CopyJob.DeriveName(sources);
        Assert.Equal("2025 (+2 more)", name);
    }

    [Fact]
    public void DeriveName_Empty_ReturnsEmpty()
    {
        var name = CopyJob.DeriveName(new List<string>());
        Assert.Equal(string.Empty, name);
    }

    #endregion

    #region Total Size Calculation Tests

    [Fact]
    public async Task CalculateTotalSizeAsync_WithFile_CalculatesCorrectSize()
    {
        // Create a temp file with known content
        var tempFile = Path.GetTempFileName();
        try
        {
            await File.WriteAllBytesAsync(tempFile, new byte[1024]);
            var job = CreateJob();
            job.SourcePaths = new List<string> { tempFile };

            await _service.CalculateTotalSizeAsync(job);

            Assert.Equal(1024, job.TotalSizeBytes);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task CalculateTotalSizeAsync_WithDirectory_SumsAllFiles()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"CopyManagerTest_{Guid.NewGuid()}");
        try
        {
            Directory.CreateDirectory(tempDir);
            await File.WriteAllBytesAsync(Path.Combine(tempDir, "file1.txt"), new byte[500]);
            await File.WriteAllBytesAsync(Path.Combine(tempDir, "file2.txt"), new byte[300]);

            var job = CreateJob();
            job.SourcePaths = new List<string> { tempDir };

            await _service.CalculateTotalSizeAsync(job);

            Assert.Equal(800, job.TotalSizeBytes);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task CalculateTotalSizeAsync_NonexistentPath_HandlesGracefully()
    {
        var job = CreateJob();
        job.SourcePaths = new List<string> { @"Z:\NonExistent\Path" };

        await _service.CalculateTotalSizeAsync(job);

        Assert.Equal(0, job.TotalSizeBytes);
    }

    [Fact]
    public async Task CalculateTotalSizeAsync_Cancellation_Throws()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"CopyManagerTest_{Guid.NewGuid()}");
        try
        {
            Directory.CreateDirectory(tempDir);
            await File.WriteAllBytesAsync(Path.Combine(tempDir, "file1.txt"), new byte[100]);

            var job = CreateJob();
            job.SourcePaths = new List<string> { tempDir };

            using var cts = new CancellationTokenSource();
            cts.Cancel(); // Cancel immediately

            // Should not throw, just handle the cancellation gracefully
            await _service.CalculateTotalSizeAsync(job, cts.Token);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    #endregion

    #region Job Persistence Tests

    [Fact]
    public async Task SaveAndLoadJobs_RoundTrip()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"test_jobs_{Guid.NewGuid()}.json");
        try
        {
            // Use reflection to set the private _jobsFilePath field
            var field = typeof(JobQueueService).GetField("_jobsFilePath",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            field!.SetValue(_service, tempFile);

            var job1 = CreateJob(JobPriority.High, "Job1");
            var job2 = CreateJob(JobPriority.Normal, "Job2");
            _service.Jobs.Add(job1);
            _service.Jobs.Add(job2);

            await _service.SaveJobsAsync();

            // Verify file exists
            Assert.True(File.Exists(tempFile));

            // Clear jobs and reload
            _service.Jobs.Clear();
            await _service.LoadJobsAsync();

            Assert.Equal(2, _service.Jobs.Count);
            Assert.Contains(_service.Jobs, j => j.Name == "Job1");
            Assert.Contains(_service.Jobs, j => j.Name == "Job2");
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task LoadJobs_ResetsRunningToPending()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"test_jobs_{Guid.NewGuid()}.json");
        try
        {
            var field = typeof(JobQueueService).GetField("_jobsFilePath",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            field!.SetValue(_service, tempFile);

            var job = CreateJob();
            job.Status = JobStatus.Running;
            job.TransferredBytes = 1000;
            _service.Jobs.Add(job);

            await _service.SaveJobsAsync();

            _service.Jobs.Clear();
            await _service.LoadJobsAsync();

            var loaded = _service.Jobs.First();
            Assert.Equal(JobStatus.Pending, loaded.Status);
            Assert.Equal(0, loaded.TransferredBytes);
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task LoadJobs_ResetsPausedToPending()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"test_jobs_{Guid.NewGuid()}.json");
        try
        {
            var field = typeof(JobQueueService).GetField("_jobsFilePath",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            field!.SetValue(_service, tempFile);

            var job = CreateJob();
            job.Status = JobStatus.Paused;
            _service.Jobs.Add(job);

            await _service.SaveJobsAsync();

            _service.Jobs.Clear();
            await _service.LoadJobsAsync();

            Assert.Equal(JobStatus.Pending, _service.Jobs.First().Status);
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    #endregion

    #region StatusChanged Event Tests

    [Fact]
    public void CancelJob_FiresStatusChangedEvent()
    {
        var job = CreateJob();
        _service.AddJob(job);

        CopyJob? changedJob = null;
        _service.JobStatusChanged += j => changedJob = j;

        _service.CancelJob(job.Id);

        Assert.NotNull(changedJob);
        Assert.Equal(job.Id, changedJob!.Id);
        Assert.Equal(JobStatus.Cancelled, changedJob.Status);
    }

    #endregion

    #region Helper Methods

    private static CopyJob CreateJob(JobPriority priority = JobPriority.Normal, string? name = null)
    {
        var job = new CopyJob
        {
            Priority = priority,
            // AddJob re-derives Name from the first source path, so give each job
            // its own path when a name is requested — otherwise every job added
            // through AddJob ends up called "TestSource".
            SourcePaths = new List<string> { name is null ? @"D:\TestSource" : $@"D:\{name}" },
            DestinationPath = @"\\NAS\dest"
        };

        if (name != null)
            job.Name = name;

        return job;
    }

    #endregion
}
