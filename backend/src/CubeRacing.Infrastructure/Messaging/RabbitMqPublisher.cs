using System.Text;
using System.Text.Json;
using CubeRacing.Application.Interfaces;
using RabbitMQ.Client;

namespace CubeRacing.Infrastructure.Messaging;

public class RabbitMqPublisher : IMessagePublisher, IDisposable
{
    private readonly IConnection _connection;
    private readonly IModel _channel;
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    public RabbitMqPublisher(IConnectionFactory factory)
    {
        _connection = factory.CreateConnection();
        _channel = _connection.CreateModel();
    }

    public async Task PublishAsync<T>(string queue, T message, CancellationToken ct = default)
    {
        await _semaphore.WaitAsync(ct);
        try
        {
            _channel.QueueDeclare(queue, durable: true, exclusive: false, autoDelete: false);
            var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(message));
            var props = _channel.CreateBasicProperties();
            props.Persistent = true;
            _channel.BasicPublish("", queue, props, body);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public void Dispose()
    {
        _semaphore.Dispose();
        _channel.Dispose();
        _connection.Dispose();
    }
}
