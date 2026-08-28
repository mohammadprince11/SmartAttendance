using System.Net;
using System.Net.Sockets;

namespace SmartAttendance.Web.Infrastructure.Integrations;

/// <summary>حارس SSRF لمخارج الـwebhook؛ لا يسمح بعنوان داخلي أو بروتوكول غير TLS.</summary>
public static class WebhookEndpointPolicy
{
    public static bool IsAllowed(Uri? endpoint)
    {
        if (endpoint is null || !endpoint.IsAbsoluteUri || endpoint.Scheme != Uri.UriSchemeHttps)
            return false;
        if (!string.IsNullOrEmpty(endpoint.UserInfo) || endpoint.IsLoopback)
            return false;
        return !IPAddress.TryParse(endpoint.Host, out var address) || IsPublic(address);
    }

    public static bool IsPublic(IPAddress address)
    {
        if (IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) ||
            address.Equals(IPAddress.IPv6Any) || address.Equals(IPAddress.None))
            return false;

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6Multicast)
                return false;
            var ipv6 = address.GetAddressBytes();
            if ((ipv6[0] & 0xFE) == 0xFC) return false; // fc00::/7 unique-local
            if (!address.IsIPv4MappedToIPv6) return true;
        }

        var bytes = address.MapToIPv4().GetAddressBytes();
        return !(bytes[0] == 10 ||
                 bytes[0] == 127 ||
                 bytes[0] == 0 ||
                 bytes[0] == 169 && bytes[1] == 254 ||
                 bytes[0] == 172 && bytes[1] is >= 16 and <= 31 ||
                 bytes[0] == 192 && bytes[1] == 168 ||
                 bytes[0] >= 224);
    }
}
