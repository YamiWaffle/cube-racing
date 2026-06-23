namespace CubeRacing.Application.Interfaces;

public interface ISessionCompletionSignal
{
    Task WaitAsync(CancellationToken ct);
    void Signal();
}
