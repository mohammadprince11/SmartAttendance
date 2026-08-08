using System.Security.Cryptography;
using System.Text;
using SmartAttendance.Web.Infrastructure.Security;
using Xunit;

namespace SmartAttendance.Tests;

/// <summary>
/// PR6 #6 — التجزئة القديمة (SHA256 بلا تمديد مفتاح) تُقبل للتوافق لكنها تُعلَّم
/// «تحتاج ترقية» حتى يعيد مسارا الدخول (الباك-أوفيس وAPI) تجزئتها بـPBKDF2 بعد
/// نجاح التحقق. هذه الاختبارات تثبّت الدلالة التي يعتمدها الوصلان.
/// </summary>
public sealed class SimplePasswordHasherTests
{
    private const string Password = "S3cret-Pass!";
    private const string Salt = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=";

    /// <summary>تجزئة قديمة حرفياً كما ينتجها النظام السابق: Base64(SHA256("salt:password")).</summary>
    private static string LegacySha256Hash(string password, string salt) =>
        Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes($"{salt}:{password}")));

    [Fact]
    public void LegacySha256_VerifiesForBackwardCompatibility()
    {
        var legacy = LegacySha256Hash(Password, Salt);
        Assert.True(SimplePasswordHasher.Verify(Password, Salt, legacy));
    }

    [Fact]
    public void LegacySha256_WrongPassword_Fails()
    {
        var legacy = LegacySha256Hash(Password, Salt);
        Assert.False(SimplePasswordHasher.Verify("wrong", Salt, legacy));
    }

    [Fact]
    public void LegacySha256_IsFlaggedForRehash()
    {
        var legacy = LegacySha256Hash(Password, Salt);
        Assert.True(SimplePasswordHasher.NeedsRehash(legacy));
    }

    [Fact]
    public void Pbkdf2Hash_VerifiesAndDoesNotNeedRehash()
    {
        var salt = SimplePasswordHasher.CreateSalt();
        var hash = SimplePasswordHasher.HashPassword(Password, salt);

        Assert.True(SimplePasswordHasher.Verify(Password, salt, hash));
        Assert.False(SimplePasswordHasher.NeedsRehash(hash));
    }

    [Fact]
    public void UpgradedHash_StillVerifiesSamePassword()
    {
        // محاكاة الترقية: تحقّق قديم ناجح ⟹ إعادة تجزئة بـPBKDF2 ⟹ التحقق يبقى صحيحاً.
        var legacy = LegacySha256Hash(Password, Salt);
        Assert.True(SimplePasswordHasher.Verify(Password, Salt, legacy));

        var newSalt = SimplePasswordHasher.CreateSalt();
        var upgraded = SimplePasswordHasher.HashPassword(Password, newSalt);

        Assert.True(SimplePasswordHasher.Verify(Password, newSalt, upgraded));
        Assert.False(SimplePasswordHasher.NeedsRehash(upgraded));
    }
}
