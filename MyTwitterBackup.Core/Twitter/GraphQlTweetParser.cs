using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using MyTwitterBackup.Core.Models;

namespace MyTwitterBackup.Core.Twitter;

public sealed class GraphQlParseResult
{
    public string OperationName { get; init; } = string.Empty;

    /// <summary>Posts that are entries of the response (timeline items, focal post, replies). Quoted posts are not included.</summary>
    public List<TweetData> TopLevel { get; } = [];

    /// <summary>All parsed posts including nested quoted posts.</summary>
    public List<TweetData> All { get; } = [];
}

/// <summary>
/// Parses x.com GraphQL responses (TweetDetail, TweetResultByRestId, Bookmarks, Likes, timelines...).
/// Instead of depending on the exact response shape of each operation, it walks the JSON tree and
/// picks every object that looks like a tweet result (__typename "Tweet" / "TweetWithVisibilityResults").
/// </summary>
public static partial class GraphQlTweetParser
{
    private const int MaxDepth = 128;

    // Logged in: https://x.com/i/api/graphql/{queryId}/{Operation}
    // Logged out: https://api.x.com/graphql/{queryId}/{Operation}
    [GeneratedRegex(@"^https://(?:[a-z0-9-]+\.)?(?:x|twitter)\.com/(?:i/api/)?graphql/[^/?]+/(?<op>[A-Za-z0-9_]+)", RegexOptions.IgnoreCase)]
    private static partial Regex OperationRegex();

    public static bool IsGraphQlUrl(string? url) => url is not null && OperationRegex().IsMatch(url);

    public static string GetOperationName(string? url)
    {
        if (url is null)
        {
            return string.Empty;
        }

        var match = OperationRegex().Match(url);
        return match.Success ? match.Groups["op"].Value : string.Empty;
    }

    /// <summary>Operations whose entries are the user's bookmarks (also the unknown "history" variants).</summary>
    public static bool IsBookmarksOperation(string operation) =>
        operation.Contains("Bookmark", StringComparison.OrdinalIgnoreCase)
        || operation.Contains("History", StringComparison.OrdinalIgnoreCase);

    /// <summary>Operations whose entries are liked posts (also the unknown "history" variants).</summary>
    public static bool IsLikesOperation(string operation) =>
        operation.Contains("Like", StringComparison.OrdinalIgnoreCase)
        || operation.Contains("History", StringComparison.OrdinalIgnoreCase);

