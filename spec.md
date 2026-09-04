# CopyManager Specification

**Version**: 1.1.0
**Updated**: 2026-03-21
**Platform**: Windows 10 / 11 (x64)

---

## 1. Purpose & Background

### 1.1 Purpose
Provide a job management front-end for FastCopy that allows users to queue, monitor, and
retry multiple large-file copy operations to a NAS — all from a single GUI.

### 1.2 Problems Solved
- Copying large files blocks other work; jobs should queue and run sequentially (or in parallel)
- Multiple source/destination combinations are hard to track; each job stores its own settings
- Copy progress should be visible at a glance across all jobs in real time
- Network instability causes transfer failures; automatic retry is required

### 1.3 Prerequisites
- FastCopy 3.x or later must be installed on the target PC
- The destination NAS must be accessible from Windows as a drive letter or UNC path (`\\NAS\share\`)

---

## 2. User Stories

| ID | Story |
|----|-------|
| US-01 | Add copy jobs and have them execute one after another in a queue |
| US-02 | See each running job's progress percentage, transfer speed, and estimated time remaining in real time |
| US-03 | Have failed jobs retried automatically; also be able to retry manually |
| US-04 | Review history of completed, failed, and cancelled jobs |
| US-05 | Have unfinished jobs restored after the app restarts or crashes |
| US-06 | Configure FastCopy path, buffer size, and other global options |
| US-07 | Reorder pending jobs and delete unwanted ones |
| US-08 | Assign a priority level to each job so higher-priority transfers run first |

---

## 3. Functional Requirements

### 3.1 Job Management

#### 3.1.1 Job Creation
- One or more source paths (file or folder) can be specified per job
- Exactly one destination path (folder) is required
- **Job name is not entered manually.** It is derived automatically:
  - Single source → name equals the file or folder name of that source (e.g. `Videos_2025`)
  - Multiple sources → `{firstName} (+{n-1} more)` (e.g. `Videos_2025 (+2 more)`)
- Copy mode is selectable: **Differential** / **Force Overwrite** / **Move**
- Each job has a **Priority** level: **High** / **Normal** (default) / **Low**
- FastCopy options can be overridden per job (buffer size, speed)

#### 3.1.2 Priority & Queue Ordering
- The queue is ordered first by priority (High → Normal → Low), then FIFO within the same level
- Changing a pending job's priority immediately re-sorts it in the queue
- Drag-and-drop reordering is supported for jobs that share the same priority level
- Running jobs are not affected when queue order changes

#### 3.1.3 Job Queue Management
- Jobs can be edited or deleted while in Pending state
- A running job can be paused (suspends the FastCopy process) or cancelled
- Cancelled jobs are kept in the list with Cancelled status for reference

#### 3.1.4 Job State Machine

```
[Added]
   │
   ▼
Pending ──────────────────────────────────┐
   │  Slot available & job is next         │ (priority re-sort)
   ▼                                       │
Running                                    │
   │           │             │             │
   ▼           ▼             ▼             │
Completed   Failed        Cancelled        │
               │                           │
               ▼ auto-retry (below limit)  │
            Pending ────────────────────── ┘
```

### 3.2 Progress Monitoring

- The job list shows each job's **total size [GB]**, progress bar, and percentage
- Running jobs additionally show: transfer speed (MB/s), bytes transferred, estimated time remaining
- FastCopy stdout is parsed asynchronously in real time
- On completion, total transferred size and elapsed time are recorded per job

### 3.3 Total Size Display

- When a job is created, the total size of all source paths is calculated in the background
  using `Directory.EnumerateFiles` / `FileInfo.Length`
- Displayed as `X.XX GB` in the job list (recalculated if sources are edited)
- While the size is still being calculated, display `Calculating...`
- For running jobs the cell shows `X.XX GB / Y.YY GB` (transferred / total)

### 3.4 Error Handling & Retry

- A non-zero FastCopy exit code transitions the job to `Failed`
- Retry interval and maximum retry count are configurable (defaults: 30 s interval, 3 retries)
- Once the retry limit is exceeded the job stays `Failed` and a toast notification is shown
- Manual retry: re-queues a `Failed` job at the tail of its priority group
- The FastCopy output log is stored per-job and viewable in the detail panel

### 3.5 Settings

| Setting | Default | Description |
|---------|---------|-------------|
| FastCopy.exe path | Auto-detect | Absolute path to FastCopy.exe |
| Buffer size | 256 MB | FastCopy `/bufsize` |
| Transfer speed | full | FastCopy `/speed` |
| Max concurrent jobs | 1 | Maximum parallel job count |
| Max retry count | 3 | Auto-retry limit on failure |
| Retry interval (s) | 30 | Wait time before each retry |
| Log directory | `%APPDATA%\CopyManager\logs` | FastCopy log storage |
| Job completion toast | Enabled | Windows toast notification |

---

## 4. Non-Functional Requirements

| Item | Requirement |
|------|-------------|
| OS | Windows 10 (21H2+) / Windows 11 |
| Architecture | x64 |
| .NET version | .NET 9+ |
| UI responsiveness | UI must not freeze during transfers (async throughout) |
| Startup time | Under 3 seconds |
| Job persistence | Unfinished jobs restored after restart or crash |
| Log retention | 30 days (auto-rotation) |
| Deployment | xcopy-deployable (no registry) or MSIX/installer |
| Language | English (all UI labels, messages, and tooltips) |

---

## 5. UI Design

### 5.1 Main Window

```
┌──────────────────────────────────────────────────────────────────────────┐
│  CopyManager                                              [_][□][×]      │
├──────────────────────────────────────────────────────────────────────────┤
│  [+ Add Job]   [▶ Start All]   [⏸ Pause All]   [⚙ Settings]            │
├────┬───────────────────────┬────────┬──────────┬─────────────┬──────────┤
│ #  │ Job Name              │ Status │ Priority │ Size (GB)   │ Progress │
├────┼───────────────────────┼────────┼──────────┼─────────────┼──────────┤
│ 1  │ Videos_2025           │Running │ High     │ 42.3 / 56.7 │ ████ 75% │
│ 2  │ Photos (+2 more)      │Pending │ Normal   │ 12.1        │ —        │
│ 3  │ Documents_Q1          │Done    │ Normal   │ 2.4         │ ████100% │
│ 4  │ RAW_Footage           │Failed  │ Low      │ 3.8 / 12.6  │ ███  30% │
├────┴───────────────────────┴────────┴──────────┴─────────────┴──────────┤
│  Speed: 85 MB/s  │  Running: 1  Pending: 1  Done: 1  Failed: 1          │
└──────────────────────────────────────────────────────────────────────────┘
```

**Column details**

| Column | Notes |
|--------|-------|
| # | Queue position (1 = next to run) |
| Job Name | Auto-derived from source path(s); read-only in the list |
| Status | Pending / Running / Paused / Done / Failed / Cancelled |
| Priority | High / Normal / Low — editable inline via dropdown |
| Size (GB) | Total source size; `X.XX / Y.YY` while running; `Calculating...` on creation |
| Progress | ProgressBar + percentage; speed shown in status bar for the active job |

### 5.2 Add / Edit Job Dialog

```
┌──────────────────────────────────────────────────────┐
│  Add Job                                      [×]    │
├──────────────────────────────────────────────────────┤
│  Copy Mode:  (●) Differential                        │
│              ( ) Force Overwrite                     │
│              ( ) Move                                │
│                                                      │
│  Priority:   [Normal ▼]  (High / Normal / Low)       │
│                                                      │
│  Source(s):                                          │
│   [D:\Videos\2025\              ]  [Browse] [Remove] │
│   [E:\Backup\Photos\            ]  [Browse] [Remove] │
│   [+ Add Source]                                     │
│                                                      │
│  Destination:                                        │
│   [\\NAS01\archive\2025\        ]  [Browse]          │
│                                                      │
│  ▼ Advanced Options                                  │
│     Buffer size: [256] MB                            │
│     Speed:       [full ▼]                            │
│                                                      │
│  Job name (preview):  Videos_2025                    │
│                                                      │
│               [Cancel]          [Add]                │
└──────────────────────────────────────────────────────┘
```

- The **Job name preview** line is read-only; it updates automatically as sources are added/removed
- Priority defaults to **Normal**

### 5.3 Job Detail Panel (expanded on row click)

```
┌──────────────────────────────────────────────────────┐
│  Job Detail: Videos_2025                             │
│  Sources:     D:\Videos\2025\                        │
│  Destination: \\NAS01\archive\2025\                  │
│  Priority:    High                                   │
│  Total size:  56.7 GB                                │
│  Started:     2026-03-21  14:32:10                   │
│  Transferred: 42.3 GB / 56.7 GB                      │
│  Elapsed:     00:08:32     Remaining: 00:02:41       │
│  Retries:     0 / 3                                  │
│                                                      │
│  [View FastCopy Log]                                 │
└──────────────────────────────────────────────────────┘
```

---

## 6. Data Models

### 6.1 CopyJob

```csharp
public class CopyJob
{
    public Guid Id { get; set; }
    public string Name { get; set; }               // auto-derived; not user-editable
    public List<string> SourcePaths { get; set; }
    public string DestinationPath { get; set; }
    public CopyMode Mode { get; set; }             // Differential, ForceCopy, Move
    public JobStatus Status { get; set; }          // Pending, Running, Paused, Completed, Failed, Cancelled
    public JobPriority Priority { get; set; }      // High, Normal, Low
    public int QueueOrder { get; set; }            // tie-breaker within the same priority (FIFO)
    public JobOptions Options { get; set; }        // per-job FastCopy option overrides
    public int RetryCount { get; set; }
    public int MaxRetryCount { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public long TotalSizeBytes { get; set; }       // pre-calculated source size
    public long TransferredBytes { get; set; }
    public string? LastErrorMessage { get; set; }
    public string? LogFilePath { get; set; }
}

public class JobOptions
{
    public int? BufferSizeMb { get; set; }         // null = use global setting
    public string? Speed { get; set; }
}

public enum CopyMode     { Differential, ForceCopy, Move }
public enum JobStatus    { Pending, Running, Paused, Completed, Failed, Cancelled }
public enum JobPriority  { High, Normal, Low }
```

### 6.2 AppSettings

```csharp
public class AppSettings
{
    public string FastCopyExePath { get; set; }
    public int DefaultBufferSizeMb { get; set; }      = 256;
    public string DefaultSpeed { get; set; }           = "full";
    public int MaxConcurrentJobs { get; set; }         = 1;
    public int MaxRetryCount { get; set; }             = 3;
    public int RetryIntervalSeconds { get; set; }      = 30;
    public string LogDirectory { get; set; }
    public bool EnableToastNotification { get; set; }  = true;
}
```

---

## 7. FastCopy Integration

### 7.1 Command-Line Construction

```
FastCopy.exe
  /cmd=<diff|force_copy|move>
  /bufsize=<N>m
  /speed=<full|autoslow|1..9>
  /log /logfile="<logFilePath>"
  /auto_close
  /no_confirm
  /error_stop
  "source1" "source2" ...
  /to="destination\"
```

### 7.2 stdout Parsing

| Field | Regex example |
|-------|--------------|
| Progress (%) | `\((\d+\.?\d*)%\)` |
| Transfer speed | `(\d+\.?\d*)\s*MB/s` |
| Time remaining | `(?:ETA|Remaining)[:\s]+(\d+:\d+(?::\d+)?)` |
| Transferred bytes | `(\d[\d,]*)\s*/` |
| Total bytes | `/\s*(\d[\d,]*)` |

Sample FastCopy output line:
```
Total: 42,345,678,912 / 56,789,012,345 bytes  (74.6%)  85.3 MB/s  ETA 02:41
```

### 7.3 Exit Codes

| Code | Meaning |
|------|---------|
| 0 | Success |
| Non-zero | Error (see log for details) |

---

## 8. File & Directory Layout (runtime)

```
%APPDATA%\CopyManager\
├── settings.json        # Application settings
├── jobs.json            # Persisted job list
└── logs\
    ├── app.log          # Application log
    └── jobs\
        ├── <JobId>.log  # Per-job FastCopy output log
        └── ...
```

---

## 9. Notifications

- **Job completed**: Windows toast — job name, Done/Failed, bytes transferred, elapsed time
- **Retry triggered**: Status bar message only (no toast)
- **All jobs done**: Toast + restore normal app icon in taskbar
- Toasts can be disabled globally in Settings

---

## 10. Future Scope (out of v1)

The following are explicitly out of scope for v1 but should be kept in mind architecturally:

- Scheduled execution (start at a specified date/time)
- Shutdown PC after all jobs complete
- Simultaneous copy to multiple NAS destinations
- Job templates / presets
- CLI arguments for adding jobs from batch scripts
