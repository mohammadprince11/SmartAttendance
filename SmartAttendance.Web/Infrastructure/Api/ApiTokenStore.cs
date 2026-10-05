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
        public int TenantId { get; set; }
        public string TenantCode { get; set; } = string.Empty;
        public int SystemUserId { get; set; }
        public int? EmployeeId { get; set; }
        public string Username { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;

        /// <summary>ختم أمان الحساب وقت الإصدار — يُقارَن بالحالي عند كل طلب.</summary>
        public string SecurityStamp { get; set; } = string.Empty;
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
        TenantId int NOT NULL,
        SystemUserId int NOT NULL,
        EmployeeId int NULL,
        Username nvarchar(150) NOT NULL,
        Role nvarchar(60) NOT NULL,
        DisplayName nvarchar(200) NULL,
        ExpiresAt datetime2 NOT NULL,
        CreatedAt datetime2 NOT NULL DEFAULT(SYSUTCDATETIME()),
        RevokedAt datetime2 NULL,
        SecurityStamp nvarchar(64) NULL
    );
    CREATE UNIQUE INDEX UX_ApiTokens_Hash ON ApiTokens (TokenHash);
END;

IF COL_LENGTH('dbo.ApiTokens', 'TenantId') IS NULL
    THROW 51000, 'ApiTokens.TenantId is missing. Apply the controlled tenant migration before starting the application.', 1;
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
INSERT INTO ApiTokens (TokenHash, TenantId, SystemUserId, EmployeeId, Username, Role, DisplayName, ExpiresAt, SecurityStamp)
VALUES (@Hash, @TenantId, @Sys, @Emp, @User, @Role, @Name, @Exp, @Stamp);
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@Hash", hash);
                HrmsDatabase.AddParameter(command, "@TenantId", identity.TenantId);
                HrmsDatabase.AddParameter(command, "@Sys", identity.SystemUserId);
                HrmsDatabase.AddParameter(command, "@Emp", (object?)identity.EmployeeId ?? DBNull.Value);
                HrmsDatabase.AddParameter(command, "@User", identity.Username);
                HrmsDatabase.AddParameter(command, "@Role", identity.Role);
                HrmsDatabase.AddParameter(command, "@Name", (object?)identity.DisplayName ?? DBNull.Value);
                HrmsDatabase.AddParameter(command, "@Exp", expires);
                HrmsDatabase.AddParameter(command, "@Stamp", identity.SecurityStamp ?? string.Empty);
            });

        return token;
    }

    /// <summary>
    /// يتحقق من توكن ويرجع هويته، أو null إن كان غير صالح/منتهٍ/ملغى.
    /// <b>مسار ساخن</b>: يُنادى بكل طلب Bearer، فلا DDL هنا — مخطط <c>ApiTokens</c>
    /// مضمونٌ عند الإقلاع (<see cref="EnsureAsync"/> بـ<c>Program</c>). التحقّق بذرةٌ
    /// مفهرسة على <c>UX_ApiTokens_Hash</c> ثم فلترة <c>RevokedAt/ExpiresAt</c> على صفٍّ واحد.
    /// </summary>
    public static async Task<TokenIdentity?> ValidateAsync(ApplicationDbContext db, string token)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;
        var hash = Hash(token);

        return (await HrmsDatabase.QueryAsync(
            db,
            """
SELECT t.TenantId, tn.Code AS TenantCode, t.SystemUserId, t.EmployeeId, t.Username, t.Role,
       t.DisplayName, ISNULL(t.SecurityStamp, '') AS SecurityStamp
FROM ApiTokens t
INNER JOIN Tenants tn ON tn.Id = t.TenantId AND tn.IsActive = 1 AND tn.IsDeleted = 0
INNER JOIN TenantLicenses license ON license.TenantId = t.TenantId
    AND license.Status IN (N'Trial', N'Active')
    AND license.StartsAtUtc <= SYSUTCDATETIME()
    AND (license.ExpiresAtUtc IS NULL
         OR license.ExpiresAtUtc >= SYSUTCDATETIME()
         OR license.GraceEndsAtUtc >= SYSUTCDATETIME())
    AND EXISTS
    (
        SELECT 1
        FROM STRING_SPLIT(license.EnabledModulesCsv, N',') enabledModule
        WHERE LTRIM(RTRIM(enabledModule.value)) = N'Mobile'
    )
WHERE t.TokenHash = @Hash AND t.RevokedAt IS NULL AND t.ExpiresAt > SYSUTCDATETIME();
""",
            command => HrmsDatabase.AddParameter(command, "@Hash", hash),
            reader => new TokenIdentity
            {
                TenantId = HrmsDatabase.GetInt(reader, "TenantId"),
                TenantCode = HrmsDatabase.GetString(reader, "TenantCode"),
                SystemUserId = HrmsDatabase.GetInt(reader, "SystemUserId"),
                EmployeeId = HrmsDatabase.GetNullableInt(reader, "EmployeeId"),
                Username = HrmsDatabase.GetString(reader, "Username"),
                Role = HrmsDatabase.GetString(reader, "Role"),
                DisplayName = HrmsDatabase.GetString(reader, "DisplayName"),
                SecurityStamp = HrmsDatabase.GetString(reader, "SecurityStamp")
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
