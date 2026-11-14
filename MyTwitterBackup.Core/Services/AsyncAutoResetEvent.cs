namespace MyTwitterBackup.Core.Services;

/// <summary>Wakes a single waiting worker. Multiple signals collapse into one.</summary>
internal sealed class AsyncAutoResetEvent
{
    private readonly SemaphoreSlim _semaphore = new(0, 1);

    public void Set()
    {
        try
        {
            _semaphore.Release();
        }
        catch (SemaphoreFullException)
        {
            // Already signaled.
        }
    }

    public Task WaitAsync(CancellationToken cancellationToken) => _semaphore.WaitAsync(cancellationToken);
}
