using System.Security.Cryptography;
using System.Text;

namespace Igevo.Vernetzt.Server.Security;

/// <summary>
/// Restricts the anonymous public API (<c>/api/public/*</c>) to the public portal. The portal's server
/// sends a shared secret in <see cref="HeaderName"/>; browsers never talk to this API directly.
/// A shared secret instead of an IP check, because the portal and the outer reverse proxy usually
/// reach the server from the same address (loopback or the same Docker network).
/// Requests without a valid key get a 404, so the API does not reveal that it exists. Without a
/// configured key (see <see cref="IsUsableKey"/>) the public API is closed for everyone.
/// </summary>
public sealed class PublicApiAccessMiddleware
{
    public const string HeaderName = "X-Vernetzt-Api-Key";
    public const string ConfigurationKey = "PublicApi:AccessKey";
    public const int MinimumKeyLength = 32;

    private static readonly PathString PublicApiPath = new("/api/public");

    private readonly RequestDelegate _next;
    private readonly byte[]? _expectedKey;

    public PublicApiAccessMiddleware(RequestDelegate next, IConfiguration configuration)
    {
        _next = next;
        var configuredKey = configuration[ConfigurationKey];
        _expectedKey = IsUsableKey(configuredKey) ? Encoding.UTF8.GetBytes(configuredKey!) : null;
    }

    /// <summary>A committed placeholder ("CHANGE-THIS-...") or a short value counts as not configured.</summary>
    public static bool IsUsableKey(string? key) =>
        !string.IsNullOrWhiteSpace(key)
        && !key.StartsWith("CHANGE-THIS", StringComparison.OrdinalIgnoreCase)
        && key.Length >= MinimumKeyLength;

    public Task InvokeAsync(HttpContext context)
    {
        // StartsWithSegments ignores case, matching the case-insensitive routing.
        if (!context.Request.Path.StartsWithSegments(PublicApiPath) || HasValidKey(context.Request))
        {
            return _next(context);
        }

        context.Response.StatusCode = StatusCodes.Status404NotFound;
        return Task.CompletedTask;
    }

    private bool HasValidKey(HttpRequest request)
    {
        if (_expectedKey is null) return false;

        var provided = request.Headers[HeaderName];
        if (provided.Count != 1 || string.IsNullOrEmpty(provided[0])) return false;

        return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(provided[0]!), _expectedKey);
    }
}
