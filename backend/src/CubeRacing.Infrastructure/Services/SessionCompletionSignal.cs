using CubeRacing.Application.Interfaces;

namespace CubeRacing.Infrastructure.Services;

public class SessionCompletionSignal : ISessionCompletionSignal
{
    private TaskCompletionSource _tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task WaitAsync(CancellationToken ct) => _tcs.Task.WaitAsync(ct);

    public void Signal()
    {
        var old = Interlocked.Exchange(ref _tcs, new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));
        old.TrySetResult();
    }
}
