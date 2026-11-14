namespace MyTwitterBackup.Core.Models;

public enum PageMode
{
    None,
    Single,
    Bookmarks,
    Likes,
}

/// <summary>Result of classifying the current WebView url.</summary>
/// <param name="Mode">Detected download mode.</param>
/// <param name="PageKey">Stable key of the page (used to keep detected posts per page).</param>
/// <param name="PostId">Post id for <see cref="PageMode.Single"/>.</param>
/// <param name="UserName">User name from the url, when present.</param>
public sealed record PageInfo(PageMode Mode, string PageKey, string? PostId = null, string? UserName = null)
{
    public static PageInfo None { get; } = new(PageMode.None, string.Empty);

    public SourceType? SourceType => Mode switch
    {
        PageMode.Single => Models.SourceType.Single,
        PageMode.Bookmarks => Models.SourceType.Bookmark,
        PageMode.Likes => Models.SourceType.Like,
        _ => null,
    };
}
