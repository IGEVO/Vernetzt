using System.Net;
using System.Net.Sockets;
using Igevo.Vernetzt.Core.Interfaces.Services;

namespace Igevo.Vernetzt.Infrastructure.Services;

/// <summary>
/// Downloads iCal feeds over HTTPS. The URLs are entered by managers, so the server must not become a
/// proxy into the internal network: connections to loopback, private, link-local and similar addresses
/// are refused (checked on every connection, including after redirects and DNS changes).
/// </summary>
public sealed class HolidayFeedFetcher(HttpClient httpClient) : IHolidayFeedFetcher
{
    /// <summary>Maximum feed size.</summary>
    public const int MaxContentBytes = 2 * 1024 * 1024;

    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    public async Task<string> FetchAsync(Uri url, CancellationToken cancellationToken = default)
    {
        if (url.Scheme != Uri.UriSchemeHttps)
        {
            throw new HolidayFeedException("Nur HTTPS-Adressen sind erlaubt.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Accept.ParseAdd("text/calendar");
        request.Headers.Accept.ParseAdd("*/*;q=0.5");

        using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new HolidayFeedException($"Der Server antwortete mit Status {(int)response.StatusCode}.");
        }

        if (response.RequestMessage?.RequestUri is { } finalUri && finalUri.Scheme != Uri.UriSchemeHttps)
        {
            throw new HolidayFeedException("Weiterleitung auf eine unverschlüsselte Adresse ist nicht erlaubt.");
        }

        if (response.Content.Headers.ContentLength > MaxContentBytes)
        {
            throw new HolidayFeedException("Der Kalender ist zu groß.");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + read > MaxContentBytes)
            {
                throw new HolidayFeedException("Der Kalender ist zu groß.");
            }

            buffer.Write(chunk, 0, read);
        }

        buffer.Position = 0;
        using var reader = new StreamReader(buffer, detectEncodingFromByteOrderMarks: true);
        return await reader.ReadToEndAsync(cancellationToken);
    }

    /// <summary>
    /// Primary handler for the typed client: timeouts, limited redirects and the address check.
    /// </summary>
    public static SocketsHttpHandler CreateHandler(bool allowPrivateNetworks = false) => new()
    {
        AllowAutoRedirect = true,
        MaxAutomaticRedirections = 5,
        ConnectTimeout = TimeSpan.FromSeconds(10),
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        ConnectCallback = async (context, cancellationToken) =>
        {
            var addresses = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, cancellationToken);
            var allowed = addresses.Where(a => allowPrivateNetworks || IsPublicAddress(a)).ToArray();
            if (allowed.Length == 0)
            {
                throw new HolidayFeedException("Die Adresse zeigt auf ein internes Netzwerk und ist nicht erlaubt.");
            }

            var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            try
            {
                await socket.ConnectAsync(allowed, context.DnsEndPoint.Port, cancellationToken);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        }
    };

    /// <summary>
    /// True for globally routable addresses; false for loopback, private, link-local, CGNAT,
    /// unique-local, multicast and unspecified addresses.
    /// </summary>
    public static bool IsPublicAddress(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any) ||
            address.Equals(IPAddress.Broadcast))
        {
            return false;
        }

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var b = address.GetAddressBytes();
            return !(b[0] == 0
                || b[0] == 10
                || (b[0] == 100 && b[1] >= 64 && b[1] <= 127) // CGNAT
                || b[0] == 127
                || (b[0] == 169 && b[1] == 254)
                || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)
                || (b[0] == 192 && b[1] == 168)
                || (b[0] == 192 && b[1] == 0 && b[2] == 0)
                || (b[0] == 198 && (b[1] == 18 || b[1] == 19))
                || b[0] >= 224);
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            var b = address.GetAddressBytes();
            return !(address.IsIPv6LinkLocal
                || address.IsIPv6SiteLocal
                || address.IsIPv6Multicast
                || (b[0] & 0xFE) == 0xFC); // unique local fc00::/7
        }

        return false;
    }
}
