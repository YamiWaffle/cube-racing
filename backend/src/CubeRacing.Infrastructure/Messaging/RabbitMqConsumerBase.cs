using System.Text;
using Microsoft.Extensions.Hosting;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace CubeRacing.Infrastructure.Messaging;

public abstract class RabbitMqConsumerBase : BackgroundService
{
    private readonly IConnectionFactory _factory;
    private readonly string _queue;

    protected RabbitMqConsumerBase(IConnectionFactory factory, string queue)
    {
        _factory = factory;
        _queue = queue;
    }

    protected override Task ExecuteAsync(CancellationToken ct)
    {
        var connection = _factory.CreateConnection();
        var channel = connection.CreateModel();
        channel.QueueDeclare(_queue, durable: true, exclusive: false, autoDelete: false);
        channel.BasicQos(0, 1, false);

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.Received += async (_, ea) =>
        {
            var body = Encoding.UTF8.GetString(ea.Body.ToArray());
            int attempts = 0;
            while (attempts < 3)
            {
                try
                {
                    await HandleAsync(body, ct);
                    channel.BasicAck(ea.DeliveryTag, false);
                    return;
                }
                catch
                {
                    attempts++;
                    if (attempts < 3) await Task.Delay(1000, ct);
                }
            }
            channel.BasicNack(ea.DeliveryTag, false, requeue: false);
        };
        channel.BasicConsume(_queue, autoAck: false, consumer);

        ct.WaitHandle.WaitOne();
        channel.Dispose();
        connection.Dispose();
        return Task.CompletedTask;
    }

    protected abstract Task HandleAsync(string messageJson, CancellationToken ct);
}
