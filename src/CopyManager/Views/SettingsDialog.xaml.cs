using System.Windows;
using CopyManager.Models;
using Microsoft.Win32;

namespace CopyManager.Views;

public partial class SettingsDialog : Window
{
    private readonly AppSettings _settings;

    public SettingsDialog(AppSettings settings)
    {
        InitializeComponent();
        _settings = settings;

        // Populate fields
        FastCopyPathText.Text = settings.FastCopyExePath;
        BufferSizeText.Text = settings.DefaultBufferSizeMb.ToString();
        MaxConcurrentText.Text = settings.MaxConcurrentJobs.ToString();
        MaxRetryText.Text = settings.MaxRetryCount.ToString();
        RetryIntervalText.Text = settings.RetryIntervalSeconds.ToString();
        LogDirectoryText.Text = settings.LogDirectory;

        // Select speed
        foreach (System.Windows.Controls.ComboBoxItem item in SpeedCombo.Items)
        {
            if (item.Content?.ToString() == settings.DefaultSpeed)
            {
                SpeedCombo.SelectedItem = item;
                break;
            }
        }
    }

    private void OnBrowseFastCopyClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select FastCopy.exe",
            Filter = "FastCopy|FastCopy.exe|All Files|*.*"
        };

        if (dialog.ShowDialog() == true)
        {
            FastCopyPathText.Text = dialog.FileName;
        }
    }

    private void OnBrowseLogDirClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Select Log Directory"
        };

        if (dialog.ShowDialog() == true)
        {
            LogDirectoryText.Text = dialog.FolderName;
        }
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        _settings.FastCopyExePath = FastCopyPathText.Text.Trim();

        if (int.TryParse(BufferSizeText.Text, out var bufSize) && bufSize > 0)
            _settings.DefaultBufferSizeMb = bufSize;

        var speed = (SpeedCombo.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Content?.ToString();
        if (!string.IsNullOrEmpty(speed))
            _settings.DefaultSpeed = speed;

        if (int.TryParse(MaxConcurrentText.Text, out var maxConcurrent) && maxConcurrent > 0)
            _settings.MaxConcurrentJobs = maxConcurrent;

        if (int.TryParse(MaxRetryText.Text, out var maxRetry) && maxRetry >= 0)
            _settings.MaxRetryCount = maxRetry;

        if (int.TryParse(RetryIntervalText.Text, out var retryInterval) && retryInterval >= 0)
            _settings.RetryIntervalSeconds = retryInterval;

        if (!string.IsNullOrWhiteSpace(LogDirectoryText.Text))
            _settings.LogDirectory = LogDirectoryText.Text.Trim();

        DialogResult = true;
        Close();
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
