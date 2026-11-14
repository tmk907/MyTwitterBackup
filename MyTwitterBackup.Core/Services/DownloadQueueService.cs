using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MyTwitterBackup.Core.Database;
using MyTwitterBackup.Core.Models;
using Serilog;

namespace MyTwitterBackup.Core.Services;

/// <summary>A post to add to the queue. <paramref name="Data"/> is null when it was not captured.</summary>
public sealed record EnqueueRequest(string PostId, SourceType SourceType, string? UserName, TweetData? Data);

public sealed record QueueCounts(int Pending, int Downloading, int Completed, int Failed, int MissingData)
{
    public int Total => Pending + Downloading + Completed + Failed + MissingData;
}

/// <summary>
/// Persistent download queue backed by SQLite. A single background worker processes pending items
/// one at a time; interrupted items are resumed after an app restart.
/// </summary>
public sealed class DownloadQueueService : IDisposable
{
    public const string MissingDataMessage =
        "Post data was not captured. Open the post in the browser or use 'Fetch missing data'.";

    private static readonly JsonSerializerOptions s_jsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IAppDbContextFactory _dbFactory;
    private readonly IAppSettings _settings;
    private readonly MediaDownloader _downloader;
    private readonly TweetCache _cache;
    private readonly AsyncAutoResetEvent _signal = new();
    private readonly CancellationTokenSource _shutdown = new();
    private readonly ConcurrentDictionary<string, byte> _missingPostIds = new();
    private readonly object _currentLock = new();
    private CancellationTokenSource? _currentItemCts;
    private Task? _worker;

    public DownloadQueueService(IAppDbContextFactory dbFactory, IAppSettings settings, MediaDownloader downloader, TweetCache cache)
    {
        _dbFactory = dbFactory;
        _settings = settings;
        _downloader = downloader;
        _cache = cache;
        _cache.TweetsAdded += OnTweetsAdded;
    }

    /// <summary>Raised from a background thread when a single item changed status.</summary>
    public event EventHandler<QueueItem>? ItemUpdated;

    /// <summary>Raised from a background thread after bulk changes (enqueue, clear, retry).</summary>
    public event EventHandler? QueueChanged;

    /// <summary>Raised when <see cref="IsPaused"/> or <see cref="CurrentItem"/> changes.</summary>
    public event EventHandler? StateChanged;

    public bool IsPaused { get; private set; }

    public QueueItem? CurrentItem { get; private set; }

    /// <summary>Starts the background worker (idempotent).</summary>
    public void Start()
    {
        _worker ??= Task.Run(() => RunAsync(_shutdown.Token));
    }

