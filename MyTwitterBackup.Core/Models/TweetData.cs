using System.Text.Json.Serialization;

namespace MyTwitterBackup.Core.Models;

public enum MediaType
{
    Photo,
    Video,
    AnimatedGif,
}

/// <summary>
/// Normalized post data captured from x.com GraphQL responses. It is serialized as JSON
/// and stored with the queue item, so media can be downloaded after an app restart
/// without opening the page again.
/// </summary>
public sealed class TweetData
{
    public required string Id { get; init; }
    public required string UserId { get; init; }
    public required string UserName { get; init; }
    public string DisplayName { get; init; } = string.Empty;
    public DateTime CreatedAtUtc { get; init; }
    public string Text { get; init; } = string.Empty;
    public string? Lang { get; init; }
    public long LikeCount { get; init; }
    public long RetweetCount { get; init; }
    public long ReplyCount { get; init; }
    public long QuoteCount { get; init; }
    public long BookmarkCount { get; init; }
    public long? ViewCount { get; init; }
    public string? ConversationId { get; init; }
    public string? InReplyToPostId { get; init; }
    public List<TweetMedia> Media { get; init; } = [];
    public TweetData? QuotedTweet { get; init; }

    [JsonIgnore]
    public string Url => $"https://x.com/{UserName}/status/{Id}";
}

public sealed class TweetMedia
{
    public required string Id { get; init; }
    public MediaType Type { get; init; }

    /// <summary>Url of the file to download: original quality photo or best mp4 variant.</summary>
    public required string Url { get; init; }

    /// <summary>Poster image for videos and gifs (null for photos).</summary>
    public string? PreviewUrl { get; init; }

    public int Width { get; init; }
    public int Height { get; init; }
    public long? DurationMs { get; init; }
    public long? Bitrate { get; init; }
}
