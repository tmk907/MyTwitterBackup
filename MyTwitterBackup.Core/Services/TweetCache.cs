using System.Collections.Concurrent;
using MyTwitterBackup.Core.Models;

namespace MyTwitterBackup.Core.Services;

/// <summary>In-memory, session-wide store of every post captured from x.com responses.</summary>
public sealed class TweetCache
{
    private readonly ConcurrentDictionary<string, TweetData> _tweets = new();

    /// <summary>Raised (on the calling thread) after posts were added or updated.</summary>
    public event EventHandler<IReadOnlyList<TweetData>>? TweetsAdded;

    public int Count => _tweets.Count;

    public TweetData? Get(string postId) => _tweets.TryGetValue(postId, out var tweet) ? tweet : null;

    public void AddRange(IReadOnlyCollection<TweetData> tweets)
    {
        if (tweets.Count == 0)
        {
            return;
        }

        foreach (var tweet in tweets)
        {
            _tweets.AddOrUpdate(tweet.Id, tweet, (_, existing) => Merge(existing, tweet));
        }

        TweetsAdded?.Invoke(this, tweets as IReadOnlyList<TweetData> ?? tweets.ToList());
    }

    private static TweetData Merge(TweetData existing, TweetData incoming)
    {
        // Partial objects (e.g. without user info or without the quoted post) must not hide richer data.
        if (string.IsNullOrEmpty(incoming.UserName) && !string.IsNullOrEmpty(existing.UserName))
        {
            return existing;
        }

        if (incoming.QuotedTweet is null && existing.QuotedTweet is not null)
        {
            return existing;
        }

        return incoming;
    }
}
