using System.Security.Cryptography;
using System.Text;

namespace SmartAttendance.Web.Infrastructure.Integrations;

public static class WebhookSignature
{
    public static string Sign(string secret, long unixTimestamp, string payload)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(secret);
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var digest = hmac.ComputeHash(Encoding.UTF8.GetBytes($"{unixTimestamp}.{payload}"));
        return "sha256=" + Convert.ToHexString(digest).ToLowerInvariant();
    }

    public static bool Verify(string secret, long unixTimestamp, string payload, string signature)
    {
        var expected = Encoding.ASCII.GetBytes(Sign(secret, unixTimestamp, payload));
        var supplied = Encoding.ASCII.GetBytes(signature ?? string.Empty);
        return expected.Length == supplied.Length && CryptographicOperations.FixedTimeEquals(expected, supplied);
    }
}
