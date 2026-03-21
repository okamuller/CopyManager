using System.Windows;
using CopyManager.Models;
using CopyManager.ViewModels;
using CopyManager.Views;

namespace CopyManager;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private MainViewModel ViewModel => (MainViewModel)DataContext;

    private void OnAddJobClick(object sender, RoutedEventArgs e)
    {
        var dialog = new JobEditDialog
        {
            Owner = this
        };

        if (dialog.ShowDialog() == true && dialog.ResultJob is not null)
        {
            ViewModel.AddJobFromDialog(dialog.ResultJob);
        }
    }

    private void OnSettingsClick(object sender, RoutedEventArgs e)
    {
        var settingsService = GetSettingsService();
        if (settingsService is null) return;

        var dialog = new SettingsDialog(settingsService.Settings)
        {
            Owner = this
        };

        if (dialog.ShowDialog() == true)
        {
            _ = settingsService.SaveAsync();
        }
    }

    private Services.ISettingsService? GetSettingsService()
    {
        // Access via the ViewModel's internal reference
        // In a full DI setup this would be injected
        var field = typeof(MainViewModel).GetField("_settingsService",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        return field?.GetValue(DataContext) as Services.ISettingsService;
    }
}
