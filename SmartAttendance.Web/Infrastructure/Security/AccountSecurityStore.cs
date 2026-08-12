using System.Security.Cryptography;
using Microsoft.Extensions.Caching.Memory;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;

namespace SmartAttendance.Web.Infrastructure.Security;

/// <summary>
/// المرحلة 5 — قراءة/تبديل ختم الأمان بجدول <c>AppLoginUsers</c>.
///
/// القراءة تمرّ بكاش قصير (<see cref="StateCacheLifetime"/>) لأن الفحص يجري بكل
/// طلب مصادَق: بلا كاش يعني ضربة قاعدة لكل صورة وملف CSS. الكاش يُبطَّل فوراً
/// عند أي تبديل ختم، فأقصى نافذة بقاء لجلسة ملغاة = عمر الكاش.
/// </summary>
public static class AccountSecurityStore
{
    public static readonly TimeSpan StateCacheLifetime = TimeSpan.FromSeconds(60);

    public const string SecurityStampClaimType = "SecurityStamp";

    private static string CacheKey(string username) =>
        "account:security:" + username.Trim().ToLowerInvariant();

    public static string CreateStamp() =>
        Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();

    /// <summary>يقرأ حالة الحساب الحالية (بكاش قصير). يرجع Missing إذا لم يوجد.</summary>
    public static async Task<AccountSecurityState> GetStateAsync(
        ApplicationDbContext dbContext,
        IMemoryCache cache,
        string? username)
    {
        if (string.IsNullOrWhiteSpace(username))
        {
            return AccountSecurityState.Missing;
        }

        var key = CacheKey(username);

        if (cache.TryGetValue<AccountSecurityState>(key, out var cached) && cached is not null)
        {
            return cached;
        }

        var state = await ReadStateAsync(dbContext, username);
        cache.Set(key, state, StateCacheLifetime);
        return state;
    }

    public static void InvalidateCache(IMemoryCache cache, string? username)
    {
        if (!string.IsNullOrWhiteSpace(username))
        {
            cache.Remove(CacheKey(username));
        }
    }

    private static async Task<AccountSecurityState> ReadStateAsync(
        ApplicationDbContext dbContext,
        string username)
    {
        var rows = await HrmsDatabase.QueryAsync(
            dbContext,
            """
SELECT TOP 1
    u.Role,
    u.IsActive,
    u.LockoutEndUtc,
    ISNULL(u.SecurityStamp, '') AS SecurityStamp,
    ISNULL(u.MustChangePassword, 0) AS MustChangePassword
FROM AppLoginUsers u
WHERE u.Username = @Username;
""",
            command => HrmsDatabase.AddParameter(command, "@Username", username.Trim()),
            reader => new
            {
                Role = HrmsDatabase.GetString(reader, "Role"),
                IsActive = HrmsDatabase.GetBool(reader, "IsActive"),
                LockoutEndUtc = HrmsDatabase.GetDateTime(reader, "LockoutEndUtc"),
                SecurityStamp = HrmsDatabase.GetString(reader, "SecurityStamp"),
                MustChangePassword = HrmsDatabase.GetBool(reader, "MustChangePassword")
            });

        var row = rows.FirstOrDefault();

        if (row is null)
        {
            return AccountSecurityState.Missing;
        }

        var lockedOut = row.LockoutEndUtc.HasValue &&
                        row.LockoutEndUtc.Value > DateTime.UtcNow;

        return new AccountSecurityState(
            Exists: true,
            IsActive: row.IsActive,
            IsLockedOut: lockedOut,
            Role: row.Role,
            SecurityStamp: string.IsNullOrWhiteSpace(row.SecurityStamp) ? null : row.SecurityStamp,
            MustChangePassword: row.MustChangePassword);
    }

    /// <summary>
    /// يضمن وجود ختم للحساب (يولّده إن غاب) ويرجعه — يُستدعى لحظة إصدار كوكي/توكن.
    /// </summary>
    public static async Task<string> EnsureStampAsync(
        ApplicationDbContext dbContext,
        int loginUserId)
    {
        var existing = await HrmsDatabase.QueryAsync(
            dbContext,
            "SELECT TOP 1 ISNULL(SecurityStamp, '') AS SecurityStamp FROM AppLoginUsers WHERE Id = @Id;",
            command => HrmsDatabase.AddParameter(command, "@Id", loginUserId),
            reader => HrmsDatabase.GetString(reader, "SecurityStamp"));

        var current = existing.FirstOrDefault();

        if (!string.IsNullOrWhiteSpace(current))
        {
            return current;
        }

        var stamp = CreateStamp();

        await HrmsDatabase.ExecuteAsync(
            dbContext,
            """
UPDATE AppLoginUsers
SET SecurityStamp = @Stamp,
    UpdatedAt = SYSUTCDATETIME()
WHERE Id = @Id AND (SecurityStamp IS NULL OR SecurityStamp = '');
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@Stamp", stamp);
                HrmsDatabase.AddParameter(command, "@Id", loginUserId);
            });

        return stamp;
    }

    /// <summary>
    /// يبدّل الختم ⟹ تُرفض كل الكوكيز الصادرة، ويُلغي توكنات الـAPI الحيّة لنفس
    /// الحساب فوراً (لا ينتظر انتهاءها). يُستدعى عند تغيير كلمة المرور/الدور/
    /// التعطيل/تعديل الحساب. يرجع الختم الجديد.
    /// </summary>
    public static async Task<string> BumpStampAsync(
        ApplicationDbContext dbContext,
        IMemoryCache cache,
        int loginUserId,
        string reason,
        string? actor)
    {
        var stamp = CreateStamp();

        var usernames = await HrmsDatabase.QueryAsync(
            dbContext,
            "SELECT TOP 1 Username FROM AppLoginUsers WHERE Id = @Id;",
            command => HrmsDatabase.AddParameter(command, "@Id", loginUserId),
            reader => HrmsDatabase.GetString(reader, "Username"));

        var username = usernames.FirstOrDefault();

        await HrmsDatabase.ExecuteAsync(
            dbContext,
            """
UPDATE AppLoginUsers
SET SecurityStamp = @Stamp,
    UpdatedAt = SYSUTCDATETIME()
WHERE Id = @Id;

IF OBJECT_ID('ApiTokens', 'U') IS NOT NULL
    UPDATE ApiTokens
    SET RevokedAt = SYSUTCDATETIME()
    WHERE RevokedAt IS NULL
      AND Username = @Username;

INSERT INTO AuditLogs
(EntityName, EntityId, Action, NewValues, UserName)
VALUES
(
    'Authentication',
    @EntityId,
    'Authentication Sessions Invalidated',
    @Reason,
    @Actor
);
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@Stamp", stamp);
                HrmsDatabase.AddParameter(command, "@Id", loginUserId);
                HrmsDatabase.AddParameter(command, "@Username", username ?? string.Empty);
                HrmsDatabase.AddParameter(command, "@EntityId", loginUserId.ToString());
                HrmsDatabase.AddParameter(command, "@Reason", reason);
                HrmsDatabase.AddParameter(command, "@Actor", actor ?? "System");
            });

        InvalidateCache(cache, username);
        return stamp;
    }
}
