using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using MyTwitterBackup.Core.Models;
using MyTwitterBackup.Desktop.ViewModels;

namespace MyTwitterBackup.Desktop.Views;

public sealed partial class DownloadedPage : Page
{
    public DownloadedPage()
    {
        ViewModel = App.GetService<DownloadedViewModel>();
        InitializeComponent();
    }

    public DownloadedViewModel ViewModel { get; }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        await ViewModel.LoadAsync();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        ViewModel.Dispose();
    }

    private void SourceSelector_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        if (sender.SelectedItem?.Tag is string tag && Enum.TryParse<SourceType>(tag, out var source))
        {
            ViewModel.SelectedSource = source;
        }
    }

    private void MediaPlayerElement_Unloaded(object sender, RoutedEventArgs e)
    {
        // Stop playback when the preview changes; otherwise audio keeps playing.
        if (sender is MediaPlayerElement element)
        {
            element.MediaPlayer?.Pause();
            element.Source = null;
        }
    }
}
