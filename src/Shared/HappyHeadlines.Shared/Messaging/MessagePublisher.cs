using System.Diagnostics;
using System.Text;
using System.Text.Json;
using HappyHeadlines.Shared.Observability;
using OpenTelemetry;
using OpenTelemetry.Context.Propagation;
using RabbitMQ.Client;

namespace HappyHeadlines.Shared.Messaging;

public sealed class MessagePublisher(RabbitMqConnection connection) : IAsyncDisposable
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    private IChannel? _channel;
    private readonly HashSet<string> _declared = [];

    public async Task PublishAsync<T>(string exchange, T message, CancellationToken ct = default)
    {
        using var activity = Telemetry.Messaging.StartActivity($"{exchange} publish", ActivityKind.Producer);
        activity?.SetTag("messaging.system", "rabbitmq");
        activity?.SetTag("messaging.destination.name", exchange);

        var properties = new BasicProperties
        {
            Persistent = true,
            ContentType = "application/json",
            Type = typeof(T).Name,
            Headers = new Dictionary<string, object?>(),
        };

        if (activity is not null)
        {
            Propagators.DefaultTextMapPropagator.Inject(
                new PropagationContext(activity.Context, Baggage.Current),
                properties.Headers,
                (headers, key, value) => headers[key] = value);
        }

        var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(message));

        await _lock.WaitAsync(ct);
        try
        {
            if (_channel is not { IsOpen: true })
            {
                var conn = await connection.GetAsync(ct);
                _channel = await conn.CreateChannelAsync(cancellationToken: ct);
                _declared.Clear();
            }

            if (_declared.Add(exchange))
                await RabbitMqConnection.DeclareExchangeAsync(_channel, exchange, ct);

            await _channel.BasicPublishAsync(exchange, routingKey: "", mandatory: false, properties, body, ct);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_channel is not null) await _channel.DisposeAsync();
    }
}
