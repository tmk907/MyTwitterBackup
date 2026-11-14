using CommunityToolkit.Mvvm.ComponentModel;
using MyTwitterBackup.Core.Database;
using MyTwitterBackup.Core.Models;

namespace MyTwitterBackup.Desktop.ViewModels;

public partial class QueueItemViewModel : ObservableObject
{
    public QueueItemViewModel(QueueItem item)
    {
        Id = item.Id;
        PostId = item.PostId;
        SourceType = item.SourceType;
        Update(item);
    }

    public int Id { get; }

    public string PostId { get; }

    public SourceType SourceType { get; }

    public string SourceText => SourceType switch
    {
        SourceType.Bookmark => "Bookmark",
        SourceType.Like => "Like",
        _ => "Single",
    };

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Title))]
    public partial string? UserName { get; set; }

    [ObservableProperty]
    public partial string? PreviewText { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText), nameof(StatusGlyph))]
    public partial QueueStatus Status { get; set; }

    [ObservableProperty]
    public partial string? Error { get; set; }

    public string Title => string.IsNullOrEmpty(UserName) ? $"Post {PostId}" : $"@{UserName} - {PostId}";

    public string StatusText => Status switch
    {
        QueueStatus.Pending => "Pending",
        QueueStatus.Downloading => "Downloading",
        QueueStatus.Completed => "Completed",
        QueueStatus.Failed => "Failed",
        QueueStatus.MissingData => "Missing data",
        _ => Status.ToString(),
    };

    public string StatusGlyph => Status switch
    {
        QueueStatus.Pending => "",     // Clock
        QueueStatus.Downloading => "", // Download
        QueueStatus.Completed => "",   // CheckMark
        QueueStatus.Failed => "",      // Error
        QueueStatus.MissingData => "", // Unknown
        _ => "",
    };

    /// <summary>Used as the accessible name of the list item.</summary>
    public override string ToString() => $"{Title}, {SourceText}, {StatusText}";

    public void Update(QueueItem item)
    {
        UserName = item.UserName;
        PreviewText = item.PreviewText;
        Status = item.Status;
        Error = item.Error;
    }
}
