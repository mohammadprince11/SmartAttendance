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

    private static string CacheKey(int tenantId, string username) =>
        $"account:security:{tenantId}:" + username.Trim().ToLowerInvariant();

    public static string CreateStamp() =>
        Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();

    /// <summary>يقرأ حالة الحساب الحالية (بكاش قصير). يرجع Missing إذا لم يوجد.</summary>
    public static async Task<AccountSecurityState> GetStateAsync(
        ApplicationDbContext dbContext,
        IMemoryCache cache,
        int tenantId,
        string? username,
        HttpContext? requestContext = null)
    {
        if (tenantId <= 0 || string.IsNullOrWhiteSpace(username))
        {
            return AccountSecurityState.Missing;
        }

        var key = CacheKey(tenantId, username);

        if (cache.TryGetValue<AccountSecurityState>(key, out var cached) && cached is not null)
        {
            return await ApplyEndServiceAsync(dbContext, tenantId, username, cached, requestContext);
        }

        var state = await ReadStateAsync(dbContext, tenantId, username);
        cache.Set(key, state, StateCacheLifetime);
        return await ApplyEndServiceAsync(dbContext, tenantId, username, state, requestContext);
    }

    private static async Task<AccountSecurityState> ApplyEndServiceAsync(
        ApplicationDbContext db, int tenantId, string username, AccountSecurityState state, HttpContext? requestContext)
    {
        // Deliberately uncached: a new termination restricts an already open cookie/token
        // immediately, and the absolute cutoff is evaluated even before the closure worker runs.
        var schedule = requestContext is null
            ? await EndServiceAccessStore.GetAsync(db, tenantId, username)
            : await EndServiceAccessStore.GetForRequestAsync(db, requestContext, tenantId, username);
        return schedule is null ? state : state with
        {
            FarewellOnly = true,
            IsActive = EndServiceAccessPolicy.AllowsAccess(state.IsActive, schedule.AccessEndsAtUtc, DateTimeOffset.UtcNow)
        };
    }

    public static void InvalidateCache(IMemoryCache cache, int tenantId, string? username)
    {
        if (tenantId > 0 && !string.IsNullOrWhiteSpace(username))
        {
            cache.Remove(CacheKey(tenantId, username));
        }
    }

    private static async Task<AccountSecurityState> ReadStateAsync(
        ApplicationDbContext dbContext,
        int tenantId,
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
WHERE u.TenantId = @TenantId AND u.Username = @Username;
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@TenantId", tenantId);
                HrmsDatabase.AddParameter(command, "@Username", username.Trim());
            },
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

        var identities = await HrmsDatabase.QueryAsync(
            dbContext,
            "SELECT TOP 1 TenantId, Username FROM AppLoginUsers WHERE Id = @Id;",
            command => HrmsDatabase.AddParameter(command, "@Id", loginUserId),
            reader => new
            {
                TenantId = HrmsDatabase.GetInt(reader, "TenantId"),
                Username = HrmsDatabase.GetString(reader, "Username")
            });

        var loginIdentity = identities.FirstOrDefault();
        var tenantId = loginIdentity?.TenantId ?? 0;
        var username = loginIdentity?.Username;

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
      AND TenantId = @TenantId
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
                HrmsDatabase.AddParameter(command, "@TenantId", tenantId);
                HrmsDatabase.AddParameter(command, "@Username", username ?? string.Empty);
                HrmsDatabase.AddParameter(command, "@EntityId", loginUserId.ToString());
                HrmsDatabase.AddParameter(command, "@Reason", reason);
                HrmsDatabase.AddParameter(command, "@Actor", actor ?? "System");
            });

        InvalidateCache(cache, tenantId, username);
        return stamp;
    }
}
