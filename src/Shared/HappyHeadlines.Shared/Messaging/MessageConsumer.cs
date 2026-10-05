using System.Diagnostics;
using System.Text;
using System.Text.Json;
using HappyHeadlines.Shared.Observability;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Context.Propagation;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace HappyHeadlines.Shared.Messaging;

/// <summary>
/// Consumes a durable queue bound to a fanout exchange. Instances of the same service share the
/// queue (competing consumers), while different services each get their own copy of every message.
/// </summary>
public abstract class MessageConsumer<T>(
    RabbitMqConnection connection,
    IServiceScopeFactory scopeFactory,
    ILogger logger,
    string exchange,
    string queue) : BackgroundService
{
    private IChannel? _channel;

    protected abstract Task HandleAsync(T message, IServiceProvider services, CancellationToken ct);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var conn = await connection.GetAsync(stoppingToken);
        _channel = await conn.CreateChannelAsync(cancellationToken: stoppingToken);

        await RabbitMqConnection.DeclareExchangeAsync(_channel, exchange, stoppingToken);
        await _channel.QueueDeclareAsync(queue, durable: true, exclusive: false, autoDelete: false, cancellationToken: stoppingToken);
        await _channel.QueueBindAsync(queue, exchange, routingKey: "", cancellationToken: stoppingToken);
        await _channel.BasicQosAsync(prefetchSize: 0, prefetchCount: 10, global: false, stoppingToken);

        var consumer = new AsyncEventingBasicConsumer(_channel);
        consumer.ReceivedAsync += (_, delivery) => OnReceivedAsync(delivery, stoppingToken);
        await _channel.BasicConsumeAsync(queue, autoAck: false, consumer, stoppingToken);

        logger.LogInformation("Consuming {Queue} bound to exchange {Exchange}", queue, exchange);
    }

    private async Task OnReceivedAsync(BasicDeliverEventArgs delivery, CancellationToken ct)
    {
        var parent = Propagators.DefaultTextMapPropagator.Extract(default, delivery.BasicProperties.Headers, ReadHeader);
        Baggage.Current = parent.Baggage;

        using var activity = Telemetry.Messaging.StartActivity($"{queue} process", ActivityKind.Consumer, parent.ActivityContext);
        activity?.SetTag("messaging.system", "rabbitmq");
        activity?.SetTag("messaging.destination.name", exchange);
        activity?.SetTag("messaging.consumer.group.name", queue);

        try
        {
            var message = JsonSerializer.Deserialize<T>(delivery.Body.Span)
                ?? throw new InvalidOperationException("Message body was empty.");

            await using var scope = scopeFactory.CreateAsyncScope();
            await HandleAsync(message, scope.ServiceProvider, ct);
            await _channel!.BasicAckAsync(delivery.DeliveryTag, multiple: false, ct);
        }
        catch (Exception ex)
        {
            activity?.AddException(ex);
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);

            // Retry once by requeueing; drop the message if it fails again so it cannot block the queue.
            var requeue = !delivery.Redelivered;
            logger.LogError(ex, "Failed to process message from {Queue}. Requeue: {Requeue}", queue, requeue);
            await _channel!.BasicNackAsync(delivery.DeliveryTag, multiple: false, requeue, ct);
        }
    }

    private static IEnumerable<string> ReadHeader(IDictionary<string, object?>? headers, string key) =>
        headers is not null && headers.TryGetValue(key, out var value) && value is byte[] bytes
            ? [Encoding.UTF8.GetString(bytes)]
            : [];

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);
        if (_channel is not null) await _channel.DisposeAsync();
    }
}
