# CopyManager

FastCopy job management front-end for Windows. Queue, monitor, and retry multiple large-file copy operations to a NAS from a single GUI.

## Features

- **Job Queue** — Add multiple copy jobs and execute them in priority order (High / Normal / Low), with FIFO within the same level
- **Real-time Progress** — Monitor transfer speed, progress percentage, and ETA parsed from FastCopy stdout
- **Auto-retry** — Failed jobs are automatically retried with configurable interval and limit
- **Persistence** — Jobs and settings are saved to `%APPDATA%\CopyManager\` as JSON; unfinished jobs are restored on restart
- **Per-job Options** — Override buffer size and transfer speed per job
- **Background Size Calculation** — Total source size is computed asynchronously on job creation

## Requirements

- Windows 10 (21H2+) / Windows 11 (x64)
- .NET 10 Runtime
- [FastCopy](https://fastcopy.jp/) 3.x or later

## Build & Run

```bash
# Build
dotnet build CopyManager.sln

# Run
dotnet run --project src/CopyManager/CopyManager.csproj

# Test
dotnet test tests/CopyManager.Tests/CopyManager.Tests.csproj

# Publish (self-contained)
dotnet publish src/CopyManager/CopyManager.csproj -c Release -r win-x64 --self-contained true -o ./publish
```

## Project Structure

```
CopyManager/
├── CopyManager.sln
├── src/CopyManager/
│   ├── Models/          # CopyJob, AppSettings, enums
│   ├── Services/        # FastCopyService, JobQueueService, SettingsService
│   ├── ViewModels/      # MainViewModel, JobViewModel (MVVM)
│   ├── Views/           # JobEditDialog, SettingsDialog
│   ├── Converters/      # WPF value converters
│   ├── MainWindow.xaml  # Main window UI
│   └── App.xaml         # Application entry point
└── tests/CopyManager.Tests/
    ├── FastCopyServiceTests.cs
    └── JobQueueServiceTests.cs
```

## Architecture

- **MVVM** pattern with [CommunityToolkit.Mvvm](https://learn.microsoft.com/dotnet/communitytoolkit/mvvm/) source generators
- **FastCopyService** — Builds CLI arguments and launches `FastCopy.exe` via `System.Diagnostics.Process`, parses stdout with regex for real-time progress
- **JobQueueService** — Priority-based queue with configurable concurrent job slots, automatic retry, and JSON persistence
- **SettingsService** — Loads/saves settings to `%APPDATA%\CopyManager\settings.json`, auto-detects FastCopy installation

## Settings

| Setting | Default | Description |
|---------|---------|-------------|
| FastCopy.exe path | Auto-detect | Path to FastCopy executable |
| Buffer size | 256 MB | FastCopy `/bufsize` parameter |
| Transfer speed | full | FastCopy `/speed` parameter |
| Max concurrent jobs | 1 | Parallel job limit |
| Max retry count | 3 | Auto-retry limit per job |
| Retry interval | 30 s | Delay between retries |
| Log directory | `%APPDATA%\CopyManager\logs` | FastCopy log storage |

## License

Proprietary.
