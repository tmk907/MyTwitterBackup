namespace MyTwitterBackup.Core.Models;

public enum QueueStatus
{
    Pending = 0,
    Downloading = 1,
    Completed = 2,
    Failed = 3,

    /// <summary>The post data (text, media urls) was not captured from x.com yet.</summary>
    MissingData = 4,
}
