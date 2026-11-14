using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MyTwitterBackup.Core.Database;
using MyTwitterBackup.Core.Services;
using MyTwitterBackup.Desktop.Services;
using Serilog;

namespace MyTwitterBackup.Desktop.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly IAppSettings _settings;
    private readonly DatabaseInitializer _databaseInitializer;
    private readonly IAppDbContextFactory _dbFactory;
    private readonly DownloadQueueService _queue;

    public SettingsViewModel(IAppSettings settings, DatabaseInitializer databaseInitializer,
        IAppDbContextFactory dbFactory, DownloadQueueService queue)
    {
        _settings = settings;
        _databaseInitializer = databaseInitializer;
        _dbFactory = dbFactory;
        _queue = queue;
        DownloadsFolder = settings.DownloadsFolder;
        SaveRawResponses = settings.SaveRawResponses;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DatabasePath))]
    public partial string DownloadsFolder { get; set; }

    [ObservableProperty]
    public partial bool SaveRawResponses { get; set; }

    [ObservableProperty]
    public partial string? Message { get; set; }

    [ObservableProperty]
    public partial bool IsError { get; set; }

    public string DatabasePath => _dbFactory.DatabasePath;

    public string DefaultFolder => AppSettings.DefaultFolder;

    public string AppVersion
    {
        get
        {
            try
            {
                var v = Windows.ApplicationModel.Package.Current.Id.Version;
                return $"{v.Major}.{v.Minor}.{v.Build}.{v.Revision}";
            }
            catch (InvalidOperationException)
            {
                return "unpackaged";
            }
        }
    }

    partial void OnSaveRawResponsesChanged(bool value) => _settings.SaveRawResponses = value;

    [RelayCommand]
    private async Task ChangeDownloadsFolderAsync()
    {
        var folder = await FolderPickerService.PickFolderAsync();
        if (folder is not null)
        {
            await SetDownloadsFolderAsync(folder);
        }
    }

    [RelayCommand]
    private Task ResetFolderAsync() => SetDownloadsFolderAsync(AppSettings.DefaultFolder);

    /// <summary>Switches the database and the media folder together.</summary>
    private async Task SetDownloadsFolderAsync(string folder)
    {
        var previous = _settings.DownloadsFolder;
        _settings.DownloadsFolder = folder;
        try
        {
            _databaseInitializer.EnsureCreated();
            DownloadsFolder = folder;
            await _queue.ReloadAsync();
            ShowMessage($"Using database {DatabasePath}. An existing database in that folder is opened, otherwise a new one is created. " +
                        "Data and media in the previous folder are not moved.", false);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Could not use folder {Folder}", folder);
            _settings.DownloadsFolder = previous;
            ShowMessage($"Could not use this folder: {ex.Message}", true);
        }
    }

    [RelayCommand]
    private Task OpenDownloadsFolderAsync() => FolderPickerService.OpenFolderAsync(DownloadsFolder);

    [RelayCommand]
    private Task OpenLogsFolderAsync() => FolderPickerService.OpenFolderAsync(AppSettings.GetLogsFolder(_settings));

    private void ShowMessage(string message, bool isError)
    {
        Message = message;
        IsError = isError;
    }
}
