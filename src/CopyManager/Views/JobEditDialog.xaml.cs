using System.Collections.ObjectModel;
using System.Windows;
using CopyManager.Models;
using Microsoft.Win32;

namespace CopyManager.Views;

public partial class JobEditDialog : Window
{
    private readonly ObservableCollection<string> _sources = new();
    public CopyJob? ResultJob { get; private set; }

    public JobEditDialog()
    {
        InitializeComponent();
        SourcesList.ItemsSource = _sources;
        _sources.CollectionChanged += (_, _) => UpdateJobNamePreview();
    }

    /// <summary>
    /// Constructor for editing an existing job.
    /// </summary>
    public JobEditDialog(CopyJob existingJob) : this()
    {
        Title = "Edit Job";

        foreach (var source in existingJob.SourcePaths)
            _sources.Add(source);

        DestinationText.Text = existingJob.DestinationPath;

        switch (existingJob.Mode)
        {
            case CopyMode.Differential: RadioDifferential.IsChecked = true; break;
            case CopyMode.ForceCopy: RadioForceCopy.IsChecked = true; break;
            case CopyMode.Move: RadioMove.IsChecked = true; break;
        }

        ComboPriority.SelectedItem = existingJob.Priority;

        if (existingJob.Options.BufferSizeMb.HasValue)
            BufferSizeText.Text = existingJob.Options.BufferSizeMb.Value.ToString();

        if (!string.IsNullOrEmpty(existingJob.Options.Speed))
        {
            foreach (System.Windows.Controls.ComboBoxItem item in SpeedCombo.Items)
            {
                if (item.Content?.ToString() == existingJob.Options.Speed)
                {
                    SpeedCombo.SelectedItem = item;
                    break;
                }
            }
        }
    }

    private void OnAddFolderClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Select Source Folder",
            Multiselect = false
        };

        if (dialog.ShowDialog() == true)
        {
            _sources.Add(dialog.FolderName);
        }
    }

    private void OnAddFileClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select Source File",
            Multiselect = true
        };

        if (dialog.ShowDialog() == true)
        {
            foreach (var file in dialog.FileNames)
                _sources.Add(file);
        }
    }

    private void OnRemoveSourceClick(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button button && button.Tag is string path)
        {
            _sources.Remove(path);
        }
    }

    private void OnBrowseDestinationClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Select Destination Folder"
        };

        if (dialog.ShowDialog() == true)
        {
            DestinationText.Text = dialog.FolderName;
        }
    }

    private void OnAddClick(object sender, RoutedEventArgs e)
    {
        if (_sources.Count == 0)
        {
            MessageBox.Show("Please add at least one source path.", "Validation",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (string.IsNullOrWhiteSpace(DestinationText.Text))
        {
            MessageBox.Show("Please specify a destination path.", "Validation",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var mode = RadioForceCopy.IsChecked == true ? CopyMode.ForceCopy
                 : RadioMove.IsChecked == true ? CopyMode.Move
                 : CopyMode.Differential;

        var priority = ComboPriority.SelectedItem is JobPriority p ? p : JobPriority.Normal;

        var options = new JobOptions();
        if (int.TryParse(BufferSizeText.Text, out var bufSize) && bufSize > 0)
            options.BufferSizeMb = bufSize;

        var selectedSpeed = (SpeedCombo.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Content?.ToString();
        if (!string.IsNullOrEmpty(selectedSpeed))
            options.Speed = selectedSpeed;

        ResultJob = new CopyJob
        {
            SourcePaths = _sources.ToList(),
            DestinationPath = DestinationText.Text.Trim(),
            Mode = mode,
            Priority = priority,
            Options = options
        };

        DialogResult = true;
        Close();
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void UpdateJobNamePreview()
    {
        JobNamePreview.Text = _sources.Count > 0
            ? CopyJob.DeriveName(_sources.ToList())
            : "—";
    }
}
