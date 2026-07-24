using System.Security.Cryptography;
using System.Text;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;

namespace SmartAttendance.Web.Infrastructure.Api;

/// <summary>
/// توكنات واجهة الموبايل (Bearer): بدل الكوكيز، يصدر توكناً معتماً عند تسجيل الدخول
/// يُخزَّن <b>مجزّأً (SHA-256)</b> بجدول self-healing، ويُتحقَّق منه بكل طلب API.
/// مصمَّم على نمط المشروع (جداول ذاتية الترميم، بلا حزم JWT خارجية). يخدم التطبيق
/// النيتف للأساسيات (ملف الموظف/الطلبات/البصم...).
/// </summary>
public static class ApiTokenStore
{
    /// <summary>هوية مالك التوكن المستخرجة عند التحقق (تبني ClaimsPrincipal).</summary>
    public sealed class TokenIdentity
    {
        public int SystemUserId { get; set; }
        public int? EmployeeId { get; set; }
        public string Username { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
    }

    public static async Task EnsureAsync(ApplicationDbContext db)
    {
        await HrmsDatabase.ExecuteAsync(
            db,
            """
IF OBJECT_ID('ApiTokens', 'U') IS NULL
BEGIN
    CREATE TABLE ApiTokens
    (
        Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
        TokenHash char(64) NOT NULL,
        SystemUserId int NOT NULL,
        EmployeeId int NULL,
        Username nvarchar(150) NOT NULL,
        Role nvarchar(60) NOT NULL,
        DisplayName nvarchar(200) NULL,
        ExpiresAt datetime2 NOT NULL,
        CreatedAt datetime2 NOT NULL DEFAULT(SYSUTCDATETIME()),
        RevokedAt datetime2 NULL
    );
    CREATE UNIQUE INDEX UX_ApiTokens_Hash ON ApiTokens (TokenHash);
END;
""");
    }

    /// <summary>يصدر توكناً جديداً ويرجع نصّه العلني (يُخزَّن مجزّأً فقط).</summary>
    public static async Task<string> IssueAsync(
        ApplicationDbContext db, TokenIdentity identity, TimeSpan lifetime)
    {
        await EnsureAsync(db);

        var raw = RandomNumberGenerator.GetBytes(32);
        var token = Convert.ToBase64String(raw).Replace('+', '-').Replace('/', '_').TrimEnd('=');
        var hash = Hash(token);
        var expires = DateTime.UtcNow.Add(lifetime);

        await HrmsDatabase.ExecuteAsync(
            db,
            """
INSERT INTO ApiTokens (TokenHash, SystemUserId, EmployeeId, Username, Role, DisplayName, ExpiresAt)
VALUES (@Hash, @Sys, @Emp, @User, @Role, @Name, @Exp);
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@Hash", hash);
                HrmsDatabase.AddParameter(command, "@Sys", identity.SystemUserId);
                HrmsDatabase.AddParameter(command, "@Emp", (object?)identity.EmployeeId ?? DBNull.Value);
                HrmsDatabase.AddParameter(command, "@User", identity.Username);
                HrmsDatabase.AddParameter(command, "@Role", identity.Role);
                HrmsDatabase.AddParameter(command, "@Name", (object?)identity.DisplayName ?? DBNull.Value);
                HrmsDatabase.AddParameter(command, "@Exp", expires);
            });

        return token;
    }

    /// <summary>يتحقق من توكن ويرجع هويته، أو null إن كان غير صالح/منتهٍ/ملغى.</summary>
    public static async Task<TokenIdentity?> ValidateAsync(ApplicationDbContext db, string token)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;
        await EnsureAsync(db);
        var hash = Hash(token);

        return (await HrmsDatabase.QueryAsync(
            db,
            """
SELECT SystemUserId, EmployeeId, Username, Role, DisplayName
FROM ApiTokens
WHERE TokenHash = @Hash AND RevokedAt IS NULL AND ExpiresAt > SYSUTCDATETIME();
""",
            command => HrmsDatabase.AddParameter(command, "@Hash", hash),
            reader => new TokenIdentity
            {
                SystemUserId = HrmsDatabase.GetInt(reader, "SystemUserId"),
                EmployeeId = HrmsDatabase.GetNullableInt(reader, "EmployeeId"),
                Username = HrmsDatabase.GetString(reader, "Username"),
                Role = HrmsDatabase.GetString(reader, "Role"),
                DisplayName = HrmsDatabase.GetString(reader, "DisplayName")
            })).FirstOrDefault();
    }

    public static async Task RevokeAsync(ApplicationDbContext db, string token)
    {
        if (string.IsNullOrWhiteSpace(token)) return;
        await EnsureAsync(db);
        await HrmsDatabase.ExecuteAsync(
            db,
            "UPDATE ApiTokens SET RevokedAt = SYSUTCDATETIME() WHERE TokenHash = @Hash AND RevokedAt IS NULL;",
            command => HrmsDatabase.AddParameter(command, "@Hash", Hash(token)));
    }

    private static string Hash(string token)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
