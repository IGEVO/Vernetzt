using Igevo.Vernetzt.Server.Security;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Igevo.Vernetzt.Tests.TestInfrastructure;

/// <summary>Lets a test client call the public API like the public portal does.</summary>
public static class PublicApiTestAccess
{
    public static HttpClient WithPublicApiKey(this HttpClient client, WebApplicationFactory<Program> factory)
    {
        var key = factory.Services.GetRequiredService<IConfiguration>()[PublicApiAccessMiddleware.ConfigurationKey];
        client.DefaultRequestHeaders.Add(PublicApiAccessMiddleware.HeaderName, key);
        return client;
    }
}
