using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace SmartAttendance.Web.Infrastructure.Security;

public static class TotpSecurity
{
    private const int Digits = 6;
    private const int PeriodSeconds = 30;
    private const string Base32Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    public static string GenerateSecret()
    {
        var bytes = RandomNumberGenerator.GetBytes(20);
        return Base32Encode(bytes);
    }

    public static string BuildOtpAuthUri(string issuer, string account, string secret)
    {
        var escapedIssuer = Uri.EscapeDataString(issuer);
        var escapedAccount = Uri.EscapeDataString(account);
        return $"otpauth://totp/{escapedIssuer}:{escapedAccount}" +
               $"?secret={secret}&issuer={escapedIssuer}&digits={Digits}&period={PeriodSeconds}";
    }
    public static bool ValidateCode(
        string secret,
        string? code,
        DateTimeOffset? now = null)
    {
        var normalized = NormalizeDigits(code);
        if (normalized.Length != Digits || !int.TryParse(normalized, out var supplied))
            return false;

        var timestamp = (now ?? DateTimeOffset.UtcNow).ToUnixTimeSeconds();
        var counter = timestamp / PeriodSeconds;

        for (var offset = -1; offset <= 1; offset++)
        {
            if (ComputeCode(secret, counter + offset) == supplied)
                return true;
        }

        return false;
    }

    public static IReadOnlyList<string> GenerateRecoveryCodes(int count = 8)
    {
        if (count is < 1 or > 20)
            throw new ArgumentOutOfRangeException(nameof(count));

        var result = new List<string>(count);
        while (result.Count < count)
        {
            var raw = Base32Encode(RandomNumberGenerator.GetBytes(8));
            var code = $"{raw[..4]}-{raw[4..8]}-{raw[8..13]}";
            if (!result.Contains(code, StringComparer.Ordinal))
                result.Add(code);
        }

        return result;
    }
    public static string HashRecoveryCode(string code)
    {
        var normalized = NormalizeRecoveryCode(code);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return Convert.ToHexString(hash);
    }

    public static string NormalizeRecoveryCode(string? code) =>
        new((code ?? string.Empty)
            .Where(char.IsLetterOrDigit)
            .Select(char.ToUpperInvariant)
            .ToArray());

    private static int ComputeCode(string secret, long counter)
    {
        var key = Base32Decode(secret);
        Span<byte> counterBytes = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(counterBytes, counter);

        using var hmac = new HMACSHA1(key);
        var hash = hmac.ComputeHash(counterBytes.ToArray());
        var offset = hash[^1] & 0x0F;
        var binary =
            ((hash[offset] & 0x7F) << 24) |
            ((hash[offset + 1] & 0xFF) << 16) |
            ((hash[offset + 2] & 0xFF) << 8) |
            (hash[offset + 3] & 0xFF);

        return binary % 1_000_000;
    }
    private static string NormalizeDigits(string? value) =>
        new((value ?? string.Empty).Where(char.IsDigit).ToArray());

    private static string Base32Encode(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty) return string.Empty;

        var output = new StringBuilder((data.Length * 8 + 4) / 5);
        var buffer = 0;
        var bitsLeft = 0;

        foreach (var b in data)
        {
            buffer = (buffer << 8) | b;
            bitsLeft += 8;

            while (bitsLeft >= 5)
            {
                output.Append(Base32Alphabet[(buffer >> (bitsLeft - 5)) & 31]);
                bitsLeft -= 5;
            }
        }

        if (bitsLeft > 0)
            output.Append(Base32Alphabet[(buffer << (5 - bitsLeft)) & 31]);

        return output.ToString();
    }
    private static byte[] Base32Decode(string value)
    {
        var normalized = value.Trim().TrimEnd('=').ToUpperInvariant();
        var result = new List<byte>(normalized.Length * 5 / 8);
        var buffer = 0;
        var bitsLeft = 0;

        foreach (var c in normalized)
        {
            var index = Base32Alphabet.IndexOf(c);
            if (index < 0)
                throw new FormatException("Invalid Base32 TOTP secret.");

            buffer = (buffer << 5) | index;
            bitsLeft += 5;

            if (bitsLeft >= 8)
            {
                result.Add((byte)((buffer >> (bitsLeft - 8)) & 0xFF));
                bitsLeft -= 8;
            }
        }

        return result.ToArray();
    }
}
