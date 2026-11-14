using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using MyTwitterBackup.Core.Database;
using MyTwitterBackup.Core.Services;
using MyTwitterBackup.Desktop.Services;
using Serilog;

namespace MyTwitterBackup.Desktop.ViewModels;

public partial class QueueViewModel(DownloadQueueService queue) : ObservableObject
{
    private readonly Dictionary<int, QueueItemViewModel> _itemsById = [];
    private bool _reloadScheduled;

    public ObservableCollection<QueueItemViewModel> Items { get; } = [];

    [ObservableProperty]
    public partial QueueCounts Counts { get; set; } = new(0, 0, 0, 0, 0);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PauseResumeLabel), nameof(PauseResumeGlyph), nameof(StateText))]
    public partial bool IsPaused { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StateText))]
    public partial string? CurrentItemText { get; set; }

    [ObservableProperty]
    public partial bool IsEmpty { get; set; } = true;

    [ObservableProperty]
    public partial string? StatusMessage { get; set; }

    public string PauseResumeLabel => IsPaused ? "Resume" : "Pause";

    public string PauseResumeGlyph => IsPaused ? "" : "";

    public string StateText => IsPaused
        ? "Paused"
        : CurrentItemText is null ? "Idle - waiting for posts" : $"Downloading {CurrentItemText}";

    public async Task ActivateAsync()
    {
        queue.ItemUpdated += OnItemUpdated;
        queue.QueueChanged += OnQueueChanged;
        queue.StateChanged += OnStateChanged;
        UpdateState();
        await LoadAsync();
    }

    public void Deactivate()
    {
        queue.ItemUpdated -= OnItemUpdated;
        queue.QueueChanged -= OnQueueChanged;
        queue.StateChanged -= OnStateChanged;
    }

    private async Task LoadAsync()
    {
        try
        {
            var items = await queue.GetItemsAsync();
            Items.Clear();
            _itemsById.Clear();
            foreach (var item in items)
            {
                var vm = new QueueItemViewModel(item);
                Items.Add(vm);
                _itemsById[item.Id] = vm;
            }

            await RefreshCountsAsync();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Could not load the queue");
            StatusMessage = $"Could not load the queue: {ex.Message}";
        }
    }

    private async Task RefreshCountsAsync()
    {
        Counts = await queue.GetCountsAsync();
        IsEmpty = Counts.Total == 0;
    }

    [RelayCommand]
    private void PauseResume()
    {
        if (queue.IsPaused)
        {
            queue.Resume();
        }
        else
        {
            queue.Pause();
        }
    }

    [RelayCommand]
    private async Task RetryFailedAsync()
    {
        var count = await queue.RetryFailedAsync();
        StatusMessage = $"{count} item(s) moved back to pending.";
    }

    [RelayCommand]
    private void FetchMissingData()
    {
        WeakReferenceMessenger.Default.Send(new ResolveMissingDataMessage());
    }

    [RelayCommand]
    private async Task ClearCompletedAsync() => await queue.ClearCompletedAsync();

    /// <summary>Clears the whole queue. The view asks for confirmation first.</summary>
    [RelayCommand]
    private async Task ClearAllAsync()
    {
        await queue.ClearAsync();
        StatusMessage = "The queue was cleared.";
    }

    private void OnItemUpdated(object? sender, QueueItem item)
    {
        App.MainDispatcher.TryEnqueue(async () =>
        {
            if (_itemsById.TryGetValue(item.Id, out var vm))
            {
                vm.Update(item);
            }

            await RefreshCountsAsync();
        });
    }

    private void OnQueueChanged(object? sender, EventArgs e)
    {
        App.MainDispatcher.TryEnqueue(async () =>
        {
            // Collapse bursts of changes into a single reload.
            if (_reloadScheduled)
            {
                return;
            }

            _reloadScheduled = true;
            await Task.Delay(300);
            _reloadScheduled = false;
            await LoadAsync();
        });
    }

    private void OnStateChanged(object? sender, EventArgs e) => App.MainDispatcher.TryEnqueue(UpdateState);

    private void UpdateState()
    {
        IsPaused = queue.IsPaused;
        var current = queue.CurrentItem;
        CurrentItemText = current is null
            ? null
            : string.IsNullOrEmpty(current.UserName) ? current.PostId : $"@{current.UserName} - {current.PostId}";
    }
}
