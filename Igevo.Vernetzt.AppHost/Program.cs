using System.Security.Cryptography;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

var builder = DistributedApplication.CreateBuilder(args);

// The database is a SQLite file of the server (Igevo.Vernetzt.Server/App_Data/vernetzt.db), no container needed.

// Two separate Mailpit instances: the relay deletes every message it fetches via POP3,
// so system emails (login links, notifications) need their own inbox.

// System emails (login links, notifications)
var mailpitSystem = builder.AddContainer("mailpit-system", "axllent/mailpit", "latest")
    .WithHttpEndpoint(targetPort: 8025, port: 56080, name: "web")
    .WithEndpoint(targetPort: 1025, port: 56025, name: "smtp")
    .WithLifetime(ContainerLifetime.Persistent);

// Email relay inbox (polled via POP3 by the relay background service)
var mailpitRelay = builder.AddContainer("mailpit-relay", "axllent/mailpit", "latest")
    .WithHttpEndpoint(targetPort: 8025, port: 58025, name: "web")
    .WithEndpoint(targetPort: 1025, port: 56026, name: "smtp")
    .WithEndpoint(targetPort: 1110, port: 56110, name: "pop3")
    .WithEnvironment("MP_POP3_BIND_ADDR", "0.0.0.0:1110")
    .WithEnvironment("MP_POP3_AUTH", "relay:relay")
    .WithLifetime(ContainerLifetime.Persistent);

// Server application
var systemSmtpEndpoint = mailpitSystem.GetEndpoint("smtp");
var mailpitPop3Endpoint = mailpitRelay.GetEndpoint("pop3");
var mailpitSmtpEndpoint = mailpitRelay.GetEndpoint("smtp");
// Shared key that restricts the public API to the public portal; new for every AppHost start.
var publicApiAccessKey = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));

var server = builder.AddProject<Projects.Igevo_Vernetzt_Server>("server")
    .WithEnvironment("PublicApi__AccessKey", publicApiAccessKey)
    // HTTPS (dev certificate) for clients that refuse credentials over plain HTTP, e.g. macOS/iOS
    // Contacts with CardDAV: https://localhost:7142/carddav/. The web app keeps using HTTP via Vite.
    .WithHttpsEndpoint(port: 7142, name: "https")
    // System email: send via Mailpit (system) SMTP
    .WithEnvironment("Email__Smtp__Host", systemSmtpEndpoint.Property(EndpointProperty.Host))
    .WithEnvironment("Email__Smtp__Port", systemSmtpEndpoint.Property(EndpointProperty.Port))
    .WithEnvironment("Email__Smtp__UseSsl", "false")
    // Email relay: monitor Mailpit (relay) via POP3 for incoming relay emails
    .WithEnvironment("EmailRelay__Enabled", "true")
    .WithEnvironment("EmailRelay__MonitorMode", "Pop3")
    .WithEnvironment("EmailRelay__Pop3__Host", mailpitPop3Endpoint.Property(EndpointProperty.Host))
    .WithEnvironment("EmailRelay__Pop3__Port", mailpitPop3Endpoint.Property(EndpointProperty.Port))
    .WithEnvironment("EmailRelay__Pop3__UseSsl", "false")
    .WithEnvironment("EmailRelay__Pop3__Username", "relay")
    .WithEnvironment("EmailRelay__Pop3__Password", "relay")
    .WithEnvironment("EmailRelay__BaseAddress", "relay@vernetzt.local")
    // Relay SMTP: used when the relay service needs to send forwarded emails through Mailpit (relay)
    .WithEnvironment("EmailRelay__Smtp__Host", mailpitSmtpEndpoint.Property(EndpointProperty.Host))
    .WithEnvironment("EmailRelay__Smtp__Port", mailpitSmtpEndpoint.Property(EndpointProperty.Port))
    .WithEnvironment("EmailRelay__Smtp__UseSsl", "false");

// Grafana LGTM (Loki, Grafana, Tempo, Prometheus) with the Vernetzt dashboards from observability/grafana.
// The server keeps sending telemetry to the Aspire dashboard and sends a copy to LGTM.
// Turn off with Observability:Lgtm:Enabled=false (e.g. appsettings.Development.json or user secrets).
if (builder.Configuration.GetValue("Observability:Lgtm:Enabled", true))
{
    var lgtm = builder.AddContainer("lgtm", "grafana/otel-lgtm", "0.40.0")
        .WithHttpEndpoint(targetPort: 3000, port: 56300, name: "grafana")
        .WithEndpoint(targetPort: 4317, port: 56317, name: "otlp-grpc", scheme: "http")
        .WithBindMount("../observability/grafana/dashboards", "/var/lib/vernetzt-dashboards", isReadOnly: true)
        .WithBindMount(
            "../observability/grafana/provisioning/dashboards/vernetzt.yaml",
            "/otel-lgtm/grafana/conf/provisioning/dashboards/vernetzt.yaml",
            isReadOnly: true)
        .WithVolume("vernetzt-lgtm-dev-data", "/data")
        .WithLifetime(ContainerLifetime.Persistent)
        .WithUrlForEndpoint("grafana", url => url.DisplayText = "Grafana (Vernetzt-Dashboards)")
        .WithUrlForEndpoint("otlp-grpc", url => url.DisplayLocation = UrlDisplayLocation.DetailsOnly);

    // No WaitFor: the exporter buffers and retries, the server must not depend on the monitoring container.
    server.WithEnvironment("Observability__AdditionalOtlpEndpoint", lgtm.GetEndpoint("otlp-grpc"));
}

// Vite is the browser entry point in development and proxies API/help requests to the server.
builder.AddViteApp("webapp", "../Igevo.Vernetzt.WebApp")
    .WithEndpoint("http", endpoint => endpoint.Port = 5173)
    .WithReference(server)
    .WithEnvironment("VERNETZT_API_URL", server.GetEndpoint("http"))
    .WaitFor(server);

// Public Nuxt SSR portal. It is independently deployable, but runs against the same API in Aspire.
var publicWeb = builder.AddJavaScriptApp("public-web", "../Igevo.Vernetzt.PublicWeb", "dev")
    .WithHttpEndpoint(port: 5174, env: "PORT")
    .WithEnvironment("HOST", "0.0.0.0")
    .WithEnvironment("NUXT_API_BASE_URL", server.GetEndpoint("http"))
    .WithEnvironment("NUXT_API_ACCESS_KEY", publicApiAccessKey)
    .WithReference(server)
    .WaitFor(server);

publicWeb.WithEnvironment("NUXT_PUBLIC_SITE_URL", publicWeb.GetEndpoint("http"));
// Lets the admin UI link to the public page of clubs, departments and groups.
server.WithEnvironment("App__PublicWebUrl", publicWeb.GetEndpoint("http"));

builder.Build().Run();
