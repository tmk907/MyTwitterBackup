using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using MyTwitterBackup.Core.Models;
using MyTwitterBackup.Core.Services;
using MyTwitterBackup.Core.Twitter;
using MyTwitterBackup.Desktop.Services;
using Serilog;

namespace MyTwitterBackup.Desktop.ViewModels;

/// <summary>Implemented by the browser page so the view model can drive the WebView.</summary>
public interface IWebViewHost
{
    void Navigate(string url);

    /// <summary>Starts or stops scrolling to the end of the list. Returns false when the page script is not available.</summary>
    Task<bool> SetAutoScrollAsync(bool enabled);
}

public partial class BrowserViewModel : ObservableObject, IRecipient<ResolveMissingDataMessage>
{
    public const string StartUrl = "https://x.com/home";

    private static readonly TimeSpan s_resolveTimeout = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan s_duplicateWindow = TimeSpan.FromSeconds(15);

    private readonly TweetCache _cache;
    private readonly DownloadQueueService _queue;
    private readonly IAppSettings _settings;

    /// <summary>Detected posts per page, kept for the whole session (going back to a page restores its list).</summary>
    private readonly Dictionary<string, ObservableCollection<DetectedPostViewModel>> _sessions = [];
    private readonly ConcurrentDictionary<string, DateTime> _recentResponses = new();
    private readonly ObservableCollection<DetectedPostViewModel> _emptyList = [];
    private CancellationTokenSource? _resolveCts;
    private bool _resolveRequested;
    private IWebViewHost? _host;

    public BrowserViewModel(TweetCache cache, DownloadQueueService queue, IAppSettings settings)
    {
        _cache = cache;
        _queue = queue;
        _settings = settings;
        DetectedPosts = _emptyList;
        WeakReferenceMessenger.Default.Register(this);
    }

    public IWebViewHost? Host
    {
        get => _host;
        set
        {
            _host = value;
            ToggleAutoScrollCommand.NotifyCanExecuteChanged();
            if (_host is not null && _resolveRequested)
            {
                _resolveRequested = false;
                _ = ResolveMissingDataAsync();
            }
        }
    }

