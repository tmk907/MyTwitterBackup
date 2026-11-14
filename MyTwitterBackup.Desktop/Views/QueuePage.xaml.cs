using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using MyTwitterBackup.Desktop.ViewModels;

namespace MyTwitterBackup.Desktop.Views;

public sealed partial class QueuePage : Page
{
    public QueuePage()
    {
        ViewModel = App.GetService<QueueViewModel>();
        InitializeComponent();
    }

    public QueueViewModel ViewModel { get; }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        await ViewModel.ActivateAsync();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        ViewModel.Deactivate();
    }

    private async void ClearQueueButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Clear the entire queue?",
            Content = $"All {ViewModel.Counts.Total} queue item(s) will be removed, including pending and failed ones. " +
                      "Downloaded posts and media are not deleted.",
            PrimaryButtonText = "Clear queue",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            await ViewModel.ClearAllCommand.ExecuteAsync(null);
        }
    }
}
