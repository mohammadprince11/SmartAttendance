using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;

namespace SmartAttendance.Web.Infrastructure.Security;

/// <summary>
/// مفاتيح WebAuthn/Passkeys للموظفين (بصمة/وجه الجهاز): جدول
/// <c>EmployeeWebAuthnCredentials</c> يخزّن المفتاح العام فقط (لا صور ولا قوالب
/// بيولوجية — البيولوجيا تبقى داخل جهاز الموظف). دورة الحياة: تسجيل جديد بحالة
/// «معلّق» ⟵ اعتماد HR يفعّله ⟵ قاعدة «مفتاح نشط واحد لكل موظف» (أي تسجيل جديد
/// يزيح القديم لحالة «مُستبدَل» ويحتاج اعتماداً جديداً).
/// </summary>
public static class WebAuthnCredentialStore
{
    public const string StatusPending = "Pending";       // بانتظار اعتماد HR
    public const string StatusActive = "Active";         // معتمد وصالح للبصم/الدخول
    public const string StatusRejected = "Rejected";     // رفضه HR
    public const string StatusSuperseded = "Superseded"; // أزاحه تسجيل أحدث
    public const string StatusRevoked = "Revoked";       // ألغاه HR بعد الاعتماد

    public sealed class Credential
    {
        public int Id { get; set; }
        public int EmployeeId { get; set; }
        public string CredentialId { get; set; } = string.Empty; // Base64Url
        public string PublicKey { get; set; } = string.Empty;    // Base64 (COSE)
        public long SignCount { get; set; }
        public string? DeviceLabel { get; set; }
        public string Status { get; set; } = StatusPending;
        public DateTime CreatedAt { get; set; }
        public string? ApprovedBy { get; set; }
        public DateTime? ApprovedAt { get; set; }
        public DateTime? LastUsedAt { get; set; }
        public string? AaGuid { get; set; }
        public string EmployeeNo { get; set; } = string.Empty;
        public string EmployeeName { get; set; } = string.Empty;

        public string StatusText => StatusArabic(Status);
    }

    // ===== منطق نقي (قابل للاختبار بلا قاعدة) =====

    /// <summary>Base64Url (بلا حشو) — صيغة معرّفات WebAuthn القياسية.</summary>
    public static string ToBase64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// <summary>فكّ Base64Url إلى بايتات (يعيد الحشو المحذوف).</summary>
    public static byte[] FromBase64Url(string value)
    {
        var s = value.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(s.PadRight(s.Length + (4 - s.Length % 4) % 4, '='));
    }

    /// <summary>هل المفتاح بهذه الحالة صالح للبصم/الدخول؟ (المعتمد فقط)</summary>
    public static bool IsUsable(string status) => status == StatusActive;

    /// <summary>هل تسجيل جديد يُزيح مفتاحاً بهذه الحالة؟ (المعلّق والنشط — «مفتاح واحد»)</summary>
    public static bool SupersededByNewRegistration(string status) =>
        status is StatusPending or StatusActive;

    /// <summary>هل يجوز اعتماد/رفض مفتاح بهذه الحالة؟ (المعلّق فقط قابل للبتّ)</summary>
    public static bool CanDecide(string status) => status == StatusPending;

    /// <summary>التسمية العربية للحالة.</summary>
    public static string StatusArabic(string status) => status switch
    {
        StatusPending => "بانتظار الاعتماد",
        StatusActive => "نشط",
        StatusRejected => "مرفوض",
        StatusSuperseded => "مُستبدَل",
        StatusRevoked => "مُلغى",
        _ => status
    };

    // ===== المخطط =====

