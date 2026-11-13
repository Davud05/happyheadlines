using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;
using Serilog.Events;

namespace HappyHeadlines.Shared.Observability;

/// <summary>
/// Central logging, tracing and metrics setup shared by every service in the architecture.
/// Logs go to Seq, traces and metrics go to the OpenTelemetry Collector.
/// </summary>
public static class ObservabilityExtensions
{
    public static WebApplicationBuilder AddObservability(this WebApplicationBuilder builder, string serviceName)
    {
        var seqUrl = builder.Configuration["Seq:Url"] ?? "http://localhost:5341";
        var otlpEndpoint = new Uri(builder.Configuration["Otlp:Endpoint"] ?? "http://localhost:4317");
        var instanceId = builder.Configuration["INSTANCE_ID"] ?? Environment.MachineName;
        var sampleRatio = builder.Configuration.GetValue("Otlp:SampleRatio", 1.0);

        builder.Services.AddSerilog(config => config
            .MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
            .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning)
            .MinimumLevel.Override("System.Net.Http.HttpClient", LogEventLevel.Warning)
            .MinimumLevel.Override("Polly", LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .Enrich.WithProperty("Service", serviceName)
            .Enrich.WithProperty("Instance", instanceId)
            .WriteTo.Console(outputTemplate:
                "[{Timestamp:HH:mm:ss} {Level:u3}] {Service}/{Instance} {Message:lj} {TraceId}{NewLine}{Exception}")
            .WriteTo.Seq(seqUrl));

        builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(serviceName, serviceInstanceId: instanceId))
            .WithTracing(tracing => tracing
                // Head sampling: the first service decides, downstream services follow the parent so traces stay whole.
                .SetSampler(new ParentBasedSampler(new TraceIdRatioBasedSampler(sampleRatio)))
                .AddSource(serviceName)
                .AddSource(Telemetry.MessagingSourceName)
                .AddAspNetCoreInstrumentation(options => options.Filter = ctx => !IsInfrastructurePath(ctx))
                .AddHttpClientInstrumentation()
                .AddNpgsql()
                .AddOtlpExporter(options => options.Endpoint = otlpEndpoint))
            .WithMetrics(metrics => metrics
                .AddMeter(CacheMetrics.MeterName)
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddOtlpExporter(options => options.Endpoint = otlpEndpoint));

        builder.Services.AddHealthChecks();
        return builder;
    }

    public static WebApplication UseObservability(this WebApplication app)
    {
        // Successful requests are already counted by the metrics and sampled by the traces,
        // so only failed and slow requests are worth a log event.
        var slowRequestMs = app.Configuration.GetValue("Logging:SlowRequestMs", 1000.0);
        app.UseSerilogRequestLogging(options =>
        {
            options.GetLevel = (ctx, elapsedMs, ex) =>
                ex is not null || ctx.Response.StatusCode >= 500 ? LogEventLevel.Error
                : elapsedMs > slowRequestMs && !IsInfrastructurePath(ctx) ? LogEventLevel.Warning
                : LogEventLevel.Debug;
        });
        app.MapHealthChecks("/health");
        return app;
    }

    private static bool IsInfrastructurePath(HttpContext ctx) =>
        ctx.Request.Path.StartsWithSegments("/health")
        || ctx.Request.Path.StartsWithSegments("/lib")
        || ctx.Request.Path.StartsWithSegments("/css")
        || ctx.Request.Path.StartsWithSegments("/js");
}
