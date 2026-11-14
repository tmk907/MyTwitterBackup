using MyTwitterBackup.Core.Services;
using Windows.Storage;

namespace MyTwitterBackup.Desktop.Services;

/// <summary>Settings stored in the local settings of the packaged app.</summary>
public sealed class AppSettings : IAppSettings
{
    private readonly ApplicationDataContainer _values = ApplicationData.Current.LocalSettings;

    /// <summary>Default folder for the database and media: user profile/Downloads/MyTwitterBackup.</summary>
    public static string DefaultFolder
    {
        get
        {
            string downloads;
            try
            {
                downloads = UserDataPaths.GetDefault().Downloads;
            }
            catch (Exception)
            {
                downloads = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            }

            return Path.Combine(downloads, "MyTwitterBackup");
        }
    }

    public string DownloadsFolder
    {
        get => Get(nameof(DownloadsFolder), DefaultFolder);
        set => _values.Values[nameof(DownloadsFolder)] = value;
    }


    public bool SaveRawResponses
    {
        get => _values.Values[nameof(SaveRawResponses)] is bool b && b;
        set => _values.Values[nameof(SaveRawResponses)] = value;
    }

    public bool AutoPlayVideos
    {
        get => _values.Values[nameof(AutoPlayVideos)] is not bool b || b;
        set => _values.Values[nameof(AutoPlayVideos)] = value;
    }

    public string AppDataFolder => ApplicationData.Current.LocalFolder.Path;

    public static string GetLogsFolder(IAppSettings settings) => Path.Combine(settings.AppDataFolder, "logs");

    public static string GetResponsesFolder(IAppSettings settings) => Path.Combine(settings.AppDataFolder, "responses");

    private string Get(string key, string defaultValue) =>
        _values.Values[key] is string s && !string.IsNullOrWhiteSpace(s) ? s : defaultValue;
}
