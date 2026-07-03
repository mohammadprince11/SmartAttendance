using System.Security.Cryptography;
using System.Text;

namespace SmartAttendance.Web.Infrastructure.Security;

public static class SimplePasswordHasher
{
    public static string CreateSalt()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes);
    }

    public static string HashPassword(string password, string salt)
    {
        var input = $"{salt}:{password}";
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToBase64String(bytes);
    }

    public static bool Verify(string password, string salt, string expectedHash)
    {
        var actualHash = HashPassword(password, salt);
        return CryptographicOperations.FixedTimeEquals(
            Convert.FromBase64String(actualHash),
            Convert.FromBase64String(expectedHash));
    }
}
