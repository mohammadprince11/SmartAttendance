using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace SmartAttendance.Web.Infrastructure.Security;

public static class SimplePasswordHasher
{
    private const string CurrentAlgorithm = "PBKDF2-SHA256";
    private const int CurrentIterations = 210_000;
    private const int SaltLength = 32;
    private const int DerivedKeyLength = 32;

    private const string DummySalt =
        "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=";

    private const string DummyHash =
        "PBKDF2-SHA256$210000$" +
        "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=";

    public static string CreateSalt()
    {
        var bytes = RandomNumberGenerator.GetBytes(SaltLength);
        return Convert.ToBase64String(bytes);
    }

    public static string HashPassword(string password, string salt)
    {
        ArgumentNullException.ThrowIfNull(password);
        ArgumentException.ThrowIfNullOrWhiteSpace(salt);

        var saltBytes = Convert.FromBase64String(salt);
        var derivedKey = Rfc2898DeriveBytes.Pbkdf2(
            password,
            saltBytes,
            CurrentIterations,
            HashAlgorithmName.SHA256,
            DerivedKeyLength);

        return $"{CurrentAlgorithm}$" +
               $"{CurrentIterations.ToString(CultureInfo.InvariantCulture)}$" +
               Convert.ToBase64String(derivedKey);
    }

    public static bool Verify(
        string password,
        string salt,
        string expectedHash)
    {
        if (string.IsNullOrEmpty(password) ||
            string.IsNullOrWhiteSpace(salt) ||
            string.IsNullOrWhiteSpace(expectedHash))
        {
            return false;
        }

        try
        {
            if (TryReadPbkdf2Hash(
                    expectedHash,
                    out var iterations,
                    out var expectedBytes))
            {
                var actualBytes = Rfc2898DeriveBytes.Pbkdf2(
                    password,
                    Convert.FromBase64String(salt),
                    iterations,
                    HashAlgorithmName.SHA256,
                    expectedBytes.Length);

                return CryptographicOperations.FixedTimeEquals(
                    actualBytes,
                    expectedBytes);
            }

            return VerifyLegacySha256(password, salt, expectedHash);
        }
        catch (FormatException)
        {
            return false;
        }
        catch (CryptographicException)
        {
            return false;
        }
    }

    public static void PerformDummyVerification(string password)
    {
        _ = Verify(password, DummySalt, DummyHash);
    }

    public static bool NeedsRehash(string expectedHash)
    {
        try
        {
            if (!TryReadPbkdf2Hash(
                    expectedHash,
                    out var iterations,
                    out var expectedBytes))
            {
                return true;
            }

            return iterations < CurrentIterations ||
                   expectedBytes.Length != DerivedKeyLength;
        }
        catch (FormatException)
        {
            return true;
        }
    }

    private static bool VerifyLegacySha256(
        string password,
        string salt,
        string expectedHash)
    {
        var input = $"{salt}:{password}";
        var actualBytes = SHA256.HashData(
            Encoding.UTF8.GetBytes(input));
        var expectedBytes = Convert.FromBase64String(expectedHash);

        return CryptographicOperations.FixedTimeEquals(
            actualBytes,
            expectedBytes);
    }

    private static bool TryReadPbkdf2Hash(
        string expectedHash,
        out int iterations,
        out byte[] expectedBytes)
    {
        iterations = 0;
        expectedBytes = Array.Empty<byte>();

        var parts = expectedHash.Split(
            '$',
            StringSplitOptions.None);

        if (parts.Length != 3 ||
            !string.Equals(
                parts[0],
                CurrentAlgorithm,
                StringComparison.Ordinal) ||
            !int.TryParse(
                parts[1],
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out iterations) ||
            iterations <= 0)
        {
            return false;
        }

        expectedBytes = Convert.FromBase64String(parts[2]);
        return expectedBytes.Length > 0;
    }
}
