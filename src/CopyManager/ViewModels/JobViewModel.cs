using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CopyManager.Models;

namespace CopyManager.ViewModels;

public partial class JobViewModel : ObservableObject
{
    private readonly CopyJob _job;
    private readonly Action<JobViewModel, string>? _onAction;

    public JobViewModel(CopyJob job, Action<JobViewModel, string>? onAction = null)
    {
        _job = job;
        _onAction = onAction;
    }

    public Guid Id => _job.Id;
    public CopyJob Model => _job;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SizeDisplay))]
    private bool _isCalculatingSize;

    public string Name => _job.Name;

    public JobStatus Status
    {
        get => _job.Status;
        set
        {
            if (_job.Status != value)
            {
                _job.Status = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(SizeDisplay));
                OnPropertyChanged(nameof(CanCancel));
                OnPropertyChanged(nameof(CanRetry));
                OnPropertyChanged(nameof(CanPause));
                OnPropertyChanged(nameof(CanEdit));
                OnPropertyChanged(nameof(IsExpanded));
            }
        }
    }

    public JobPriority Priority
    {
        get => _job.Priority;
        set
        {
            if (_job.Priority != value)
            {
                _job.Priority = value;
                OnPropertyChanged();
                _onAction?.Invoke(this, "PriorityChanged");
            }
        }
    }

    public double Progress
    {
        get
        {
            if (_job.TotalSizeBytes <= 0) return 0;
            return (double)_job.TransferredBytes / _job.TotalSizeBytes * 100;
        }
    }

    public string SizeDisplay
    {
        get
        {
            if (IsCalculatingSize)
                return "Calculating...";

            if (_job.TotalSizeBytes <= 0)
                return "—";

            var totalGb = _job.TotalSizeBytes / (1024.0 * 1024.0 * 1024.0);

            if (_job.Status == JobStatus.Running)
            {
                var transferredGb = _job.TransferredBytes / (1024.0 * 1024.0 * 1024.0);
                return $"{transferredGb:F1} / {totalGb:F1}";
            }

            return $"{totalGb:F1}";
        }
    }

    [ObservableProperty]
    private double _speedMbps;

    [ObservableProperty]
    private string? _timeRemaining;

    public long TransferredBytes
    {
        get => _job.TransferredBytes;
        set
        {
            _job.TransferredBytes = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Progress));
            OnPropertyChanged(nameof(SizeDisplay));
        }
    }

    public long TotalSizeBytes
    {
        get => _job.TotalSizeBytes;
        set
        {
            _job.TotalSizeBytes = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Progress));
            OnPropertyChanged(nameof(SizeDisplay));
        }
    }

    public string? LastErrorMessage => _job.LastErrorMessage;
    public int RetryCount => _job.RetryCount;
    public int MaxRetryCount => _job.MaxRetryCount;
    public DateTime? StartedAt => _job.StartedAt;
    public DateTime? CompletedAt => _job.CompletedAt;
    public List<string> SourcePaths => _job.SourcePaths;
    public string DestinationPath => _job.DestinationPath;
    public CopyMode Mode => _job.Mode;
    public string? LogFilePath => _job.LogFilePath;

    public bool CanCancel => Status is JobStatus.Running or JobStatus.Pending;
    public bool CanRetry => Status == JobStatus.Failed;
    public bool CanPause => Status == JobStatus.Running;
    public bool CanEdit => Status == JobStatus.Pending;

    [ObservableProperty]
    private bool _isExpanded;

    public string ElapsedTime
    {
        get
        {
            if (_job.StartedAt is null) return "—";
            var end = _job.CompletedAt ?? DateTime.Now;
            var elapsed = end - _job.StartedAt.Value;
            return elapsed.ToString(@"hh\:mm\:ss");
        }
    }

    [RelayCommand]
    private void Cancel() => _onAction?.Invoke(this, "Cancel");

    [RelayCommand]
    private void Retry() => _onAction?.Invoke(this, "Retry");

    [RelayCommand]
    private void Pause() => _onAction?.Invoke(this, "Pause");

    [RelayCommand]
    private void Resume() => _onAction?.Invoke(this, "Resume");

    [RelayCommand]
    private void Delete() => _onAction?.Invoke(this, "Delete");

    [RelayCommand]
    private void ViewLog() => _onAction?.Invoke(this, "ViewLog");

    public void Refresh()
    {
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(Status));
        OnPropertyChanged(nameof(Priority));
        OnPropertyChanged(nameof(Progress));
        OnPropertyChanged(nameof(SizeDisplay));
        OnPropertyChanged(nameof(TransferredBytes));
        OnPropertyChanged(nameof(TotalSizeBytes));
        OnPropertyChanged(nameof(LastErrorMessage));
        OnPropertyChanged(nameof(RetryCount));
        OnPropertyChanged(nameof(ElapsedTime));
        OnPropertyChanged(nameof(CanCancel));
        OnPropertyChanged(nameof(CanRetry));
        OnPropertyChanged(nameof(CanPause));
        OnPropertyChanged(nameof(CanEdit));
    }
}
