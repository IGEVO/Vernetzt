using System.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.ServiceDiscovery;
using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Microsoft.Extensions.Hosting;

/// <summary>
/// Adds common Aspire services: service discovery, resilience, health checks, and OpenTelemetry.
/// This project should be referenced by each service project in your solution.
/// </summary>
public static class Extensions
{
    private const string HealthEndpointPath = "/health";
    private const string AlivenessEndpointPath = "/alive";

    public static TBuilder AddServiceDefaults<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.ConfigureOpenTelemetry();

        builder.AddDefaultHealthChecks();

        builder.Services.AddServiceDiscovery();

        builder.Services.ConfigureHttpClientDefaults(http =>
        {
            http.AddStandardResilienceHandler();
            http.AddServiceDiscovery();
        });

        return builder;
    }

    public static TBuilder ConfigureOpenTelemetry<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.Logging.AddOpenTelemetry(logging =>
        {
            logging.IncludeFormattedMessage = true;
            logging.IncludeScopes = true;
        });

        builder.Services.AddOpenTelemetry()
            // service.name comes from OTEL_SERVICE_NAME; the version allows comparing releases in Grafana
            .ConfigureResource(resource => resource.AddAttributes(
            [
                new KeyValuePair<string, object>("service.version", GetServiceVersion())
            ]))
            .WithMetrics(metrics =>
            {
                metrics.AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddRuntimeInstrumentation();
            })
            .WithTracing(tracing =>
            {
                tracing.AddSource(builder.Environment.ApplicationName)
                    .AddAspNetCoreInstrumentation(tracing =>
                        tracing.Filter = context =>
                            !context.Request.Path.StartsWithSegments(HealthEndpointPath)
                            && !context.Request.Path.StartsWithSegments(AlivenessEndpointPath)
                    )
                    .AddHttpClientInstrumentation();
            });

        builder.AddOpenTelemetryExporters();

        return builder;
    }

    private static string GetServiceVersion()
    {
        var assembly = Assembly.GetEntryAssembly();
        var version = assembly?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? assembly?.GetName().Version?.ToString()
            ?? "unknown";

        // "1.2.3+abcdef": keep the build metadata out, the git hash is exported separately if needed
        var plus = version.IndexOf('+');
        return plus > 0 ? version[..plus] : version;
    }

    /// <summary>
    /// OTLP export: <c>OTEL_EXPORTER_OTLP_ENDPOINT</c> (in production the LGTM stack, under Aspire its dashboard).
    /// <c>Observability:AdditionalOtlpEndpoint</c> sends a copy of all signals to a second OTLP/gRPC receiver,
    /// e.g. the LGTM container started by the Aspire AppHost next to the Aspire dashboard.
    /// </summary>
    private static TBuilder AddOpenTelemetryExporters<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        var useOtlpExporter = !string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]);
        var additionalEndpoint = builder.Configuration["Observability:AdditionalOtlpEndpoint"];

        if (string.IsNullOrWhiteSpace(additionalEndpoint))
        {
            if (useOtlpExporter)
            {
                builder.Services.AddOpenTelemetry().UseOtlpExporter();
            }

            return builder;
        }

        // UseOtlpExporter supports a single destination only, so both exporters are registered per signal.
        // The unnamed exporter reads the OTEL_EXPORTER_OTLP_* variables, the named one gets its endpoint here.
        const string additional = "additional";
        void ConfigureAdditional(OtlpExporterOptions options)
        {
            options.Endpoint = new Uri(additionalEndpoint);
            options.Protocol = OtlpExportProtocol.Grpc;
        }

        builder.Services.AddOpenTelemetry()
            .WithLogging(logging =>
            {
                if (useOtlpExporter)
                {
                    logging.AddOtlpExporter();
                }

                logging.AddOtlpExporter(additional, ConfigureAdditional);
            })
            .WithMetrics(metrics =>
            {
                if (useOtlpExporter)
                {
                    metrics.AddOtlpExporter();
                }

                metrics.AddOtlpExporter(additional, ConfigureAdditional);
            })
            .WithTracing(tracing =>
            {
                if (useOtlpExporter)
                {
                    tracing.AddOtlpExporter();
                }

                tracing.AddOtlpExporter(additional, ConfigureAdditional);
            });

        return builder;
    }

    public static TBuilder AddDefaultHealthChecks<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.Services.AddHealthChecks()
            .AddCheck("self", () => HealthCheckResult.Healthy(), ["live"]);

        return builder;
    }

    public static WebApplication MapDefaultEndpoints(this WebApplication app)
    {
        if (app.Environment.IsDevelopment())
        {
            app.MapHealthChecks(HealthEndpointPath).AllowAnonymous();

            app.MapHealthChecks(AlivenessEndpointPath, new HealthCheckOptions
            {
                Predicate = r => r.Tags.Contains("live")
            }).AllowAnonymous();
        }

        return app;
    }
}