    [ObservableProperty]
    public partial string CurrentUrl { get; set; } = StartUrl;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ModeTitle), nameof(ModeDescription), nameof(ModeGlyph), nameof(IsDownloadablePage), nameof(IsListPage))]
    [NotifyCanExecuteChangedFor(nameof(DownloadCommand), nameof(ToggleAutoScrollCommand))]
    public partial PageInfo Page { get; set; } = PageInfo.None;

    [ObservableProperty]
    public partial ObservableCollection<DetectedPostViewModel> DetectedPosts { get; set; }

    [ObservableProperty]
    public partial int DetectedCount { get; set; }

    [ObservableProperty]
    public partial int WithDataCount { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DownloadCommand))]
    public partial int NotQueuedCount { get; set; }

    [ObservableProperty]
    public partial string? StatusMessage { get; set; }

    [ObservableProperty]
    public partial bool IsResolving { get; set; }

    [ObservableProperty]
    public partial string? ResolveProgressText { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AutoScrollLabel), nameof(AutoScrollGlyph))]
    public partial bool IsAutoScrolling { get; set; }

    public bool IsDownloadablePage => Page.Mode != PageMode.None;

    /// <summary>Bookmarks or likes: a list that can be scrolled to the end.</summary>
    public bool IsListPage => Page.Mode is PageMode.Bookmarks or PageMode.Likes;

    public string AutoScrollLabel => IsAutoScrolling ? "Stop scrolling" : "Scroll to the end";

    public string AutoScrollGlyph => IsAutoScrolling ? "\uE71A" : "\uE74B"; // Stop : Down

    public string ModeTitle => Page.Mode switch
    {
        PageMode.Single => "Single post",
        PageMode.Bookmarks => "Bookmarks",
        PageMode.Likes => "Likes",
        _ => "Nothing to download here",
    };

    public string ModeDescription => Page.Mode switch
    {
        PageMode.Single => "Click Download to add this post to the queue.",
        PageMode.Bookmarks or PageMode.Likes =>
            "Scroll the page (or use Scroll to the end) to detect more posts. Download adds all detected posts to the queue.",
        _ => "Open a post, your bookmarks (x.com/i/bookmarks) or likes (x.com/{user}/likes). Log in to x.com first.",
    };

    public string ModeGlyph => Page.Mode switch
    {
        PageMode.Single => "",    // Document
        PageMode.Bookmarks => "", // Bookmarks
        PageMode.Likes => "",     // Heart
        _ => "",                  // Globe
    };

    /// <summary>Called (UI thread) when the WebView url changes, including SPA navigation.</summary>
    public void OnUrlChanged(string? url)
    {
        if (string.IsNullOrEmpty(url))
        {
            return;
        }

        CurrentUrl = url;
        var page = XUrlClassifier.Classify(url);
        if (page.PageKey == Page.PageKey && page.Mode == Page.Mode)
        {
            return;
        }

        Page = page;
        StatusMessage = null;
        if (IsAutoScrolling)
        {
            // The page script stops by itself when the url changes; this only resets the button.
            IsAutoScrolling = false;
            _ = Host?.SetAutoScrollAsync(false);
        }

        if (page.Mode == PageMode.None)
        {
            DetectedPosts = _emptyList;
        }
        else
        {
            DetectedPosts = GetSession(page.PageKey);
            if (page.Mode == PageMode.Single && page.PostId is not null)
            {
                AddDetected(DetectedPosts, page.PostId, page.UserName);
            }
        }

        UpdateCounts();
    }

    /// <summary>Called from any thread with a raw message from the injected script.</summary>
    public void OnWebMessage(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("type", out var type))
            {
                return;
            }

            switch (type.GetString())
            {
                case "graphql":
                    var url = root.GetProperty("url").GetString();
                    var body = root.GetProperty("body").GetString();
                    if (url is not null && body is not null)
                    {
                        _ = Task.Run(() => OnGraphQlResponse(url, body));
                    }

                    break;

                case "dom":
                    var pageUrl = root.GetProperty("url").GetString();
                    var posts = root.GetProperty("posts").EnumerateArray()
                        .Select(p => (User: p.GetProperty("user").GetString(), Id: p.GetProperty("id").GetString()))
                        .Where(p => !string.IsNullOrEmpty(p.Id))
                        .Select(p => (p.User, Id: p.Id!))
                        .ToList();
                    OnDomPosts(pageUrl, posts);
                    break;

                case "autoscroll":
                    var reachedEnd = root.GetProperty("state").GetString() == "done";
                    App.MainDispatcher.TryEnqueue(() => OnAutoScrollEnded(reachedEnd));
                    break;
            }
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            Log.Warning(ex, "Invalid web message");
        }
    }

    /// <summary>Called from a background thread with a captured GraphQL response.</summary>
    public void OnGraphQlResponse(string url, string body)
    {
        if (!GraphQlTweetParser.IsGraphQlUrl(url) || IsDuplicate(url, body))
        {
            return;
        }

        SaveRawResponse(url, body);

        GraphQlParseResult result;
        try
        {
            result = GraphQlTweetParser.Parse(url, body);
        }
        catch (JsonException ex)
        {
            Log.Warning(ex, "Could not parse GraphQL response {Url}", url);
            return;
        }

        Log.Debug("GraphQL {Operation}: {Top} entries, {All} posts", result.OperationName, result.TopLevel.Count, result.All.Count);
        if (result.All.Count == 0)
        {
            return;
        }

        _cache.AddRange(result.All);
        App.MainDispatcher.TryEnqueue(() => ApplyParsed(result));
    }

    private void ApplyParsed(GraphQlParseResult result)
    {
        var addEntries = Page.Mode switch
        {
            PageMode.Bookmarks => GraphQlTweetParser.IsBookmarksOperation(result.OperationName),
            PageMode.Likes => GraphQlTweetParser.IsLikesOperation(result.OperationName),
            _ => false,
        };

        if (addEntries)
        {
            foreach (var tweet in result.TopLevel)
            {
                AddDetected(DetectedPosts, tweet.Id, tweet.UserName);
            }
        }

        RefreshData();
        UpdateCounts();
    }

    private void OnDomPosts(string? pageUrl, List<(string? User, string Id)> posts)
    {
        Log.Verbose("DOM scan {Url}: {Count} posts", pageUrl, posts.Count);
        App.MainDispatcher.TryEnqueue(() =>
        {
            var page = XUrlClassifier.Classify(pageUrl);
            if (page.Mode is not (PageMode.Bookmarks or PageMode.Likes) || page.PageKey != Page.PageKey)
            {
                return;
            }

            foreach (var (user, id) in posts)
            {
                AddDetected(DetectedPosts, id, user);
            }

            UpdateCounts();
        });
    }

    private void AddDetected(ObservableCollection<DetectedPostViewModel> list, string postId, string? userName)
    {
        if (list.Any(p => p.PostId == postId))
        {
            return;
        }

        list.Add(new DetectedPostViewModel(postId, userName) { Data = _cache.Get(postId) });
    }

    /// <summary>Fills captured data into detected posts that did not have it yet.</summary>
    private void RefreshData()
    {
        foreach (var session in _sessions.Values)
        {
            foreach (var item in session)
            {
                if (_cache.Get(item.PostId) is { } data && !ReferenceEquals(item.Data, data))
                {
                    item.Data = data;
                }
            }
        }
    }

    private bool CanDownload() => Page.SourceType is not null && NotQueuedCount > 0;

    [RelayCommand(CanExecute = nameof(CanDownload))]
    private async Task DownloadAsync()
    {
        if (Page.SourceType is not { } sourceType)
        {
            return;
        }

        var items = DetectedPosts.Where(p => !p.IsQueued).ToList();
        var requests = items
            .Select(p => new EnqueueRequest(p.PostId, sourceType, p.UserName, p.Data ?? _cache.Get(p.PostId)))
            .ToList();

        try
        {
            var added = await _queue.EnqueueAsync(requests);
            foreach (var item in items)
            {
                item.IsQueued = true;
            }

            _queue.Resume();
            var missing = requests.Count(r => r.Data is null);
            StatusMessage = missing == 0
                ? $"{added} post(s) added to the queue."
                : $"{added} post(s) added to the queue. {missing} without captured data: open them or use 'Fetch missing data' on the Queue page.";
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Enqueue failed");
            StatusMessage = $"Could not add posts to the queue: {ex.Message}";
        }

        UpdateCounts();
    }

    private bool CanToggleAutoScroll() => IsListPage && Host is not null;

    [RelayCommand(CanExecute = nameof(CanToggleAutoScroll))]
    private async Task ToggleAutoScrollAsync()
    {
        if (Host is null)
        {
            return;
        }

        if (IsAutoScrolling)
        {
            IsAutoScrolling = false;
            await Host.SetAutoScrollAsync(false);
            return;
        }

        if (await Host.SetAutoScrollAsync(true))
        {
            IsAutoScrolling = true;
            StatusMessage = null;
        }
        else
        {
            StatusMessage = "Auto-scroll is not available on this page. Reload the page and try again.";
        }
    }

    private void OnAutoScrollEnded(bool reachedEnd)
    {
        if (!IsAutoScrolling)
        {
            return;
        }

        IsAutoScrolling = false;
        if (reachedEnd)
        {
            StatusMessage = $"Reached the end of the list: {DetectedCount} post(s) detected.";
        }
    }

    [RelayCommand]
    private void ClearDetected()
    {
        DetectedPosts.Clear();
        if (Page.Mode == PageMode.Single && Page.PostId is not null)
        {
            AddDetected(DetectedPosts, Page.PostId, Page.UserName);
        }

        StatusMessage = null;
        UpdateCounts();
    }

    public void Receive(ResolveMissingDataMessage message)
    {
        if (Host is null)
        {
            _resolveRequested = true;
            return;
        }

        _ = ResolveMissingDataAsync();
    }

    /// <summary>
    /// Fallback for queue items without data: opens each post page so the app can capture its
    /// TweetDetail response. The queue picks up the captured data automatically.
    /// </summary>
    private async Task ResolveMissingDataAsync()
    {
        if (IsResolving || Host is null)
        {
            return;
        }

        var items = (await _queue.GetMissingDataItemsAsync()).DistinctBy(i => i.PostId).ToList();
        if (items.Count == 0)
        {
            StatusMessage = "No queued posts are missing data.";
            return;
        }

        _resolveCts = new CancellationTokenSource();
        var token = _resolveCts.Token;
        IsResolving = true;
        var resolved = 0;
        try
        {
            for (var i = 0; i < items.Count && !token.IsCancellationRequested; i++)
            {
                var item = items[i];
                ResolveProgressText = $"Fetching missing data {i + 1} of {items.Count}...";
                if (_cache.Get(item.PostId) is null)
                {
                    Host.Navigate(item.Url);
                    var deadline = DateTime.UtcNow + s_resolveTimeout;
                    while (_cache.Get(item.PostId) is null && DateTime.UtcNow < deadline)
                    {
                        await Task.Delay(250, token);
                    }

                    // Be gentle with x.com.
                    await Task.Delay(1500, token);
                }

                if (_cache.Get(item.PostId) is not null)
                {
                    resolved++;
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            IsResolving = false;
            ResolveProgressText = null;
            _resolveCts.Dispose();
            _resolveCts = null;
        }

        // Captured items are normally moved to Pending right away; this catches posts that were already cached.
        await _queue.FillMissingFromCacheAsync();
        StatusMessage = $"Captured data for {resolved} of {items.Count} post(s).";
    }

    [RelayCommand]
    private void CancelResolve() => _resolveCts?.Cancel();

    private ObservableCollection<DetectedPostViewModel> GetSession(string pageKey)
    {
        if (!_sessions.TryGetValue(pageKey, out var list))
        {
            list = [];
            _sessions[pageKey] = list;
        }

        return list;
    }

    private void UpdateCounts()
    {
        DetectedCount = DetectedPosts.Count;
        WithDataCount = DetectedPosts.Count(p => p.HasData);
        NotQueuedCount = DetectedPosts.Count(p => !p.IsQueued);
    }

    /// <summary>The same response can arrive from the injected script and from WebResourceResponseReceived.</summary>
    private bool IsDuplicate(string url, string body)
    {
        var now = DateTime.UtcNow;
        var key = $"{url}|{body.Length}";
        if (_recentResponses.TryGetValue(key, out var seen) && now - seen < s_duplicateWindow)
        {
            return true;
        }

        _recentResponses[key] = now;
        if (_recentResponses.Count > 500)
        {
            foreach (var old in _recentResponses.Where(p => now - p.Value > s_duplicateWindow).ToList())
            {
                _recentResponses.TryRemove(old.Key, out _);
            }
        }

        return false;
    }

    private void SaveRawResponse(string url, string body)
    {
        if (!_settings.SaveRawResponses)
        {
            return;
        }

        try
        {
            var folder = AppSettings.GetResponsesFolder(_settings);
            Directory.CreateDirectory(folder);
            var name = $"{DateTime.Now:yyyyMMdd_HHmmss_fff}_{GraphQlTweetParser.GetOperationName(url)}.json";
            File.WriteAllText(Path.Combine(folder, name), body);
        }
        catch (IOException ex)
        {
            Log.Warning(ex, "Could not save raw response");
        }
    }
}
