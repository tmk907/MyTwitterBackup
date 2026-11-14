using System.Globalization;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using MyTwitterBackup.Core.Database;
using MyTwitterBackup.Core.Models;
using MyTwitterBackup.Core.Services;
using MyTwitterBackup.Desktop.Services;
using Windows.Media.Core;

namespace MyTwitterBackup.Desktop.ViewModels;

public sealed record MetadataEntry(string Label, string Value);

/// <summary>A downloaded photo, video or gif shown in the post preview.</summary>
public sealed class MediaItemViewModel : IDisposable
{
    private MediaSource? _mediaSource;

    public MediaItemViewModel(PostMedia media, string downloadsFolder)
    {
        Type = media.Type;
        FilePath = media.LocalPath is null ? null : Path.Combine(downloadsFolder, media.LocalPath);
        PosterPath = media.PreviewLocalPath is null ? null : Path.Combine(downloadsFolder, media.PreviewLocalPath);
        FileExists = FilePath is not null && File.Exists(FilePath);
        Description = media.Type switch
        {
            MediaType.Video => $"Video {media.Width}x{media.Height}" +
                               (media.DurationMs is { } ms ? $", {TimeSpan.FromMilliseconds(ms):m\\:ss}" : string.Empty),
            MediaType.AnimatedGif => $"GIF {media.Width}x{media.Height}",
            _ => $"Photo {media.Width}x{media.Height}",
        };
    }

    public MediaType Type { get; }

    public bool IsVideo => Type != MediaType.Photo;

    public string? FilePath { get; }

    public string? PosterPath { get; }

    public bool FileExists { get; }

    public string Description { get; }

    public bool AutoPlay { get; set; }

    public bool IsLooping => Type == MediaType.AnimatedGif;

    public ImageSource? Image => FileExists && !IsVideo
        ? new BitmapImage(new Uri(FilePath!)) { DecodePixelWidth = 1600 }
        : null;

    public ImageSource? Poster => PosterPath is not null && File.Exists(PosterPath)
        ? new BitmapImage(new Uri(PosterPath))
        : null;

    public MediaSource? MediaSource => FileExists && IsVideo
        ? _mediaSource ??= MediaSource.CreateFromUri(new Uri(FilePath!))
        : null;

    public void Dispose()
    {
        _mediaSource?.Dispose();
        _mediaSource = null;
    }
}

/// <summary>Preview of a downloaded post (and its quoted post).</summary>
public sealed partial class PostDetailViewModel : IDisposable
{
    public PostDetailViewModel(Post post, string downloadsFolder, bool autoPlay = false, bool includeQuoted = true)
    {
        Id = post.Id;
        DisplayName = string.IsNullOrEmpty(post.DisplayName) ? post.UserName : post.DisplayName;
        UserName = "@" + post.UserName;
        CreatedText = FormatDate(post.CreatedAt);
        Text = post.Text;
        PostUri = new Uri(post.Url);
        var firstMediaPath = post.Media.Select(m => m.LocalPath).FirstOrDefault(p => p is not null);
        FolderPath = firstMediaPath is not null
            ? Path.Combine(downloadsFolder, Path.GetDirectoryName(firstMediaPath)!)
            : Path.Combine(downloadsFolder, DownloadQueueService.MediaFolderName, post.UserId, post.Id);
        Media = post.Media.OrderBy(m => m.Index).Select(m => new MediaItemViewModel(m, downloadsFolder)).ToList();
        Quoted = includeQuoted && post.QuotedPost is not null
            ? new PostDetailViewModel(post.QuotedPost, downloadsFolder, includeQuoted: false)
            : null;

        if (autoPlay)
        {
            // GIFs are silent and all play; only the first video plays so sounds do not overlap.
            var allMedia = Media.Concat(Quoted?.Media ?? []).ToList();
            foreach (var gif in allMedia.Where(m => m.Type == MediaType.AnimatedGif))
            {
                gif.AutoPlay = true;
            }

            if (allMedia.FirstOrDefault(m => m.Type == MediaType.Video) is { } video)
            {
                video.AutoPlay = true;
            }
        }

        var metadata = new List<MetadataEntry>
        {
            new("Likes", post.LikeCount.ToString("N0", CultureInfo.CurrentCulture)),
            new("Reposts", post.RetweetCount.ToString("N0", CultureInfo.CurrentCulture)),
            new("Replies", post.ReplyCount.ToString("N0", CultureInfo.CurrentCulture)),
            new("Quotes", post.QuoteCount.ToString("N0", CultureInfo.CurrentCulture)),
            new("Bookmarks", post.BookmarkCount.ToString("N0", CultureInfo.CurrentCulture)),
        };
        if (post.ViewCount is { } views)
        {
            metadata.Add(new("Views", views.ToString("N0", CultureInfo.CurrentCulture)));
        }

        metadata.Add(new("Posted", CreatedText));
        metadata.Add(new("Post id", post.Id));
        metadata.Add(new("User id", post.UserId));
        if (!string.IsNullOrEmpty(post.Lang))
        {
            metadata.Add(new("Language", post.Lang));
        }

        if (post.Sources.Count > 0)
        {
            metadata.Add(new("Downloaded from", string.Join(", ", post.Sources.Select(s => s.SourceType switch
            {
                SourceType.Bookmark => "Bookmarks",
                SourceType.Like => "Likes",
                _ => "Single post",
            }))));
        }

        metadata.Add(new("Downloaded", FormatDate(post.DownloadedAt)));
        Metadata = metadata;
    }

    public string Id { get; }

    public string DisplayName { get; }

    public string UserName { get; }

    public string CreatedText { get; }

    public string Text { get; }

    public Uri PostUri { get; }

    public string FolderPath { get; }

    public List<MediaItemViewModel> Media { get; }

    public bool HasMedia => Media.Count > 0;

    public PostDetailViewModel? Quoted { get; }

    public bool HasQuoted => Quoted is not null;

    public List<MetadataEntry> Metadata { get; }

    [RelayCommand]
    private async Task OpenFolderAsync() => await FolderPickerService.OpenFolderAsync(FolderPath);

    public static string FormatDate(DateTime utc) =>
        utc == DateTime.MinValue
            ? "-"
            : DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToLocalTime().ToString("g", CultureInfo.CurrentCulture);

    public void Dispose()
    {
        foreach (var media in Media)
        {
            media.Dispose();
        }

        Quoted?.Dispose();
    }
}
