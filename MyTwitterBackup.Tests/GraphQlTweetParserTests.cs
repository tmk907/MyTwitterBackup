using MyTwitterBackup.Core.Models;
using MyTwitterBackup.Core.Twitter;

namespace MyTwitterBackup.Tests;

public class GraphQlTweetParserTests
{
    private readonly GraphQlParseResult _result = GraphQlTweetParser.Parse(SampleResponses.BookmarksUrl, SampleResponses.Bookmarks);

    [Fact]
    public void Parse_ReadsOperationName()
    {
        Assert.Equal("Bookmarks", _result.OperationName);
        Assert.Equal("TweetDetail", GraphQlTweetParser.GetOperationName(SampleResponses.TweetDetailUrl));
    }

    [Theory]
    [InlineData("https://x.com/i/api/graphql/abc/TweetDetail?variables=1", "TweetDetail")]
    [InlineData("https://api.x.com/graphql/abc/TweetResultByRestId?variables=1", "TweetResultByRestId")]
    [InlineData("https://twitter.com/i/api/graphql/abc/Likes", "Likes")]
    [InlineData("https://example.com/graphql/abc/Likes", "")]
    [InlineData("https://x.com/i/api/2/badge_count", "")]
    public void GetOperationName_RecognizesGraphQlUrls(string url, string expected)
    {
        Assert.Equal(expected, GraphQlTweetParser.GetOperationName(url));
        Assert.Equal(expected.Length > 0, GraphQlTweetParser.IsGraphQlUrl(url));
    }

    [Fact]
    public void Parse_TopLevelContainsEntriesButNotQuotedPosts()
    {
        Assert.Equal(["100", "200", "500"], _result.TopLevel.Select(t => t.Id));
        Assert.Contains(_result.All, t => t.Id == "300");
        Assert.DoesNotContain(_result.All, t => t.Id == "400");
    }

    [Fact]
    public void Parse_ReadsMetadataAndExpandsText()
    {
        var tweet = _result.TopLevel.Single(t => t.Id == "100");
        Assert.Equal("11", tweet.UserId);
        Assert.Equal("alice", tweet.UserName);
        Assert.Equal("Alice A", tweet.DisplayName);
        Assert.Equal(new DateTime(2018, 10, 10, 20, 19, 24, DateTimeKind.Utc), tweet.CreatedAtUtc);
        Assert.Equal(5, tweet.LikeCount);
        Assert.Equal(3, tweet.BookmarkCount);
        Assert.Equal(1234, tweet.ViewCount);
        Assert.Equal("Hello & welcome https://example.com/page", tweet.Text);
    }

    [Fact]
    public void Parse_PicksOriginalPhotoAndBestVideoVariant()
    {
        var media = _result.TopLevel.Single(t => t.Id == "100").Media;
        Assert.Equal(2, media.Count);
        Assert.Equal(MediaType.Photo, media[0].Type);
        Assert.Equal("https://pbs.twimg.com/media/AAA.jpg?name=orig", media[0].Url);
        Assert.Equal(MediaType.Video, media[1].Type);
        Assert.EndsWith("high.mp4", media[1].Url);
        Assert.Equal(15000, media[1].DurationMs);
        Assert.NotNull(media[1].PreviewUrl);
    }

    [Fact]
    public void Parse_UsesNoteTweetTextAndQuotedPost()
    {
        var tweet = _result.TopLevel.Single(t => t.Id == "200");
        Assert.Equal("bob", tweet.UserName);
        Assert.Equal("A very long post with a link https://long.example.com and more text", tweet.Text);
        Assert.NotNull(tweet.QuotedTweet);
        Assert.Equal("300", tweet.QuotedTweet!.Id);
        Assert.Equal("carol", tweet.QuotedTweet.UserName);
        Assert.Equal(MediaType.AnimatedGif, tweet.QuotedTweet.Media.Single().Type);
    }

    [Fact]
    public void Parse_RetweetResolvesToOriginal()
    {
        var tweet = _result.TopLevel.Single(t => t.Id == "500");
        Assert.Equal("erin", tweet.UserName);
        Assert.Equal(99, tweet.LikeCount);
    }

    [Theory]
    [InlineData("https://pbs.twimg.com/media/X.jpg", "https://pbs.twimg.com/media/X.jpg?name=orig")]
    [InlineData("https://pbs.twimg.com/media/X?format=png&name=small", "https://pbs.twimg.com/media/X?format=png&name=orig")]
    public void ToOriginalPhotoUrl_SetsNameOrig(string input, string expected)
    {
        Assert.Equal(expected, GraphQlTweetParser.ToOriginalPhotoUrl(input));
    }
}
