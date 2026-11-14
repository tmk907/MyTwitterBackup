using MyTwitterBackup.Core.Models;

namespace MyTwitterBackup.Core.Database;

/// <summary>A downloaded post. One record per post id, regardless of how many sources it has.</summary>
public class Post
{
    /// <summary>Post id (tweet rest_id).</summary>
    public required string Id { get; set; }
    public required string UserId { get; set; }
    public required string UserName { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public string Text { get; set; } = string.Empty;
    public string? Lang { get; set; }
    public long LikeCount { get; set; }
    public long RetweetCount { get; set; }
    public long ReplyCount { get; set; }
    public long QuoteCount { get; set; }
    public long BookmarkCount { get; set; }
    public long? ViewCount { get; set; }
    public string? ConversationId { get; set; }
    public string? InReplyToPostId { get; set; }

    /// <summary>Id of the quoted post (stored as its own <see cref="Post"/> record).</summary>
    public string? QuotedPostId { get; set; }
    public Post? QuotedPost { get; set; }

    public DateTime DownloadedAt { get; set; }

    public List<PostMedia> Media { get; set; } = [];
    public List<PostSource> Sources { get; set; } = [];

    public string Url => $"https://x.com/{UserName}/status/{Id}";
}

public class PostMedia
{
    public int Id { get; set; }
    public required string PostId { get; set; }
    public int Index { get; set; }
    public required string MediaId { get; set; }
    public MediaType Type { get; set; }
    public required string RemoteUrl { get; set; }
    public string? PreviewRemoteUrl { get; set; }

    /// <summary>Path relative to the downloads folder: {userId}/{postId}/{file}.</summary>
    public string? LocalPath { get; set; }

    /// <summary>Poster image path relative to the downloads folder (videos and gifs).</summary>
    public string? PreviewLocalPath { get; set; }

    public int Width { get; set; }
    public int Height { get; set; }
    public long? DurationMs { get; set; }
}

/// <summary>Download type of a post. A post can have many: Bookmark, Like and Single.</summary>
public class PostSource
{
    public required string PostId { get; set; }
    public SourceType SourceType { get; set; }
    public DateTime AddedAt { get; set; }
}
