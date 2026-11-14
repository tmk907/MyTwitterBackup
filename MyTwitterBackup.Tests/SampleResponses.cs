namespace MyTwitterBackup.Tests;

/// <summary>Trimmed-down GraphQL responses in the shape x.com returns them.</summary>
internal static class SampleResponses
{
    public const string BookmarksUrl = "https://x.com/i/api/graphql/abc123/Bookmarks?variables=%7B%7D";
    public const string TweetDetailUrl = "https://x.com/i/api/graphql/xyz/TweetDetail?variables=%7B%7D";

    public const string Bookmarks = """
    {
      "data": { "bookmark_timeline_v2": { "timeline": { "instructions": [ { "type": "TimelineAddEntries", "entries": [
        { "entryId": "tweet-100", "content": { "entryType": "TimelineTimelineItem", "itemContent": { "itemType": "TimelineTweet",
          "tweet_results": { "result": {
            "__typename": "Tweet",
            "rest_id": "100",
            "core": { "user_results": { "result": { "__typename": "User", "rest_id": "11",
              "core": { "name": "Alice A", "screen_name": "alice" }, "legacy": {} } } },
            "views": { "count": "1234", "state": "EnabledWithCount" },
            "legacy": {
              "created_at": "Wed Oct 10 20:19:24 +0000 2018",
              "full_text": "Hello &amp; welcome https://t.co/link https://t.co/media",
              "favorite_count": 5, "retweet_count": 2, "reply_count": 1, "quote_count": 0, "bookmark_count": 3,
              "lang": "en", "user_id_str": "11", "id_str": "100", "conversation_id_str": "100",
              "entities": {
                "urls": [ { "url": "https://t.co/link", "expanded_url": "https://example.com/page" } ],
                "media": [ { "url": "https://t.co/media", "id_str": "900", "type": "photo", "media_url_https": "https://pbs.twimg.com/media/AAA.jpg" } ]
              },
              "extended_entities": { "media": [
                { "url": "https://t.co/media", "id_str": "900", "media_key": "3_900", "type": "photo",
                  "media_url_https": "https://pbs.twimg.com/media/AAA.jpg", "original_info": { "width": 1200, "height": 800 } },
                { "url": "https://t.co/media", "id_str": "901", "media_key": "7_901", "type": "video",
                  "media_url_https": "https://pbs.twimg.com/ext_tw_video_thumb/901/pu/img/BBB.jpg",
                  "original_info": { "width": 1280, "height": 720 },
                  "video_info": { "duration_millis": 15000, "variants": [
                    { "content_type": "application/x-mpegURL", "url": "https://video.twimg.com/ext_tw_video/901/pu/pl/x.m3u8" },
                    { "bitrate": 832000, "content_type": "video/mp4", "url": "https://video.twimg.com/ext_tw_video/901/pu/vid/640x360/low.mp4" },
                    { "bitrate": 2176000, "content_type": "video/mp4", "url": "https://video.twimg.com/ext_tw_video/901/pu/vid/1280x720/high.mp4" }
                  ] } }
              ] }
            }
          } } } } },
        { "entryId": "tweet-200", "content": { "entryType": "TimelineTimelineItem", "itemContent": { "itemType": "TimelineTweet",
          "tweet_results": { "result": {
            "__typename": "TweetWithVisibilityResults",
            "tweet": {
              "rest_id": "200",
              "core": { "user_results": { "result": { "__typename": "User", "rest_id": "22",
                "legacy": { "name": "Bob", "screen_name": "bob" } } } },
              "note_tweet": { "is_expandable": true, "note_tweet_results": { "result": {
                "text": "A very long post with a link https://t.co/long and more text",
                "entity_set": { "urls": [ { "url": "https://t.co/long", "expanded_url": "https://long.example.com" } ] } } } },
              "quoted_status_result": { "result": {
                "__typename": "Tweet",
                "rest_id": "300",
                "core": { "user_results": { "result": { "__typename": "User", "rest_id": "33",
                  "core": { "name": "Carol", "screen_name": "carol" } } } },
                "legacy": {
                  "created_at": "Mon Jan 01 10:00:00 +0000 2024",
                  "full_text": "Quoted post",
                  "favorite_count": 10, "user_id_str": "33", "id_str": "300",
                  "extended_entities": { "media": [
                    { "id_str": "950", "type": "animated_gif", "media_url_https": "https://pbs.twimg.com/tweet_video_thumb/GIF.jpg",
                      "video_info": { "variants": [ { "bitrate": 0, "content_type": "video/mp4", "url": "https://video.twimg.com/tweet_video/GIF.mp4" } ] } }
                  ] }
                }
              } },
              "legacy": {
                "created_at": "Tue Feb 02 12:00:00 +0000 2021",
                "full_text": "A very long post with a link https://t.co/long and mo...",
                "favorite_count": 7, "user_id_str": "22", "id_str": "200",
                "is_quote_status": true,
                "quoted_status_permalink": { "url": "https://t.co/quote" }
              }
            }
          } } } } },
        { "entryId": "tweet-400", "content": { "entryType": "TimelineTimelineItem", "itemContent": { "itemType": "TimelineTweet",
          "tweet_results": { "result": {
            "__typename": "Tweet",
            "rest_id": "400",
            "core": { "user_results": { "result": { "__typename": "User", "rest_id": "44", "legacy": { "screen_name": "dave", "name": "Dave" } } } },
            "legacy": {
              "created_at": "Tue Feb 02 12:00:00 +0000 2021",
              "full_text": "RT @erin: original text",
              "user_id_str": "44", "id_str": "400",
              "retweeted_status_result": { "result": {
                "__typename": "Tweet",
                "rest_id": "500",
                "core": { "user_results": { "result": { "__typename": "User", "rest_id": "55", "legacy": { "screen_name": "erin", "name": "Erin" } } } },
                "legacy": { "created_at": "Tue Feb 02 11:00:00 +0000 2021", "full_text": "original text", "favorite_count": 99, "user_id_str": "55", "id_str": "500" }
              } }
            }
          } } } } },
        { "entryId": "tweet-600", "content": { "itemContent": { "tweet_results": { "result": { "__typename": "TweetTombstone" } } } } },
        { "entryId": "cursor-bottom-1", "content": { "entryType": "TimelineTimelineCursor", "value": "abc", "cursorType": "Bottom" } }
      ] } ] } } }
    }
    """;
}
