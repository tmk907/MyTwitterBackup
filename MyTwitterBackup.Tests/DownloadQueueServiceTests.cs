using System.Net;
using Microsoft.EntityFrameworkCore;
using MyTwitterBackup.Core.Database;
using MyTwitterBackup.Core.Models;
using MyTwitterBackup.Core.Services;
using MyTwitterBackup.Core.Twitter;

namespace MyTwitterBackup.Tests;

public sealed class DownloadQueueServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "MyTwitterBackupTests", Guid.NewGuid().ToString("N"));
    private readonly AppDbContextFactory _factory;
    private readonly FakeHandler _handler = new();
    private readonly TweetCache _cache = new();
    private readonly DownloadQueueService _queue;

    public DownloadQueueServiceTests()
    {
        var settings = new TestSettings(_root);
        _factory = new AppDbContextFactory(settings);
        new DatabaseInitializer(_factory).EnsureCreated();
        _queue = new DownloadQueueService(_factory, settings, new MediaDownloader(_handler), _cache);
    }

    [Fact]
    public async Task Queue_DownloadsPostQuotedPostAndMedia_Once()
    {
        var parsed = GraphQlTweetParser.Parse(SampleResponses.BookmarksUrl, SampleResponses.Bookmarks);
        var quoting = parsed.TopLevel.Single(t => t.Id == "200");
        var withMedia = parsed.TopLevel.Single(t => t.Id == "100");

        await _queue.EnqueueAsync([
            new EnqueueRequest(quoting.Id, SourceType.Bookmark, quoting.UserName, quoting),
            new EnqueueRequest(withMedia.Id, SourceType.Bookmark, withMedia.UserName, withMedia),
            new EnqueueRequest(withMedia.Id, SourceType.Like, withMedia.UserName, withMedia),
        ]);
        _queue.Start();
        await WaitUntilAsync(async () => (await _queue.GetCountsAsync()).Completed == 3);

        await using var db = _factory.Create();
        var post = await db.Posts.Include(p => p.Sources).SingleAsync(p => p.Id == "100");
        Assert.Equal(2, post.Sources.Count);
        Assert.Equal("300", (await db.Posts.SingleAsync(p => p.Id == "200")).QuotedPostId);
        Assert.True(await db.Posts.AnyAsync(p => p.Id == "300"));
        Assert.True(File.Exists(Path.Combine(_root, "downloads", "11", "100", "01_900.jpg")));
        Assert.True(File.Exists(Path.Combine(_root, "downloads", "11", "100", "02_901.mp4")));
        Assert.True(File.Exists(Path.Combine(_root, "downloads", "33", "300", "01_950.mp4")));

        // Photo, video, video poster, gif, gif poster: each downloaded once although post 100 was queued twice.
        Assert.Equal(5, _handler.Requests.Count);
    }

    [Fact]
    public async Task Queue_ItemWithoutData_WaitsForCapturedData()
    {
        await _queue.EnqueueAsync([new EnqueueRequest("500", SourceType.Single, "erin", null)]);
        Assert.Equal(1, (await _queue.GetCountsAsync()).MissingData);

        _queue.Start();
        await Task.Delay(200); // let the worker load the list of missing posts
        var parsed = GraphQlTweetParser.Parse(SampleResponses.BookmarksUrl, SampleResponses.Bookmarks);
        _cache.AddRange(parsed.All);

        await WaitUntilAsync(async () => (await _queue.GetCountsAsync()).Completed == 1);
    }

    [Fact]
    public async Task ClearAsync_RemovesAllItems()
    {
        await _queue.EnqueueAsync([new EnqueueRequest("1", SourceType.Single, null, null)]);
        await _queue.ClearAsync();
        Assert.Equal(0, (await _queue.GetCountsAsync()).Total);
    }

    private static async Task WaitUntilAsync(Func<Task<bool>> condition)
    {
        var timeout = DateTime.UtcNow.AddSeconds(15);
        while (!await condition())
        {
            Assert.True(DateTime.UtcNow < timeout, "Timed out waiting for the queue.");
            await Task.Delay(50);
        }
    }

    public void Dispose()
    {
        _queue.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(_root, true);
        }
        catch (IOException)
        {
        }
    }

    private sealed class TestSettings(string root) : IAppSettings
    {
        public string DownloadsFolder { get; set; } = Path.Combine(root, "downloads");
        public string DatabaseFolder { get; set; } = Path.Combine(root, "db");
        public bool SaveRawResponses { get; set; }
        public string AppDataFolder => root;
    }

    private sealed class FakeHandler : HttpMessageHandler
    {
        public List<string> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            lock (Requests)
            {
                Requests.Add(request.RequestUri!.ToString());
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent([1, 2, 3]),
            });
        }
    }
}
