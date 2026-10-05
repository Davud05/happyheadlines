using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
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
        app.UseSerilogRequestLogging(options =>
        {
            options.GetLevel = (ctx, _, ex) =>
                ex is not null || ctx.Response.StatusCode >= 500 ? LogEventLevel.Error
                : IsInfrastructurePath(ctx) ? LogEventLevel.Verbose
                : LogEventLevel.Information;
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
