using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;

namespace HappyHeadlines.Shared.Messaging;

public sealed class RabbitMqConnection(IConfiguration configuration, ILogger<RabbitMqConnection> logger) : IAsyncDisposable
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    private IConnection? _connection;

    public async Task<IConnection> GetAsync(CancellationToken ct)
    {
        if (_connection is { IsOpen: true }) return _connection;

        await _lock.WaitAsync(ct);
        try
        {
            if (_connection is { IsOpen: true }) return _connection;

            var factory = new ConnectionFactory
            {
                HostName = configuration["RabbitMq:Host"] ?? "localhost",
                UserName = configuration["RabbitMq:Username"] ?? "guest",
                Password = configuration["RabbitMq:Password"] ?? "guest",
                AutomaticRecoveryEnabled = true,
            };

            await StartupRetry.RunAsync(
                async () => _connection = await factory.CreateConnectionAsync(ct),
                logger, "Connecting to RabbitMQ");
            return _connection!;
        }
        finally
        {
            _lock.Release();
        }
    }

    public static Task DeclareExchangeAsync(IChannel channel, string exchange, CancellationToken ct) =>
        channel.ExchangeDeclareAsync(exchange, ExchangeType.Fanout, durable: true, autoDelete: false, cancellationToken: ct);

    public async ValueTask DisposeAsync()
    {
        if (_connection is not null) await _connection.DisposeAsync();
    }
}
