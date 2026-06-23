namespace CubeRacing.Application.Interfaces;

public interface IMessagePublisher
{
    Task PublishAsync<T>(string queue, T message, CancellationToken ct = default);
}