    public static GraphQlParseResult Parse(string url, string json)
    {
        var result = new GraphQlParseResult { OperationName = GetOperationName(url) };
        using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 256 });
        Walk(doc.RootElement, result, 0);
        return result;
    }

    private static void Walk(JsonElement element, GraphQlParseResult result, int depth)
    {
        if (depth > MaxDepth)
        {
            return;
        }

        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                if (IsTweetResult(element))
                {
                    var tweet = ParseTweetResult(element, result.All, 0);
                    if (tweet is not null && !result.TopLevel.Exists(t => t.Id == tweet.Id))
                    {
                        result.TopLevel.Add(tweet);
                    }

                    return;
                }

                foreach (var property in element.EnumerateObject())
                {
                    Walk(property.Value, result, depth + 1);
                }

                break;

            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    Walk(item, result, depth + 1);
                }

                break;
        }
    }

    private static bool IsTweetResult(JsonElement element)
    {
        var typeName = GetString(element, "__typename");
        if (typeName is not null)
        {
            return typeName switch
            {
                "Tweet" => element.TryGetProperty("legacy", out _),
                "TweetWithVisibilityResults" => element.TryGetProperty("tweet", out _),
                _ => false,
            };
        }

        // Some responses omit __typename.
        return element.TryGetProperty("rest_id", out _)
            && Get(element, "legacy", "full_text") is not null
            && Get(element, "core", "user_results") is not null;
    }

    private static TweetData? ParseTweetResult(JsonElement element, List<TweetData> all, int depth)
    {
        if (depth > 4)
        {
            return null;
        }

        // TweetWithVisibilityResults wraps the real tweet.
        if (GetString(element, "__typename") == "TweetWithVisibilityResults"
            && element.TryGetProperty("tweet", out var inner))
        {
            element = inner;
        }

        var typeName = GetString(element, "__typename");
        if (typeName is not null and not "Tweet")
        {
            return null; // TweetTombstone, TweetUnavailable...
        }

        if (!element.TryGetProperty("legacy", out var legacy) || legacy.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        // Retweet: keep the original post.
        if (Get(legacy, "retweeted_status_result", "result") is { } retweeted)
        {
            return ParseTweetResult(retweeted, all, depth + 1);
        }

        var id = GetString(element, "rest_id") ?? GetString(legacy, "id_str");
        if (string.IsNullOrEmpty(id))
        {
            return null;
        }

        var user = Get(element, "core", "user_results", "result");
        var userId = (user is { } u1 ? GetString(u1, "rest_id") : null) ?? GetString(legacy, "user_id_str") ?? string.Empty;
        var userName = user is { } u2
            ? GetString(u2, "core", "screen_name") ?? GetString(u2, "legacy", "screen_name")
            : null;
        var displayName = user is { } u3
            ? GetString(u3, "core", "name") ?? GetString(u3, "legacy", "name")
            : null;

        TweetData? quoted = null;
        if (Get(element, "quoted_status_result", "result") is { } quotedElement)
        {
            quoted = ParseTweetResult(quotedElement, all, depth + 1);
        }

        var tweet = new TweetData
        {
            Id = id,
            UserId = userId,
            UserName = userName ?? string.Empty,
            DisplayName = displayName ?? string.Empty,
            CreatedAtUtc = ParseDate(GetString(legacy, "created_at")),
            Text = BuildText(element, legacy),
            Lang = GetString(legacy, "lang"),
            LikeCount = GetInt64(legacy, "favorite_count") ?? 0,
            RetweetCount = GetInt64(legacy, "retweet_count") ?? 0,
            ReplyCount = GetInt64(legacy, "reply_count") ?? 0,
            QuoteCount = GetInt64(legacy, "quote_count") ?? 0,
            BookmarkCount = GetInt64(legacy, "bookmark_count") ?? 0,
            ViewCount = Get(element, "views") is { } views ? GetInt64(views, "count") : null,
            ConversationId = GetString(legacy, "conversation_id_str"),
            InReplyToPostId = GetString(legacy, "in_reply_to_status_id_str"),
            Media = ParseMedia(legacy),
            QuotedTweet = quoted,
        };

        all.Add(tweet);
        return tweet;
    }

    private static string BuildText(JsonElement tweet, JsonElement legacy)
    {
        // Long posts: the full text is in note_tweet, legacy.full_text is truncated.
        if (Get(tweet, "note_tweet", "note_tweet_results", "result") is { } note
            && GetString(note, "text") is { } noteText)
        {
            return ExpandUrls(noteText, Get(note, "entity_set", "urls"), [], htmlDecode: false);
        }

        var text = GetString(legacy, "full_text") ?? GetString(legacy, "text") ?? string.Empty;

        var remove = new List<string>();
        if (Get(legacy, "entities", "media") is { ValueKind: JsonValueKind.Array } media)
        {
            foreach (var m in media.EnumerateArray())
            {
                if (GetString(m, "url") is { } mediaUrl)
                {
                    remove.Add(mediaUrl);
                }
            }
        }

        if (GetString(legacy, "quoted_status_permalink", "url") is { } quoteUrl)
        {
            remove.Add(quoteUrl);
        }

        return ExpandUrls(text, Get(legacy, "entities", "urls"), remove, htmlDecode: true);
    }

    private static string ExpandUrls(string text, JsonElement? urls, List<string> remove, bool htmlDecode)
    {
        foreach (var r in remove)
        {
            text = text.Replace(r, string.Empty, StringComparison.Ordinal);
        }

        if (urls is { ValueKind: JsonValueKind.Array } list)
        {
            foreach (var u in list.EnumerateArray())
            {
                var shortUrl = GetString(u, "url");
                var expanded = GetString(u, "expanded_url");
                if (!string.IsNullOrEmpty(shortUrl) && !string.IsNullOrEmpty(expanded))
                {
                    text = text.Replace(shortUrl, expanded, StringComparison.Ordinal);
                }
            }
        }

        if (htmlDecode)
        {
            text = WebUtility.HtmlDecode(text);
        }

        return text.Trim();
    }

    private static List<TweetMedia> ParseMedia(JsonElement legacy)
    {
        var list = new List<TweetMedia>();
        var media = Get(legacy, "extended_entities", "media") ?? Get(legacy, "entities", "media");
        if (media is not { ValueKind: JsonValueKind.Array } array)
        {
            return list;
        }

        foreach (var m in array.EnumerateArray())
        {
            var mediaId = GetString(m, "id_str") ?? GetString(m, "media_key");
            var mediaUrl = GetString(m, "media_url_https") ?? GetString(m, "media_url");
            if (mediaId is null || mediaUrl is null || list.Exists(x => x.Id == mediaId))
            {
                continue;
            }

            var width = (int)(GetInt64(m, "original_info", "width") ?? 0);
            var height = (int)(GetInt64(m, "original_info", "height") ?? 0);
            var type = GetString(m, "type");

            if (type is "video" or "animated_gif")
            {
                var best = BestMp4Variant(m);
                if (best is null)
                {
                    continue; // only HLS available, nothing to download as a single file
                }

                list.Add(new TweetMedia
                {
                    Id = mediaId,
                    Type = type == "video" ? MediaType.Video : MediaType.AnimatedGif,
                    Url = best.Value.Url,
                    Bitrate = best.Value.Bitrate,
                    PreviewUrl = mediaUrl,
                    Width = width,
                    Height = height,
                    DurationMs = GetInt64(m, "video_info", "duration_millis"),
                });
            }
            else
            {
                list.Add(new TweetMedia
                {
                    Id = mediaId,
                    Type = MediaType.Photo,
                    Url = ToOriginalPhotoUrl(mediaUrl),
                    Width = width,
                    Height = height,
                });
            }
        }

        return list;
    }

    private static (string Url, long Bitrate)? BestMp4Variant(JsonElement media)
    {
        if (Get(media, "video_info", "variants") is not { ValueKind: JsonValueKind.Array } variants)
        {
            return null;
        }

        (string Url, long Bitrate)? best = null;
        foreach (var v in variants.EnumerateArray())
        {
            var contentType = GetString(v, "content_type");
            var url = GetString(v, "url");
            if (url is null || !string.Equals(contentType, "video/mp4", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var bitrate = GetInt64(v, "bitrate") ?? 0;
            if (best is null || bitrate > best.Value.Bitrate)
            {
                best = (url, bitrate);
            }
        }

        return best;
    }

    /// <summary>https://pbs.twimg.com/media/abc.jpg -> https://pbs.twimg.com/media/abc.jpg?name=orig</summary>
    public static string ToOriginalPhotoUrl(string url)
    {
        var queryIndex = url.IndexOf('?');
        if (queryIndex < 0)
        {
            return url + "?name=orig";
        }

        var basePart = url[..queryIndex];
        var query = url[(queryIndex + 1)..]
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Where(p => !p.StartsWith("name=", StringComparison.OrdinalIgnoreCase))
            .Append("name=orig");
        return basePart + "?" + string.Join('&', query);
    }

    private static DateTime ParseDate(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return DateTime.MinValue;
        }

        // "Wed Oct 10 20:19:24 +0000 2018"
        if (DateTime.TryParseExact(value, "ddd MMM dd HH:mm:ss '+0000' yyyy", CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var date))
        {
            return date;
        }

        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var dto)
            ? dto.UtcDateTime
            : DateTime.MinValue;
    }

    private static JsonElement? Get(JsonElement element, params string[] path)
    {
        var current = element;
        foreach (var name in path)
        {
            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(name, out current))
            {
                return null;
            }
        }

        return current.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined ? null : current;
    }

    private static string? GetString(JsonElement element, params string[] path) =>
        Get(element, path) is { ValueKind: JsonValueKind.String } value ? value.GetString() : null;

    private static long? GetInt64(JsonElement element, params string[] path)
    {
        return Get(element, path) switch
        {
            { ValueKind: JsonValueKind.Number } n when n.TryGetInt64(out var l) => l,
            { ValueKind: JsonValueKind.String } s when long.TryParse(s.GetString(), out var l) => l,
            _ => null,
        };
    }
}
