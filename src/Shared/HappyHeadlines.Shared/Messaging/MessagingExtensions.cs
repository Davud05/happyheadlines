using Microsoft.Extensions.DependencyInjection;

namespace HappyHeadlines.Shared.Messaging;

public static class MessagingExtensions
{
    public static IServiceCollection AddMessaging(this IServiceCollection services)
    {
        services.AddSingleton<RabbitMqConnection>();
        services.AddSingleton<MessagePublisher>();
        return services;
    }
}