    public static async Task EnsureAsync(ApplicationDbContext db)
    {
        await HrmsDatabase.ExecuteAsync(
            db,
            """
IF OBJECT_ID('EmployeeWebAuthnCredentials', 'U') IS NULL
BEGIN
    CREATE TABLE EmployeeWebAuthnCredentials
    (
        Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
        EmployeeId int NOT NULL,
        CredentialId nvarchar(600) NOT NULL,
        PublicKey nvarchar(max) NOT NULL,
        SignCount bigint NOT NULL DEFAULT(0),
        DeviceLabel nvarchar(200) NULL,
        Status nvarchar(20) NOT NULL DEFAULT('Pending'),
        CreatedAt datetime2 NOT NULL DEFAULT(SYSUTCDATETIME()),
        ApprovedBy nvarchar(100) NULL,
        ApprovedAt datetime2 NULL,
        LastUsedAt datetime2 NULL,
        AaGuid nvarchar(64) NULL
    );
    CREATE UNIQUE INDEX UX_EmployeeWebAuthnCredentials_CredentialId
        ON EmployeeWebAuthnCredentials (CredentialId);
    CREATE INDEX IX_EmployeeWebAuthnCredentials_Employee
        ON EmployeeWebAuthnCredentials (EmployeeId, Status);
END;
""");
    }

    // ===== العمليات =====

    /// <summary>
    /// تسجيل مفتاح جديد بحالة «معلّق»: يُزيح أي مفتاح معلّق/نشط سابق للموظف
    /// (قاعدة المفتاح الواحد) ويُشعر HR بطلب الاعتماد. يرفض معرّف مفتاح مكرراً.
    /// </summary>
    public static async Task<(bool Ok, string Message)> RegisterAsync(
        ApplicationDbContext db, int employeeId, string credentialIdB64Url,
        string publicKeyB64, long signCount, string? deviceLabel, string? aaGuid)
    {
        await EnsureAsync(db);

        var duplicate = await HrmsDatabase.ScalarAsync<int>(
            db,
            "SELECT COUNT(1) FROM EmployeeWebAuthnCredentials WHERE CredentialId = @Cred;",
            c => HrmsDatabase.AddParameter(c, "@Cred", credentialIdB64Url));
        if (duplicate > 0)
            return (false, "هذا المفتاح مسجَّل مسبقاً في النظام.");

        await HrmsDatabase.ExecuteAsync(
            db,
            $"""
UPDATE EmployeeWebAuthnCredentials
SET Status = '{StatusSuperseded}'
WHERE EmployeeId = @Emp AND Status IN ('{StatusPending}', '{StatusActive}');

INSERT INTO EmployeeWebAuthnCredentials
    (EmployeeId, CredentialId, PublicKey, SignCount, DeviceLabel, Status, AaGuid)
VALUES
    (@Emp, @Cred, @Key, @Count, @Label, '{StatusPending}', @AaGuid);

INSERT INTO SystemNotifications (Title, Message, TargetRole, Url)
SELECT N'طلب اعتماد بصمة/وجه',
       N'سجّل الموظف ' + ISNULL(e.FullName, N'') + N' مفتاح بصمة/وجه جديداً بانتظار اعتماد الموارد البشرية.',
       'HR', '/BiometricKeys'
FROM Employees e WHERE e.Id = @Emp;
""",
            c =>
            {
                HrmsDatabase.AddParameter(c, "@Emp", employeeId);
                HrmsDatabase.AddParameter(c, "@Cred", credentialIdB64Url);
                HrmsDatabase.AddParameter(c, "@Key", publicKeyB64);
                HrmsDatabase.AddParameter(c, "@Count", signCount);
                HrmsDatabase.AddParameter(c, "@Label", (object?)deviceLabel ?? DBNull.Value);
                HrmsDatabase.AddParameter(c, "@AaGuid", (object?)aaGuid ?? DBNull.Value);
            });

        return (true, "سُجّل المفتاح وهو الآن بانتظار اعتماد الموارد البشرية.");
    }

    private const string SelectColumns =
        """
SELECT c.Id, c.EmployeeId, c.CredentialId, c.PublicKey, c.SignCount, c.DeviceLabel,
       c.Status, c.CreatedAt, c.ApprovedBy, c.ApprovedAt, c.LastUsedAt, c.AaGuid,
       ISNULL(e.EmployeeNo, N'') AS EmployeeNo, ISNULL(e.FullName, N'') AS FullName
FROM EmployeeWebAuthnCredentials c
LEFT JOIN Employees e ON e.Id = c.EmployeeId
""";

