using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MyTwitterBackup.Core.Models;
using MyTwitterBackup.Core.Services;
using Serilog;

namespace MyTwitterBackup.Desktop.ViewModels;

public sealed record PostListItem(PostSummary Summary)
{
    public string Id => Summary.Id;

    public string Author => string.IsNullOrEmpty(Summary.DisplayName)
        ? "@" + Summary.UserName
        : $"{Summary.DisplayName} @{Summary.UserName}";

    public string DateText => PostDetailViewModel.FormatDate(Summary.CreatedAt);

    public string Snippet => Summary.Snippet.Length > 0 ? Summary.Snippet : $"({Summary.MediaCount} media)";

    /// <summary>Used as the accessible name of the list item.</summary>
    public override string ToString() => $"{Author}, {DateText}";

    public bool Matches(string filter) =>
        Summary.Snippet.Contains(filter, StringComparison.CurrentCultureIgnoreCase)
        || Summary.UserName.Contains(filter, StringComparison.CurrentCultureIgnoreCase)
        || Summary.DisplayName.Contains(filter, StringComparison.CurrentCultureIgnoreCase);
}

public partial class DownloadedViewModel(PostRepository repository, IAppSettings settings) : ObservableObject, IDisposable
{
    private List<PostListItem> _all = [];
    private int _detailVersion;

    public ObservableCollection<PostListItem> Posts { get; } = [];

    [ObservableProperty]
    public partial SourceType SelectedSource { get; set; } = SourceType.Bookmark;

    [ObservableProperty]
    public partial string FilterText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial PostListItem? SelectedPost { get; set; }

    [ObservableProperty]
    public partial PostDetailViewModel? Detail { get; set; }

    [ObservableProperty]
    public partial string CountText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsEmpty { get; set; }

    [ObservableProperty]
    public partial string? ErrorMessage { get; set; }

    /// <summary>Applies to the next previewed post.</summary>
    [ObservableProperty]
    public partial bool AutoPlayVideos { get; set; } = settings.AutoPlayVideos;

    partial void OnAutoPlayVideosChanged(bool value) => settings.AutoPlayVideos = value;

    partial void OnSelectedSourceChanged(SourceType value) => _ = LoadAsync();

    partial void OnFilterTextChanged(string value) => ApplyFilter();

    partial void OnSelectedPostChanged(PostListItem? value) => _ = LoadDetailAsync(value);

    partial void OnDetailChanged(PostDetailViewModel? oldValue, PostDetailViewModel? newValue) => oldValue?.Dispose();

    [RelayCommand]
    public async Task LoadAsync()
    {
        try
        {
            ErrorMessage = null;
            _all = (await repository.GetPostsAsync(SelectedSource)).Select(s => new PostListItem(s)).ToList();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Could not load posts");
            ErrorMessage = $"Could not load posts: {ex.Message}";
            _all = [];
        }

        ApplyFilter();
    }

    private void ApplyFilter()
    {
        var selectedId = SelectedPost?.Id;
        var filter = FilterText.Trim();
        var items = filter.Length == 0 ? _all : _all.Where(p => p.Matches(filter)).ToList();

        Posts.Clear();
        foreach (var item in items)
        {
            Posts.Add(item);
        }

        SelectedPost = Posts.FirstOrDefault(p => p.Id == selectedId) ?? Posts.FirstOrDefault();
        IsEmpty = _all.Count == 0;
        CountText = items.Count == _all.Count ? $"{_all.Count} posts" : $"{items.Count} of {_all.Count} posts";
    }

    private async Task LoadDetailAsync(PostListItem? item)
    {
        var version = ++_detailVersion;
        if (item is null)
        {
            Detail = null;
            return;
        }

        try
        {
            var post = await repository.GetPostAsync(item.Id);
            if (version == _detailVersion)
            {
                Detail = post is null ? null : new PostDetailViewModel(post, settings.DownloadsFolder, AutoPlayVideos);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Could not load post {Id}", item.Id);
            ErrorMessage = $"Could not load the post: {ex.Message}";
        }
    }

    public void Dispose() => Detail = null;
}
