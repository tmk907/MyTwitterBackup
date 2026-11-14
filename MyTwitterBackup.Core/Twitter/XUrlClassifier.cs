using System.Text.RegularExpressions;
using MyTwitterBackup.Core.Models;

namespace MyTwitterBackup.Core.Twitter;

/// <summary>Recognizes x.com / twitter.com pages that the app can download from.</summary>
public static partial class XUrlClassifier
{
    private static readonly HashSet<string> s_hosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "x.com", "www.x.com", "mobile.x.com",
        "twitter.com", "www.twitter.com", "mobile.twitter.com",
    };

    // Top level paths that are not user names.
    private static readonly HashSet<string> s_reservedPaths = new(StringComparer.OrdinalIgnoreCase)
    {
        "i", "home", "explore", "notifications", "messages", "search", "settings", "compose",
        "login", "logout", "signup", "tos", "privacy", "jobs", "hashtag", "intent", "share",
    };

    [GeneratedRegex(@"^/(?<user>[A-Za-z0-9_]{1,20})/status(?:es)?/(?<id>\d+)(?:/.*)?$")]
    private static partial Regex UserStatusRegex();

    [GeneratedRegex(@"^/i/(?:web/)?status/(?<id>\d+)(?:/.*)?$")]
    private static partial Regex AnonymousStatusRegex();

    [GeneratedRegex(@"^/i/bookmarks(?:/(?<folder>\d+))?/?$")]
    private static partial Regex BookmarksRegex();

    [GeneratedRegex(@"^/i/history/?$")]
    private static partial Regex HistoryRegex();

    [GeneratedRegex(@"^/i/history/likes/?$")]
    private static partial Regex HistoryLikesRegex();

    [GeneratedRegex(@"^/(?<user>[A-Za-z0-9_]{1,20})/likes/?$")]
    private static partial Regex UserLikesRegex();

    public static bool IsXHost(Uri uri) => s_hosts.Contains(uri.Host);

    public static PageInfo Classify(string? url)
    {
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return PageInfo.None;
        }

        return Classify(uri);
    }

    public static PageInfo Classify(Uri uri)
    {
        if (!IsXHost(uri))
        {
            return PageInfo.None;
        }

        var path = uri.AbsolutePath;

        var match = BookmarksRegex().Match(path);
        if (match.Success)
        {
            var folder = match.Groups["folder"].Success ? match.Groups["folder"].Value : string.Empty;
            return new PageInfo(PageMode.Bookmarks, $"bookmarks:{folder}");
        }

        if (HistoryRegex().IsMatch(path))
        {
            return new PageInfo(PageMode.Bookmarks, "history:bookmarks");
        }

        if (HistoryLikesRegex().IsMatch(path))
        {
            return new PageInfo(PageMode.Likes, "history:likes");
        }

        match = AnonymousStatusRegex().Match(path);
        if (match.Success)
        {
            var id = match.Groups["id"].Value;
            return new PageInfo(PageMode.Single, $"single:{id}", id);
        }

        match = UserStatusRegex().Match(path);
        if (match.Success && !s_reservedPaths.Contains(match.Groups["user"].Value))
        {
            var id = match.Groups["id"].Value;
            return new PageInfo(PageMode.Single, $"single:{id}", id, match.Groups["user"].Value);
        }

        match = UserLikesRegex().Match(path);
        if (match.Success && !s_reservedPaths.Contains(match.Groups["user"].Value))
        {
            var user = match.Groups["user"].Value;
            return new PageInfo(PageMode.Likes, $"likes:{user.ToLowerInvariant()}", UserName: user);
        }

        return PageInfo.None;
    }

    /// <summary>Extracts (user, postId) from a status link, e.g. "/jack/status/20".</summary>
    public static (string? UserName, string? PostId) ParseStatusLink(string? href)
    {
        if (string.IsNullOrEmpty(href))
        {
            return (null, null);
        }

        var path = Uri.TryCreate(href, UriKind.Absolute, out var abs) ? abs.AbsolutePath : href;
        var match = UserStatusRegex().Match(path);
        if (match.Success)
        {
            return (match.Groups["user"].Value, match.Groups["id"].Value);
        }

        match = AnonymousStatusRegex().Match(path);
        return match.Success ? (null, match.Groups["id"].Value) : (null, null);
    }
}
