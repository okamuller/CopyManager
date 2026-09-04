using System.IO;

namespace CopyManager.Models;

public class CopyJob
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public List<string> SourcePaths { get; set; } = new();
    public string DestinationPath { get; set; } = string.Empty;
    public CopyMode Mode { get; set; } = CopyMode.Differential;
    public JobStatus Status { get; set; } = JobStatus.Pending;
    public JobPriority Priority { get; set; } = JobPriority.Normal;
    public int QueueOrder { get; set; }
    public JobOptions Options { get; set; } = new();
    public int RetryCount { get; set; }
    public int MaxRetryCount { get; set; } = 3;
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public long TotalSizeBytes { get; set; }
    public long TransferredBytes { get; set; }
    public string? LastErrorMessage { get; set; }
    public string? LogFilePath { get; set; }

    /// <summary>
    /// Derives the job name from source paths per spec §3.1.1.
    /// Single source: file/folder name. Multiple: "{first} (+{n-1} more)".
    /// </summary>
    public static string DeriveName(IReadOnlyList<string> sourcePaths)
    {
        if (sourcePaths.Count == 0)
            return string.Empty;

        var firstName = Path.GetFileName(sourcePaths[0].TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (string.IsNullOrEmpty(firstName))
            firstName = sourcePaths[0];

        return sourcePaths.Count == 1
            ? firstName
            : $"{firstName} (+{sourcePaths.Count - 1} more)";
    }
}

public class JobOptions
{
    public int? BufferSizeMb { get; set; }
    public string? Speed { get; set; }
}

public enum CopyMode
{
    Differential,
    ForceCopy,
    Move
}

public enum JobStatus
{
    Pending,
    Running,
    Paused,
    Completed,
    Failed,
    Cancelled
}

public enum JobPriority
{
    High,
    Normal,
    Low
}
