using Microsoft.UI.Xaml.Controls;
using MyTwitterBackup.Desktop.ViewModels;

namespace MyTwitterBackup.Desktop.Views;

public sealed partial class SettingsPage : Page
{
    public SettingsPage()
    {
        ViewModel = App.GetService<SettingsViewModel>();
        InitializeComponent();
    }

    public SettingsViewModel ViewModel { get; }
}
