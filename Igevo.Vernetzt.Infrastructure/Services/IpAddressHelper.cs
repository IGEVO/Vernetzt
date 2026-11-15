using System.Net;
using System.Net.Sockets;

namespace Igevo.Vernetzt.Infrastructure.Services;

/// <summary>
/// Shortens IP addresses for logs (data minimisation): IPv4 to /24, IPv6 to /48. Enough to recognise
/// a network for troubleshooting, without identifying a single connection.
/// </summary>
public static class IpAddressHelper
{
    public static string Anonymize(IPAddress? address)
    {
        if (address is null)
        {
            return "unknown";
        }

        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        var bytes = address.GetAddressBytes();
        var keep = address.AddressFamily == AddressFamily.InterNetwork ? 3 : 6;
        for (var i = keep; i < bytes.Length; i++)
        {
            bytes[i] = 0;
        }

        return new IPAddress(bytes).ToString();
    }
}
