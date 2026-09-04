# CopyManager - CLAUDE.md

## Project Overview

CopyManager is a Windows-only FastCopy job management tool.
It provides a WPF GUI for queuing, monitoring, and retrying multiple large-file / large-folder
copy operations to a NAS, using FastCopy as the copy engine via CLI.

- **Platform**: Windows 10 / 11 only
- **UI Framework**: WPF (.NET 9+)
- **Language**: C# 13+
- **FastCopy integration**: Command-line (CLI) via `System.Diagnostics.Process`
- **Application language**: English (all UI labels, messages, and tooltips)

---

## Tech Stack

| Item | Detail |
|------|--------|
| Runtime | .NET 9 (Windows) |
| UI | WPF (Windows Presentation Foundation) |
| Language | C# 13 |
| FastCopy integration | Launch `FastCopy.exe` via `System.Diagnostics.Process` |
| Settings persistence | JSON (`System.Text.Json`) |
| Job persistence | JSON file (local) |

---

## Repository Layout

```
CopyManager/
├── CLAUDE.md
├── spec.md
├── CopyManager.sln
├── src/
│   └── CopyManager/
│       ├── CopyManager.csproj
│       ├── App.xaml / App.xaml.cs
│       ├── MainWindow.xaml / MainWindow.xaml.cs
│       ├── Models/
│       │   ├── CopyJob.cs          # Job data model (incl. Priority, TotalSizeBytes)
│       │   └── AppSettings.cs      # Application settings model
│       ├── ViewModels/
│       │   ├── MainViewModel.cs    # Main window VM
│       │   └── JobViewModel.cs     # Per-job VM
│       ├── Services/
│       │   ├── FastCopyService.cs  # FastCopy.exe wrapper
│       │   ├── JobQueueService.cs  # Queue management & execution control
│       │   └── SettingsService.cs  # Settings read/write
│       ├── Views/
│       │   ├── JobEditDialog.xaml  # Add / edit job dialog
│       │   └── SettingsDialog.xaml # Application settings dialog
│       └── Converters/             # WPF value converters
└── tests/
    └── CopyManager.Tests/
        ├── CopyManager.Tests.csproj
        ├── FastCopyServiceTests.cs
        └── JobQueueServiceTests.cs
```

---

## Build & Run

```bash
# Build
dotnet build CopyManager.sln

# Run
dotnet run --project src/CopyManager/CopyManager.csproj

# Test
dotnet test tests/CopyManager.Tests/CopyManager.Tests.csproj

# Release build (Windows self-contained)
dotnet publish src/CopyManager/CopyManager.csproj \
  -c Release -r win-x64 --self-contained true \
  -o ./publish
```

---

## Key Design Decisions

### MVVM Pattern
Standard WPF MVVM with `INotifyPropertyChanged` + `ICommand`.
External libraries are kept minimal; `CommunityToolkit.Mvvm` (source generators) is allowed.

### FastCopy Integration
- `FastCopyService` builds the argument string and launches `FastCopy.exe` via `Process`
- stdout / stderr are read asynchronously; progress and errors are pushed to `JobViewModel`
- Job success/failure is determined by the FastCopy exit code (0 = success)

### Job Queue
- `JobQueueService` manages jobs in priority order (High → Normal → Low), then FIFO within the same priority
- Max concurrent jobs is configurable (default: 1)
- Job states: `Pending → Running → Completed / Failed / Cancelled`
- Failed jobs are automatically re-queued up to the configured retry limit

### Job Naming
- Job names are **never entered by the user**
- They are derived automatically from the first source path's file/folder name
- Multiple sources: `{firstName} (+{n-1} more)`

### Total Size Calculation
- When a job is created (or its sources edited), `TotalSizeBytes` is computed in the background
- Shows as `Calculating...` until complete, then `X.XX GB` in the job list
- While a job is running, the column shows `transferred GB / total GB`

### Settings & Job Persistence
- App settings saved to `%APPDATA%\CopyManager\settings.json`
- Job list saved to `%APPDATA%\CopyManager\jobs.json`
- On startup, unfinished jobs are restored and reset to `Pending`

---

## FastCopy CLI Reference

```
FastCopy.exe /cmd=<command> [options] "source1" "source2" /to="destination"

Commands:
  diff        Differential copy (new / updated files only)
  force_copy  Overwrite all files
  move        Move files

Key options:
  /bufsize=256m     Buffer size
  /speed=full       Transfer speed (full / autoslow / 1..9)
  /log              Enable logging
  /logfile="path"   Log file path
  /auto_close       Auto-close FastCopy window on finish
  /no_confirm       Suppress confirmation dialogs
  /error_stop       Stop on error
  /filelog          Per-file log output
```

---

## Coding Conventions

- Naming: C# standard (PascalCase / camelCase)
- Async: use `async/await` throughout; never block the UI thread
- Error handling: always catch exceptions from external process calls and record them on the job
- Logging: use `Microsoft.Extensions.Logging`
- Unit tests: `xUnit`; `FastCopyService` and `JobQueueService` must have test coverage

---

## Dependencies

```xml
<!-- src/CopyManager/CopyManager.csproj -->
<PackageReference Include="CommunityToolkit.Mvvm" Version="8.*" />
<PackageReference Include="Microsoft.Extensions.Logging" Version="9.*" />

<!-- tests/CopyManager.Tests/CopyManager.Tests.csproj -->
<PackageReference Include="xunit" Version="2.*" />
<PackageReference Include="Moq" Version="4.*" />
```

---

## Common Workflow Patterns

### Adding a Job
1. User clicks **Add Job**
2. `JobEditDialog` opens — user selects sources, destination, mode, and priority
3. Job name is derived automatically from the first source path
4. Background task calculates `TotalSizeBytes`
5. `MainViewModel.AddJobCommand` creates `CopyJob` and passes it to `JobQueueService`
6. `JobQueueService` inserts the job at the correct priority position; launches `FastCopyService` if a slot is free

### Progress Update Flow
1. `FastCopyService` reads FastCopy.exe stdout asynchronously
2. Parsed results are pushed to `JobViewModel.Progress`, `TransferredBytes`, `SpeedMbps`, etc.
3. WPF `ProgressBar` and the Size column update in real time via data binding