    private static Credential Map(System.Data.Common.DbDataReader reader) => new()
    {
        Id = HrmsDatabase.GetInt(reader, "Id"),
        EmployeeId = HrmsDatabase.GetInt(reader, "EmployeeId"),
        CredentialId = HrmsDatabase.GetString(reader, "CredentialId"),
        PublicKey = HrmsDatabase.GetString(reader, "PublicKey"),
        SignCount = reader["SignCount"] is long l ? l : Convert.ToInt64(reader["SignCount"]),
        DeviceLabel = HrmsDatabase.GetString(reader, "DeviceLabel"),
        Status = HrmsDatabase.GetString(reader, "Status"),
        CreatedAt = HrmsDatabase.GetDateTime(reader, "CreatedAt") ?? default,
        ApprovedBy = HrmsDatabase.GetString(reader, "ApprovedBy"),
        ApprovedAt = HrmsDatabase.GetDateTime(reader, "ApprovedAt"),
        LastUsedAt = HrmsDatabase.GetDateTime(reader, "LastUsedAt"),
        AaGuid = HrmsDatabase.GetString(reader, "AaGuid"),
        EmployeeNo = HrmsDatabase.GetString(reader, "EmployeeNo"),
        EmployeeName = HrmsDatabase.GetString(reader, "FullName")
    };

    /// <summary>مفاتيح موظف واحد (كل الحالات، الأحدث أولاً) — لعرضها بدرج إعداداته.</summary>
    public static async Task<List<Credential>> ListForEmployeeAsync(ApplicationDbContext db, int employeeId)
    {
        await EnsureAsync(db);
        return await HrmsDatabase.QueryAsync(
            db,
            SelectColumns + " WHERE c.EmployeeId = @Emp ORDER BY c.Id DESC;",
            c => HrmsDatabase.AddParameter(c, "@Emp", employeeId),
            Map);
    }

    /// <summary>المفتاح النشط (المعتمد) للموظف، أو null.</summary>
    public static async Task<Credential?> GetActiveForEmployeeAsync(ApplicationDbContext db, int employeeId)
    {
        await EnsureAsync(db);
        var rows = await HrmsDatabase.QueryAsync(
            db,
            SelectColumns + $" WHERE c.EmployeeId = @Emp AND c.Status = '{StatusActive}';",
            c => HrmsDatabase.AddParameter(c, "@Emp", employeeId),
            Map);
        return rows.FirstOrDefault();
    }

    /// <summary>هل للموظف مفتاح نشط؟ (لإنفاذ التأكيد البيولوجي لحظة البصم)</summary>
    public static async Task<bool> HasActiveForEmployeeAsync(ApplicationDbContext db, int employeeId)
    {
        await EnsureAsync(db);
        return await HrmsDatabase.ScalarAsync<int>(
            db,
            $"SELECT COUNT(1) FROM EmployeeWebAuthnCredentials WHERE EmployeeId = @Emp AND Status = '{StatusActive}';",
            c => HrmsDatabase.AddParameter(c, "@Emp", employeeId)) > 0;
    }

    /// <summary>إيجاد مفتاح بمعرّفه (للدخول بالمفتاح والتحقق من التفرّد).</summary>
    public static async Task<Credential?> FindByCredentialIdAsync(ApplicationDbContext db, string credentialIdB64Url)
    {
        await EnsureAsync(db);
        var rows = await HrmsDatabase.QueryAsync(
            db,
            SelectColumns + " WHERE c.CredentialId = @Cred;",
            c => HrmsDatabase.AddParameter(c, "@Cred", credentialIdB64Url),
            Map);
        return rows.FirstOrDefault();
    }

