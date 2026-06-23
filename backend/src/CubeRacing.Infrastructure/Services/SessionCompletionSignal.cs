using CubeRacing.Application.Interfaces;

namespace CubeRacing.Infrastructure.Services;

public class SessionCompletionSignal : ISessionCompletionSignal
{
    private readonly SemaphoreSlim _semaphore = new(0, 1);

    public Task WaitAsync(CancellationToken ct) => _semaphore.WaitAsync(ct);

    public void Signal()
    {
        // Release only if count is 0 (idempotent — prevents double-release exception)
        if (_semaphore.CurrentCount == 0)
            _semaphore.Release();
    }
}
