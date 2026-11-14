namespace MyTwitterBackup.Core.Services;

public interface IAppSettings
{
    /// <summary>Root folder for media: {DownloadsFolder}/{userId}/{postId}/.</summary>
    string DownloadsFolder { get; set; }

    /// <summary>Folder that contains the SQLite database file.</summary>
    string DatabaseFolder { get; set; }

    /// <summary>Save captured GraphQL responses to disk (diagnostics).</summary>
    bool SaveRawResponses { get; set; }

    /// <summary>App private data folder (logs, diagnostics).</summary>
    string AppDataFolder { get; }
}
