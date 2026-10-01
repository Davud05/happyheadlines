using Microsoft.Extensions.Http.Resilience;
using Polly;

namespace CommentService.Profanity;

public static class ProfanityResilience
{
    public static IServiceCollection AddProfanityClient(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddHttpClient<ProfanityClient>(client =>
                client.BaseAddress = new Uri(configuration["Services:Profanity"] ?? "http://localhost:8002"))
            .AddResilienceHandler("profanity", (pipeline, context) =>
            {
                var logger = context.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("ProfanityCircuitBreaker");

                // Outermost: total time budget for a call including retries.
                pipeline.AddTimeout(TimeSpan.FromSeconds(6));

                pipeline.AddRetry(new HttpRetryStrategyOptions
                {
                    MaxRetryAttempts = 2,
                    Delay = TimeSpan.FromMilliseconds(200),
                    BackoffType = DelayBackoffType.Exponential,
                    UseJitter = true,
                });

                pipeline.AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
                {
                    FailureRatio = 0.5,
                    MinimumThroughput = 4,
                    SamplingDuration = TimeSpan.FromSeconds(30),
                    BreakDuration = TimeSpan.FromSeconds(20),
                    OnOpened = args =>
                    {
                        logger.LogWarning("Circuit to ProfanityService OPENED for {BreakDuration}s", args.BreakDuration.TotalSeconds);
                        return default;
                    },
                    OnHalfOpened = _ =>
                    {
                        logger.LogInformation("Circuit to ProfanityService HALF-OPEN, sending a trial request");
                        return default;
                    },
                    OnClosed = _ =>
                    {
                        logger.LogInformation("Circuit to ProfanityService CLOSED, service is healthy again");
                        return default;
                    },
                });

                // Innermost: time limit for a single attempt.
                pipeline.AddTimeout(TimeSpan.FromSeconds(2));
            });

        return services;
    }
}