    /// <summary>كل المفاتيح (لشاشة HR) — المعلّق أولاً ثم الأحدث.</summary>
    public static async Task<List<Credential>> ListAllAsync(ApplicationDbContext db, string? status = null)
    {
        await EnsureAsync(db);
        var where = string.IsNullOrWhiteSpace(status) ? "" : " WHERE c.Status = @Status";
        return await HrmsDatabase.QueryAsync(
            db,
            SelectColumns + where + $"""
 ORDER BY CASE c.Status WHEN '{StatusPending}' THEN 0 ELSE 1 END, c.Id DESC;
""",
            c =>
            {
                if (!string.IsNullOrWhiteSpace(status))
                    HrmsDatabase.AddParameter(c, "@Status", status);
            },
            Map);
    }

    /// <summary>
    /// اعتماد HR: «معلّق» ⟵ «نشط» مع إزاحة أي نشط آخر لنفس الموظف (ضمانة أخيرة
    /// لقاعدة المفتاح الواحد حتى لو تسابقت الطلبات).
    /// </summary>
    public static async Task<bool> ApproveAsync(ApplicationDbContext db, int id, string approvedBy)
    {
        await EnsureAsync(db);
        return await HrmsDatabase.ScalarAsync<int>(
            db,
            $"""
UPDATE c SET c.Status = '{StatusSuperseded}'
FROM EmployeeWebAuthnCredentials c
INNER JOIN EmployeeWebAuthnCredentials t ON t.Id = @Id AND t.Status = '{StatusPending}'
WHERE c.EmployeeId = t.EmployeeId AND c.Id <> t.Id AND c.Status = '{StatusActive}';

UPDATE EmployeeWebAuthnCredentials
SET Status = '{StatusActive}', ApprovedBy = @By, ApprovedAt = SYSUTCDATETIME()
WHERE Id = @Id AND Status = '{StatusPending}';
SELECT @@ROWCOUNT;
""",
            c =>
            {
                HrmsDatabase.AddParameter(c, "@Id", id);
                HrmsDatabase.AddParameter(c, "@By", approvedBy);
            }) > 0;
    }

    /// <summary>رفض HR لمفتاح معلّق.</summary>
    public static async Task<bool> RejectAsync(ApplicationDbContext db, int id, string decidedBy)
    {
        await EnsureAsync(db);
        return await HrmsDatabase.ScalarAsync<int>(
            db,
            $"""
UPDATE EmployeeWebAuthnCredentials
SET Status = '{StatusRejected}', ApprovedBy = @By, ApprovedAt = SYSUTCDATETIME()
WHERE Id = @Id AND Status = '{StatusPending}';
SELECT @@ROWCOUNT;
""",
            c =>
            {
                HrmsDatabase.AddParameter(c, "@Id", id);
                HrmsDatabase.AddParameter(c, "@By", decidedBy);
            }) > 0;
    }

    /// <summary>إلغاء HR لمفتاح نشط أو معلّق (فقدان جهاز/إنهاء خدمة...).</summary>
    public static async Task<bool> RevokeAsync(ApplicationDbContext db, int id, string decidedBy)
    {
        await EnsureAsync(db);
        return await HrmsDatabase.ScalarAsync<int>(
            db,
            $"""
UPDATE EmployeeWebAuthnCredentials
SET Status = '{StatusRevoked}', ApprovedBy = @By, ApprovedAt = SYSUTCDATETIME()
WHERE Id = @Id AND Status IN ('{StatusPending}', '{StatusActive}');
SELECT @@ROWCOUNT;
""",
            c =>
            {
                HrmsDatabase.AddParameter(c, "@Id", id);
                HrmsDatabase.AddParameter(c, "@By", decidedBy);
            }) > 0;
    }

    /// <summary>تحديث عدّاد التوقيعات وآخر استخدام بعد تحقق ناجح (حماية الاستنساخ).</summary>
    public static Task UpdateSignCountAsync(ApplicationDbContext db, int id, long signCount) =>
        HrmsDatabase.ExecuteAsync(
            db,
            "UPDATE EmployeeWebAuthnCredentials SET SignCount = @Count, LastUsedAt = SYSUTCDATETIME() WHERE Id = @Id;",
            c =>
            {
                HrmsDatabase.AddParameter(c, "@Id", id);
                HrmsDatabase.AddParameter(c, "@Count", signCount);
            });
}
