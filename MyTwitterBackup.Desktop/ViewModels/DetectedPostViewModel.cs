using CommunityToolkit.Mvvm.ComponentModel;
using MyTwitterBackup.Core.Models;
using MyTwitterBackup.Core.Services;

namespace MyTwitterBackup.Desktop.ViewModels;

/// <summary>A post detected on the current page (Browser side panel).</summary>
public partial class DetectedPostViewModel(string postId, string? userName) : ObservableObject
{
    public string PostId { get; } = postId;

    public string? UserName { get; private set; } = userName;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasData), nameof(Title), nameof(Snippet), nameof(DataStatusText))]
    public partial TweetData? Data { get; set; }

    [ObservableProperty]
    public partial bool IsQueued { get; set; }

    public bool HasData => Data is not null;

    public string Title
    {
        get
        {
            var user = Data?.UserName ?? UserName;
            if (Data is { DisplayName.Length: > 0 })
            {
                return $"{Data.DisplayName} @{user}";
            }

            return string.IsNullOrEmpty(user) ? $"Post {PostId}" : $"@{user}";
        }
    }

    public string Snippet => Data is null
        ? $"Post {PostId}"
        : DownloadQueueService.Snippet(Data.Text, 90) is { Length: > 0 } s ? s : $"{Data.Media.Count} media";

    public string DataStatusText => HasData ? "Data captured" : "Data not captured yet";

    /// <summary>Used as the accessible name of the list item.</summary>
    public override string ToString() => $"{Title}, {DataStatusText}";

    partial void OnDataChanged(TweetData? value)
    {
        if (value is not null && !string.IsNullOrEmpty(value.UserName))
        {
            UserName = value.UserName;
        }
    }
}