    public void Pause()
    {
        IsPaused = true;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Resume()
    {
        IsPaused = false;
        _signal.Set();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Re-reads state after the database location changed.</summary>
    public async Task ReloadAsync()
    {
        CancelCurrentItem();
        await ResetInterruptedAsync();
        _signal.Set();
        QueueChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task<int> EnqueueAsync(IReadOnlyCollection<EnqueueRequest> requests)
    {
        if (requests.Count == 0)
        {
            return 0;
        }

        var added = 0;
        var now = DateTime.UtcNow;
        await using (var db = _dbFactory.Create())
        {
            var ids = requests.Select(r => r.PostId).Distinct().ToList();
            var existing = await db.QueueItems.Where(q => ids.Contains(q.PostId)).ToListAsync();

            foreach (var request in requests.DistinctBy(r => (r.PostId, r.SourceType)))
            {
                var data = request.Data ?? _cache.Get(request.PostId);
                var json = data is null ? null : JsonSerializer.Serialize(data, s_jsonOptions);
                var item = existing.FirstOrDefault(q => q.PostId == request.PostId && q.SourceType == request.SourceType);

                if (item is null)
                {
                    item = new QueueItem
                    {
                        PostId = request.PostId,
                        SourceType = request.SourceType,
                        CreatedAt = now,
                    };
                    db.QueueItems.Add(item);
                    added++;
                }
                else if (item.Status is QueueStatus.Failed or QueueStatus.MissingData)
                {
                    added++;
                }
                else if (item.Status is QueueStatus.Completed or QueueStatus.Downloading || json is null)
                {
                    continue;
                }

                item.UserName = data?.UserName ?? request.UserName ?? item.UserName;
                item.PreviewText = data is null ? item.PreviewText : Snippet(data.Text);
                item.TweetJson = json ?? item.TweetJson;
                item.Status = item.TweetJson is null ? QueueStatus.MissingData : QueueStatus.Pending;
                item.Error = item.TweetJson is null ? MissingDataMessage : null;
                item.UpdatedAt = now;

                if (item.Status == QueueStatus.MissingData)
                {
                    _missingPostIds[item.PostId] = 0;
                }
            }

            await db.SaveChangesAsync();
        }

        Log.Information("Enqueued {Added} of {Requested} posts", added, requests.Count);
        _signal.Set();
        QueueChanged?.Invoke(this, EventArgs.Empty);
        return added;
    }

    /// <summary>Removes every item from the queue (cancels the current download).</summary>
    public async Task ClearAsync()
    {
        CancelCurrentItem();
        await using (var db = _dbFactory.Create())
        {
            await db.QueueItems.ExecuteDeleteAsync();
        }

        _missingPostIds.Clear();
        QueueChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task ClearCompletedAsync()
    {
        await using (var db = _dbFactory.Create())
        {
            await db.QueueItems.Where(q => q.Status == QueueStatus.Completed).ExecuteDeleteAsync();
        }

        QueueChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Moves failed items (and missing data items that are now in the cache) back to pending.</summary>
    public async Task<int> RetryFailedAsync()
    {
        int count;
        await using (var db = _dbFactory.Create())
        {
            count = await db.QueueItems
                .Where(q => q.Status == QueueStatus.Failed)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(q => q.Status, QueueStatus.Pending)
                    .SetProperty(q => q.Error, (string?)null)
                    .SetProperty(q => q.UpdatedAt, DateTime.UtcNow));
        }

        var filled = await FillMissingFromCacheAsync();
        _signal.Set();
        QueueChanged?.Invoke(this, EventArgs.Empty);
        return count + filled;
    }

    /// <summary>Moves <see cref="QueueStatus.MissingData"/> items whose data is now captured back to pending.</summary>
    public async Task<int> FillMissingFromCacheAsync()
    {
        var missing = await GetMissingDataItemsAsync();
        var cached = missing.Select(m => _cache.Get(m.PostId)).OfType<TweetData>().DistinctBy(t => t.Id).ToList();
        if (cached.Count > 0)
        {
            await SupplyDataAsync(cached);
            _signal.Set();
            QueueChanged?.Invoke(this, EventArgs.Empty);
        }

        return cached.Count;
    }

    public async Task<List<QueueItem>> GetItemsAsync()
    {
        await using var db = _dbFactory.Create();
        return await db.QueueItems.AsNoTracking()
            .OrderBy(q => q.Id)
            .Select(q => new QueueItem
            {
                Id = q.Id,
                PostId = q.PostId,
                SourceType = q.SourceType,
                Status = q.Status,
                UserName = q.UserName,
                PreviewText = q.PreviewText,
                Error = q.Error,
                Attempts = q.Attempts,
                CreatedAt = q.CreatedAt,
                UpdatedAt = q.UpdatedAt,
            })
            .ToListAsync();
    }

    public async Task<List<QueueItem>> GetMissingDataItemsAsync()
    {
        await using var db = _dbFactory.Create();
        return await db.QueueItems.AsNoTracking()
            .Where(q => q.Status == QueueStatus.MissingData)
            .OrderBy(q => q.Id)
            .ToListAsync();
    }

    public async Task<QueueCounts> GetCountsAsync()
    {
        await using var db = _dbFactory.Create();
        var groups = await db.QueueItems
            .GroupBy(q => q.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync();
        int Count(QueueStatus s) => groups.FirstOrDefault(g => g.Status == s)?.Count ?? 0;
        return new QueueCounts(
            Count(QueueStatus.Pending),
            Count(QueueStatus.Downloading),
            Count(QueueStatus.Completed),
            Count(QueueStatus.Failed),
            Count(QueueStatus.MissingData));
    }

    private async Task RunAsync(CancellationToken shutdown)
    {
        try
        {
            await ResetInterruptedAsync();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Could not reset interrupted queue items");
        }

        while (!shutdown.IsCancellationRequested)
        {
            try
            {
                QueueItem? item = null;
                if (!IsPaused)
                {
                    await using var db = _dbFactory.Create();
                    item = await db.QueueItems.AsNoTracking()
                        .Where(q => q.Status == QueueStatus.Pending)
                        .OrderBy(q => q.Id)
                        .FirstOrDefaultAsync(shutdown);
                }

                if (item is null)
                {
                    await _signal.WaitAsync(shutdown);
                    continue;
                }

                await ProcessAsync(item, shutdown);
            }
            catch (OperationCanceledException) when (shutdown.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // Database not available (e.g. folder changed). Wait for a signal or a timeout.
                Log.Error(ex, "Download queue worker error");
                try
                {
                    await Task.WhenAny(_signal.WaitAsync(shutdown), Task.Delay(TimeSpan.FromSeconds(30), shutdown));
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }

    private async Task ResetInterruptedAsync()
    {
        await using var db = _dbFactory.Create();
        await db.QueueItems
            .Where(q => q.Status == QueueStatus.Downloading)
            .ExecuteUpdateAsync(s => s.SetProperty(q => q.Status, QueueStatus.Pending));

        _missingPostIds.Clear();
        var missing = await db.QueueItems
            .Where(q => q.Status == QueueStatus.MissingData)
            .Select(q => q.PostId)
            .ToListAsync();
        foreach (var id in missing)
        {
            _missingPostIds[id] = 0;
        }
    }

    private async Task ProcessAsync(QueueItem item, CancellationToken shutdown)
    {
        using var itemCts = CancellationTokenSource.CreateLinkedTokenSource(shutdown);
        lock (_currentLock)
        {
            _currentItemCts = itemCts;
        }

        CurrentItem = item;
        StateChanged?.Invoke(this, EventArgs.Empty);

        try
        {
            await UpdateStatusAsync(item, QueueStatus.Downloading, null);

            var data = Deserialize(item.TweetJson) ?? _cache.Get(item.PostId);
            if (data is null)
            {
                _missingPostIds[item.PostId] = 0;
                await UpdateStatusAsync(item, QueueStatus.MissingData, MissingDataMessage);
                return;
            }

            await SavePostAsync(data, item.SourceType, itemCts.Token);
            await UpdateStatusAsync(item, QueueStatus.Completed, null);
        }
        catch (OperationCanceledException) when (itemCts.IsCancellationRequested)
        {
            if (!shutdown.IsCancellationRequested)
            {
                // Item was cancelled by "clear queue"; if it still exists put it back to pending.
                await UpdateStatusAsync(item, QueueStatus.Pending, null);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Download of post {PostId} failed", item.PostId);
            item.Attempts++;
            await UpdateStatusAsync(item, QueueStatus.Failed, ex.Message);
        }
        finally
        {
            lock (_currentLock)
            {
                _currentItemCts = null;
            }

            CurrentItem = null;
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private async Task UpdateStatusAsync(QueueItem item, QueueStatus status, string? error)
    {
        item.Status = status;
        item.Error = error;
        item.UpdatedAt = DateTime.UtcNow;
        await using (var db = _dbFactory.Create())
        {
            await db.QueueItems
                .Where(q => q.Id == item.Id)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(q => q.Status, item.Status)
                    .SetProperty(q => q.Error, item.Error)
                    .SetProperty(q => q.Attempts, item.Attempts)
                    .SetProperty(q => q.UpdatedAt, item.UpdatedAt));
        }

        ItemUpdated?.Invoke(this, item);
    }

    /// <summary>Saves the post (and its quoted post) to the database and downloads media.</summary>
    internal async Task SavePostAsync(TweetData data, SourceType sourceType, CancellationToken cancellationToken)
    {
        List<PostMedia>? quotedMedia = null;
        if (data.QuotedTweet is not null)
        {
            quotedMedia = await DownloadMediaAsync(data.QuotedTweet, cancellationToken);
        }

        var media = await DownloadMediaAsync(data, cancellationToken);

        await using var db = _dbFactory.Create();
        if (data.QuotedTweet is not null)
        {
            await UpsertPostAsync(db, data.QuotedTweet, quotedMedia!);
        }

        await UpsertPostAsync(db, data, media);

        var hasSource = await db.PostSources.AnyAsync(s => s.PostId == data.Id && s.SourceType == sourceType, cancellationToken);
        if (!hasSource)
        {
            db.PostSources.Add(new PostSource { PostId = data.Id, SourceType = sourceType, AddedAt = DateTime.UtcNow });
        }

        await db.SaveChangesAsync(CancellationToken.None);
    }

    private async Task<List<PostMedia>> DownloadMediaAsync(TweetData tweet, CancellationToken cancellationToken)
    {
        var relativeFolder = GetRelativeFolder(tweet);
        var root = _settings.DownloadsFolder;
        var result = new List<PostMedia>();

        for (var i = 0; i < tweet.Media.Count; i++)
        {
            var media = tweet.Media[i];
            var baseName = $"{i + 1:D2}_{SafeFileName(media.Id)}";
            var extension = media.Type == MediaType.Photo ? GetPhotoExtension(media.Url) : "mp4";
            var relativePath = Path.Combine(relativeFolder, $"{baseName}.{extension}");

            await _downloader.DownloadFileAsync(media.Url, Path.Combine(root, relativePath), cancellationToken);

            string? previewRelativePath = null;
            if (!string.IsNullOrEmpty(media.PreviewUrl))
            {
                previewRelativePath = Path.Combine(relativeFolder, $"{baseName}_poster.{GetPhotoExtension(media.PreviewUrl)}");
                try
                {
                    await _downloader.DownloadFileAsync(media.PreviewUrl, Path.Combine(root, previewRelativePath), cancellationToken);
                }
                catch (HttpRequestException ex)
                {
                    Log.Warning(ex, "Poster download failed for post {PostId}", tweet.Id);
                    previewRelativePath = null;
                }
            }

            result.Add(new PostMedia
            {
                PostId = tweet.Id,
                Index = i,
                MediaId = media.Id,
                Type = media.Type,
                RemoteUrl = media.Url,
                PreviewRemoteUrl = media.PreviewUrl,
                LocalPath = relativePath,
                PreviewLocalPath = previewRelativePath,
                Width = media.Width,
                Height = media.Height,
                DurationMs = media.DurationMs,
            });
        }

        return result;
    }

    private static async Task UpsertPostAsync(AppDbContext db, TweetData data, List<PostMedia> media)
    {
        var post = await db.Posts.Include(p => p.Media).FirstOrDefaultAsync(p => p.Id == data.Id);
        if (post is null)
        {
            post = new Post
            {
                Id = data.Id,
                UserId = data.UserId,
                UserName = data.UserName,
                DownloadedAt = DateTime.UtcNow,
            };
            db.Posts.Add(post);
        }

        post.UserId = string.IsNullOrEmpty(data.UserId) ? post.UserId : data.UserId;
        post.UserName = string.IsNullOrEmpty(data.UserName) ? post.UserName : data.UserName;
        post.DisplayName = data.DisplayName;
        post.CreatedAt = data.CreatedAtUtc;
        post.Text = data.Text;
        post.Lang = data.Lang;
        post.LikeCount = data.LikeCount;
        post.RetweetCount = data.RetweetCount;
        post.ReplyCount = data.ReplyCount;
        post.QuoteCount = data.QuoteCount;
        post.BookmarkCount = data.BookmarkCount;
        post.ViewCount = data.ViewCount;
        post.ConversationId = data.ConversationId;
        post.InReplyToPostId = data.InReplyToPostId;
        post.QuotedPostId = data.QuotedTweet?.Id;

        foreach (var m in media)
        {
            var existing = post.Media.FirstOrDefault(x => x.MediaId == m.MediaId);
            if (existing is null)
            {
                post.Media.Add(m);
                continue;
            }

            existing.Index = m.Index;
            existing.Type = m.Type;
            existing.RemoteUrl = m.RemoteUrl;
            existing.PreviewRemoteUrl = m.PreviewRemoteUrl;
            existing.LocalPath = m.LocalPath;
            existing.PreviewLocalPath = m.PreviewLocalPath ?? existing.PreviewLocalPath;
            existing.Width = m.Width;
            existing.Height = m.Height;
            existing.DurationMs = m.DurationMs;
        }

        // Quoted post must exist before the referencing post is saved.
        await db.SaveChangesAsync();
    }

    private void OnTweetsAdded(object? sender, IReadOnlyList<TweetData> tweets)
    {
        if (_missingPostIds.IsEmpty)
        {
            return;
        }

        var hits = tweets.Where(t => _missingPostIds.ContainsKey(t.Id)).ToList();
        if (hits.Count > 0)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await SupplyDataAsync(hits);
                    _signal.Set();
                    QueueChanged?.Invoke(this, EventArgs.Empty);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Could not update queue items with captured data");
                }
            });
        }
    }

    /// <summary>Fills captured data into items waiting in <see cref="QueueStatus.MissingData"/>.</summary>
    private async Task SupplyDataAsync(IReadOnlyCollection<TweetData> tweets)
    {
        await using var db = _dbFactory.Create();
        var ids = tweets.Select(t => t.Id).ToList();
        var items = await db.QueueItems
            .Where(q => ids.Contains(q.PostId) && q.Status == QueueStatus.MissingData)
            .ToListAsync();

        foreach (var item in items)
        {
            var data = tweets.First(t => t.Id == item.PostId);
            item.TweetJson = JsonSerializer.Serialize(data, s_jsonOptions);
            item.UserName = data.UserName;
            item.PreviewText = Snippet(data.Text);
            item.Status = QueueStatus.Pending;
            item.Error = null;
            item.UpdatedAt = DateTime.UtcNow;
        }

        await db.SaveChangesAsync();
        foreach (var id in ids)
        {
            _missingPostIds.TryRemove(id, out _);
        }

        Log.Information("Captured data for {Count} queue items with missing data", items.Count);
    }

    private void CancelCurrentItem()
    {
        lock (_currentLock)
        {
            _currentItemCts?.Cancel();
        }
    }

    private static TweetData? Deserialize(string? json)
    {
        if (string.IsNullOrEmpty(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<TweetData>(json, s_jsonOptions);
        }
        catch (JsonException ex)
        {
            Log.Warning(ex, "Invalid queue item json");
            return null;
        }
    }

    public static string GetRelativeFolder(TweetData tweet)
    {
        var user = !string.IsNullOrEmpty(tweet.UserId) ? tweet.UserId
            : !string.IsNullOrEmpty(tweet.UserName) ? tweet.UserName
            : "unknown";
        return Path.Combine(SafeFileName(user), SafeFileName(tweet.Id));
    }

    public static string Snippet(string? text, int maxLength = 140)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var normalized = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return normalized.Length <= maxLength ? normalized : normalized[..maxLength].TrimEnd() + "...";
    }

    private static string GetPhotoExtension(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return "jpg";
        }

        var query = uri.Query.TrimStart('?').Split('&');
        var format = query.FirstOrDefault(p => p.StartsWith("format=", StringComparison.OrdinalIgnoreCase));
        if (format is not null)
        {
            return SafeFileName(format["format=".Length..]);
        }

        var ext = Path.GetExtension(uri.AbsolutePath).TrimStart('.');
        return string.IsNullOrEmpty(ext) ? "jpg" : SafeFileName(ext.ToLowerInvariant());
    }

    private static string SafeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string(value.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
    }

    public void Dispose()
    {
        _cache.TweetsAdded -= OnTweetsAdded;
        _shutdown.Cancel();
        _shutdown.Dispose();
    }
}
