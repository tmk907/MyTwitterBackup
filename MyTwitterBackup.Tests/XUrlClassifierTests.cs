using MyTwitterBackup.Core.Models;
using MyTwitterBackup.Core.Twitter;

namespace MyTwitterBackup.Tests;

public class XUrlClassifierTests
{
    [Theory]
    [InlineData("https://x.com/i/bookmarks", PageMode.Bookmarks)]
    [InlineData("https://x.com/i/bookmarks/1234567", PageMode.Bookmarks)]
    [InlineData("https://x.com/i/history", PageMode.Bookmarks)]
    [InlineData("https://x.com/i/history/likes", PageMode.Likes)]
    [InlineData("https://x.com/jack/likes", PageMode.Likes)]
    [InlineData("https://twitter.com/jack/likes", PageMode.Likes)]
    [InlineData("https://x.com/jack/status/20", PageMode.Single)]
    [InlineData("https://x.com/jack/status/20/photo/1", PageMode.Single)]
    [InlineData("https://mobile.twitter.com/jack/status/20/video/1", PageMode.Single)]
    [InlineData("https://x.com/i/web/status/20", PageMode.Single)]
    [InlineData("https://x.com/home", PageMode.None)]
    [InlineData("https://x.com/jack", PageMode.None)]
    [InlineData("https://x.com/i/likes", PageMode.None)]
    [InlineData("https://example.com/jack/status/20", PageMode.None)]
    [InlineData("not a url", PageMode.None)]
    public void Classify_DetectsMode(string url, PageMode expected)
    {
        Assert.Equal(expected, XUrlClassifier.Classify(url).Mode);
    }

    [Fact]
    public void Classify_SingleReturnsPostIdAndUser()
    {
        var info = XUrlClassifier.Classify("https://x.com/jack/status/20?s=20");
        Assert.Equal("20", info.PostId);
        Assert.Equal("jack", info.UserName);
        Assert.Equal(SourceType.Single, info.SourceType);
    }

    [Fact]
    public void ParseStatusLink_ReadsRelativeLinks()
    {
        Assert.Equal(("jack", "20"), XUrlClassifier.ParseStatusLink("/jack/status/20"));
    }
}
