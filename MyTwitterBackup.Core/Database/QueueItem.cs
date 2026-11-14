using MyTwitterBackup.Core.Models;

namespace MyTwitterBackup.Core.Database;

/// <summary>Persistent download queue entry. Unique per (PostId, SourceType).</summary>
public class QueueItem
{
    public int Id { get; set; }
    public required string PostId { get; set; }
    public SourceType SourceType { get; set; }
    public QueueStatus Status { get; set; }
    public string? UserName { get; set; }
    public string? PreviewText { get; set; }

    /// <summary>Captured <see cref="TweetData"/> as JSON. Null when the data was not captured.</summary>
    public string? TweetJson { get; set; }

    public string? Error { get; set; }
    public int Attempts { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public string Url => string.IsNullOrEmpty(UserName)
        ? $"https://x.com/i/status/{PostId}"
        : $"https://x.com/{UserName}/status/{PostId}";
}
