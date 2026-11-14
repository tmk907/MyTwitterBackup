namespace MyTwitterBackup.Core.Services;

public interface IAppSettings
{
    /// <summary>
    /// Folder for all app data: the SQLite database and media in {DownloadsFolder}/data/{userId}/{postId}/.
    /// </summary>
    string DownloadsFolder { get; set; }

    /// <summary>Save captured GraphQL responses to disk (diagnostics).</summary>
    bool SaveRawResponses { get; set; }

    /// <summary>Start playing videos when a downloaded post is previewed.</summary>
    bool AutoPlayVideos { get; set; }

    /// <summary>App private data folder (logs, diagnostics).</summary>
    string AppDataFolder { get; }
}
