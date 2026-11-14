using Windows.Storage.Pickers;

namespace MyTwitterBackup.Desktop.Services;

public static class FolderPickerService
{
    public static async Task<string?> PickFolderAsync()
    {
        var picker = new FolderPicker { SuggestedStartLocation = PickerLocationId.Downloads };
        picker.FileTypeFilter.Add("*");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, App.WindowHandle);
        var folder = await picker.PickSingleFolderAsync();
        return folder?.Path;
    }

    public static async Task OpenFolderAsync(string path)
    {
        Directory.CreateDirectory(path);
        await Windows.System.Launcher.LaunchFolderPathAsync(path);
    }
}
