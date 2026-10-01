using System.Data;
using Microsoft.EntityFrameworkCore;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;
using SmartAttendance.Web.Infrastructure.Security;

namespace SmartAttendance.Web.Infrastructure.Platform;

public static class PlatformPortalStore
{
    private const string BootstrapUsernameVariable = "ZYNORA_PLATFORM_OWNER_USERNAME";
    private const string BootstrapPasswordVariable = "ZYNORA_PLATFORM_OWNER_PASSWORD";

    public static readonly IReadOnlyDictionary<string, string> ModuleCatalog =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["CoreHR"] = "الموارد البشرية",
            ["Attendance"] = "الحضور والانصراف",
            ["Payroll"] = "الرواتب",
            ["SelfService"] = "الخدمة الذاتية",
            ["Performance"] = "إدارة الأداء",
            ["Mobile"] = "تطبيق الموظف",
            ["PeopleAI"] = "ذكاء الأشخاص"
        };

    // Administrative record of money collected outside ZYNORA only.
    // The platform does not process cards, hold banking credentials, or call a payment gateway.
    public static readonly IReadOnlyDictionary<string, string> PaymentMethodCatalog =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["BankTransfer"] = "تحويل مصرفي خارجي",
            ["Cash"] = "نقداً",
            ["Cheque"] = "صك/شيك",
            ["Other"] = "طريقة خارجية أخرى"
        };

    public static readonly IReadOnlyDictionary<string, string> CurrencyCatalog =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["IQD"] = "دينار عراقي",
            ["USD"] = "دولار أمريكي"
        };

    public sealed record PlatformOwnerIdentity(
        int Id,
        string Username,
        string DisplayName,
        string PasswordHash,
        string PasswordSalt,
        bool IsActive);

    public sealed record TenantSummary(
        int Id,
        string Code,
        string Name,
        string? LegalName,
        string? ContactName,
        string? ContactEmail,
        string? ContactPhone,
        string? Country,
        string? Address,
        string? TaxNumber,
        string? PortalSubdomain,
        string? CustomDomain,
        string DomainStatus,
        bool IsActive,
        bool IsDeleted,
        DateTime? ArchivedAtUtc,
        string? ArchivedBy,
        string PlanCode,
        string LicenseStatus,
        DateTime StartsAtUtc,
        DateTime? ExpiresAtUtc,
        DateTime? GraceEndsAtUtc,
        int MaxCompanies,
        int MaxEmployees,
        int MaxDevices,
        string EnabledModulesCsv,
        int CompanyCount,
        int EmployeeCount,
        int DeviceCount,
        DateTime CreatedAt,
        byte[] Version)
    {
        public string EffectiveStatus => PlatformLicensePolicy.DisplayStatus(
            LicenseStatus,
            ExpiresAtUtc,
            GraceEndsAtUtc,
            DateTime.UtcNow);

        public IReadOnlyList<string> EnabledModules => EnabledModulesCsv
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        public string VersionToken => Convert.ToBase64String(Version);
    }

    public sealed record CreateTenantCommand(
        string Name,
        string? LegalName,
        string ContactName,
        string ContactEmail,
        string ContactPhone,
        string Country,
        string? Address,
        string? TaxNumber,
        string AdminUsername,
        string AdminPassword,
        string PlanCode,
        string LicenseStatus,
        DateTime StartsAtUtc,
        DateTime? ExpiresAtUtc,
        DateTime? GraceEndsAtUtc,
        int MaxCompanies,
        int MaxEmployees,
        int MaxDevices,
        IReadOnlyCollection<string> EnabledModules);

    public sealed record UpdateTenantProfileCommand(
        int TenantId,
        string Name,
        string? LegalName,
        string ContactName,
        string ContactEmail,
        string ContactPhone,
        string Country,
        string? Address,
        string? TaxNumber);

    public sealed record UpdateTenantDomainCommand(
        int TenantId,
        string? PortalSubdomain,
        string? CustomDomain);

    public sealed record TenantAdminAccount(
        int Id,
        string Username,
        bool IsActive,
        int FailedLoginAttempts,
        DateTime? LockoutEndUtc,
        bool MustChangePassword,
        DateTime? LastLoginAt);

    public sealed record UpdateLicenseCommand(
        int TenantId,
        string PlanCode,
        string LicenseStatus,
        DateTime StartsAtUtc,
        DateTime? ExpiresAtUtc,
        DateTime? GraceEndsAtUtc,
        int MaxCompanies,
        int MaxEmployees,
        int MaxDevices,
        IReadOnlyCollection<string> EnabledModules,
        byte[] ExpectedVersion);

    public sealed record AuditEvent(
        long Id,
        string ActorUsername,
        string ActionCode,
        string Details,
        string? IpAddress,
        DateTime CreatedAtUtc);

    public sealed record SubscriptionInvoice(
        long Id,
        string InvoiceNumber,
        DateTime PeriodStartsAtUtc,
        DateTime PeriodEndsAtUtc,
        int Months,
        int GraceDays,
        decimal Amount,
        string Currency,
        string PaymentMethod,
        string? PaymentReference,
        string? Notes,
        string Status,
        DateTime PaidAtUtc,
        string CreatedBy,
        DateTime CreatedAtUtc,
        DateTime? VoidedAtUtc,
        string? VoidedBy,
        string? VoidReason);

    public sealed record RecordRenewalCommand(
        int TenantId,
        byte[] ExpectedLicenseVersion,
        Guid IdempotencyKey,
        int Months,
        int GraceDays,
        decimal Amount,
        string Currency,
        string PaymentMethod,
        string? PaymentReference,
        string? Notes);

    public sealed record RenewalResult(bool Success, bool WasDuplicate, long? InvoiceId);

    public static async Task VerifySchemaAsync(ApplicationDbContext db)
    {
        var missing = await HrmsDatabase.QueryAsync(
            db,
            """
SELECT RequiredName
FROM (VALUES
    (N'PlatformOwners'),
    (N'TenantLicenses'),
    (N'PlatformAuditEvents'),
    (N'PlatformSubscriptionInvoices')
) required(RequiredName)
WHERE OBJECT_ID(N'dbo.' + RequiredName, 'U') IS NULL;
""",
            null,
            reader => HrmsDatabase.GetString(reader, "RequiredName"));

        if (missing.Count > 0)
        {
            throw new InvalidOperationException(
                "مخطط بوابة مالك المنصة غير مكتمل. الجداول المفقودة: " +
                string.Join(", ", missing));
        }
    }

    public static async Task EnsureBootstrapOwnerAsync(ApplicationDbContext db)
    {
        var count = await HrmsDatabase.ScalarAsync<int>(
            db,
            "SELECT COUNT(*) FROM dbo.PlatformOwners;");

        if (count > 0)
        {
            return;
        }

        var username = Environment.GetEnvironmentVariable(BootstrapUsernameVariable)?.Trim();
        var password = Environment.GetEnvironmentVariable(BootstrapPasswordVariable);

        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
        {
            return;
        }

        if (password.Length < 12)
        {
            throw new InvalidOperationException(
                $"{BootstrapPasswordVariable} must contain at least 12 characters.");
        }

        var salt = SimplePasswordHasher.CreateSalt();
        var hash = SimplePasswordHasher.HashPassword(password, salt);
        var normalized = username.ToUpperInvariant();

        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var current = await HrmsDatabase.ScalarAsync<int>(db, "SELECT COUNT(*) FROM dbo.PlatformOwners WITH (UPDLOCK, HOLDLOCK);");
        if (current == 0)
        {
            await HrmsDatabase.ExecuteAsync(
                db,
                """
INSERT INTO dbo.PlatformOwners
    (Username, NormalizedUsername, DisplayName, PasswordHash, PasswordSalt, IsActive, CreatedAtUtc)
VALUES
    (@Username, @NormalizedUsername, @DisplayName, @PasswordHash, @PasswordSalt, 1, SYSUTCDATETIME());

INSERT INTO dbo.PlatformAuditEvents
    (ActorUsername, ActionCode, TargetType, TargetKey, Details, IpAddress, CreatedAtUtc)
VALUES
    (N'System', N'BootstrapOwnerCreated', N'PlatformOwner', @Username,
     N'Initial platform owner created from protected environment values.', NULL, SYSUTCDATETIME());
""",
                command =>
                {
                    HrmsDatabase.AddParameter(command, "@Username", username);
                    HrmsDatabase.AddParameter(command, "@NormalizedUsername", normalized);
                    HrmsDatabase.AddParameter(command, "@DisplayName", username);
                    HrmsDatabase.AddParameter(command, "@PasswordHash", hash);
                    HrmsDatabase.AddParameter(command, "@PasswordSalt", salt);
                });
        }

        await transaction.CommitAsync();
    }

    public static async Task<PlatformOwnerIdentity?> FindOwnerAsync(
        ApplicationDbContext db,
        string username)
    {
        var rows = await HrmsDatabase.QueryAsync(
            db,
            """
SELECT TOP (1) Id, Username, DisplayName, PasswordHash, PasswordSalt, IsActive
FROM dbo.PlatformOwners
WHERE NormalizedUsername = @NormalizedUsername;
""",
            command => HrmsDatabase.AddParameter(
                command,
                "@NormalizedUsername",
                username.Trim().ToUpperInvariant()),
            reader => new PlatformOwnerIdentity(
                HrmsDatabase.GetInt(reader, "Id"),
                HrmsDatabase.GetString(reader, "Username"),
                HrmsDatabase.GetString(reader, "DisplayName"),
                HrmsDatabase.GetString(reader, "PasswordHash"),
                HrmsDatabase.GetString(reader, "PasswordSalt"),
                HrmsDatabase.GetBool(reader, "IsActive")));

        return rows.SingleOrDefault();
    }

    public static Task RecordOwnerLoginAsync(
        ApplicationDbContext db,
        int ownerId,
        string username,
        string? ipAddress) =>
        HrmsDatabase.ExecuteAsync(
            db,
            """
UPDATE dbo.PlatformOwners
SET LastLoginAtUtc = SYSUTCDATETIME()
WHERE Id = @OwnerId;

INSERT INTO dbo.PlatformAuditEvents
    (ActorUsername, ActionCode, TargetType, TargetKey, Details, IpAddress, CreatedAtUtc)
VALUES
    (@Username, N'OwnerLogin', N'PlatformOwner', CONVERT(nvarchar(40), @OwnerId),
     N'Platform owner signed in.', @IpAddress, SYSUTCDATETIME());
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@OwnerId", ownerId);
                HrmsDatabase.AddParameter(command, "@Username", username);
                HrmsDatabase.AddParameter(command, "@IpAddress", ipAddress);
            });

    public static Task<List<TenantSummary>> ListTenantsAsync(
        ApplicationDbContext db,
        string? search = null)
    {
        var normalizedSearch = string.IsNullOrWhiteSpace(search)
            ? null
            : search.Trim()[..Math.Min(search.Trim().Length, 100)];

        return HrmsDatabase.QueryAsync(
            db,
            TenantSummarySql +
            """
 WHERE (@Search IS NULL
        OR t.Code LIKE @SearchPattern ESCAPE N'~'
        OR t.Name LIKE @SearchPattern ESCAPE N'~'
        OR EXISTS (
            SELECT 1
            FROM dbo.Companies searchCompany
            WHERE searchCompany.TenantId = t.Id
              AND searchCompany.IsDeleted = 0
              AND (searchCompany.Name LIKE @SearchPattern ESCAPE N'~'
                   OR searchCompany.Code LIKE @SearchPattern ESCAPE N'~')))
 ORDER BY t.CreatedAt DESC;
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@Search", normalizedSearch);
                HrmsDatabase.AddParameter(
                    command,
                    "@SearchPattern",
                    normalizedSearch is null ? null : $"%{EscapeLikePattern(normalizedSearch)}%");
            },
            MapTenantSummary);
    }

    public static async Task<TenantSummary?> GetTenantAsync(ApplicationDbContext db, int tenantId)
    {
        var rows = await HrmsDatabase.QueryAsync(
            db,
            TenantSummarySql + " WHERE t.Id = @TenantId;",
            command => HrmsDatabase.AddParameter(command, "@TenantId", tenantId),
            MapTenantSummary);

        return rows.SingleOrDefault();
    }

    public static async Task<TenantAdminAccount?> GetTenantAdminAsync(
        ApplicationDbContext db,
        int tenantId)
    {
        var rows = await HrmsDatabase.QueryAsync(
            db,
            """
SELECT TOP (1) Id, Username, IsActive, FailedLoginAttempts, LockoutEndUtc,
       MustChangePassword, LastLoginAt
FROM dbo.AppLoginUsers
WHERE TenantId = @TenantId AND Role = N'Admin'
ORDER BY CASE WHEN EmployeeId IS NULL THEN 0 ELSE 1 END, Id;
""",
            command => HrmsDatabase.AddParameter(command, "@TenantId", tenantId),
            reader => new TenantAdminAccount(
                HrmsDatabase.GetInt(reader, "Id"),
                HrmsDatabase.GetString(reader, "Username"),
                HrmsDatabase.GetBool(reader, "IsActive"),
                HrmsDatabase.GetInt(reader, "FailedLoginAttempts"),
                HrmsDatabase.GetDateTime(reader, "LockoutEndUtc"),
                HrmsDatabase.GetBool(reader, "MustChangePassword"),
                HrmsDatabase.GetDateTime(reader, "LastLoginAt")));

        return rows.SingleOrDefault();
    }

    public static Task<List<AuditEvent>> ListTenantAuditAsync(
        ApplicationDbContext db,
        int tenantId,
        string tenantCode,
        int limit = 50) =>
        HrmsDatabase.QueryAsync(
            db,
            """
SELECT TOP (@Limit) Id, ActorUsername, ActionCode, Details, IpAddress, CreatedAtUtc
FROM dbo.PlatformAuditEvents
WHERE (TargetType = N'Tenant' AND TargetKey = @TenantCode)
   OR (TargetType = N'TenantAdmin' AND TargetKey LIKE @TenantAdminPrefix ESCAPE N'~')
ORDER BY CreatedAtUtc DESC, Id DESC;
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@Limit", Math.Clamp(limit, 1, 200));
                HrmsDatabase.AddParameter(command, "@TenantCode", tenantCode);
                HrmsDatabase.AddParameter(command, "@TenantAdminPrefix", $"{tenantId}:%");
            },
            reader => new AuditEvent(
                Convert.ToInt64(reader["Id"]),
                HrmsDatabase.GetString(reader, "ActorUsername"),
                HrmsDatabase.GetString(reader, "ActionCode"),
                HrmsDatabase.GetString(reader, "Details"),
                reader["IpAddress"] is DBNull ? null : HrmsDatabase.GetString(reader, "IpAddress"),
                HrmsDatabase.GetDateTime(reader, "CreatedAtUtc") ?? DateTime.MinValue));

    public static Task<List<SubscriptionInvoice>> ListSubscriptionInvoicesAsync(
        ApplicationDbContext db,
        int tenantId,
        int limit = 100) =>
        HrmsDatabase.QueryAsync(
            db,
            """
SELECT TOP (@Limit) Id, InvoiceNumber, PeriodStartsAtUtc, PeriodEndsAtUtc,
       Months, GraceDays, Amount, Currency, PaymentMethod, PaymentReference,
       Notes, Status, PaidAtUtc, CreatedBy, CreatedAtUtc,
       VoidedAtUtc, VoidedBy, VoidReason
FROM dbo.PlatformSubscriptionInvoices
WHERE TenantId = @TenantId
ORDER BY CreatedAtUtc DESC, Id DESC;
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@Limit", Math.Clamp(limit, 1, 500));
                HrmsDatabase.AddParameter(command, "@TenantId", tenantId);
            },
            reader => new SubscriptionInvoice(
                Convert.ToInt64(reader["Id"]),
                HrmsDatabase.GetString(reader, "InvoiceNumber"),
                HrmsDatabase.GetDateTime(reader, "PeriodStartsAtUtc") ?? DateTime.MinValue,
                HrmsDatabase.GetDateTime(reader, "PeriodEndsAtUtc") ?? DateTime.MinValue,
                HrmsDatabase.GetInt(reader, "Months"),
                HrmsDatabase.GetInt(reader, "GraceDays"),
                Convert.ToDecimal(reader["Amount"]),
                HrmsDatabase.GetString(reader, "Currency"),
                HrmsDatabase.GetString(reader, "PaymentMethod"),
                reader["PaymentReference"] is DBNull ? null : HrmsDatabase.GetString(reader, "PaymentReference"),
                reader["Notes"] is DBNull ? null : HrmsDatabase.GetString(reader, "Notes"),
                HrmsDatabase.GetString(reader, "Status"),
                HrmsDatabase.GetDateTime(reader, "PaidAtUtc") ?? DateTime.MinValue,
                HrmsDatabase.GetString(reader, "CreatedBy"),
                HrmsDatabase.GetDateTime(reader, "CreatedAtUtc") ?? DateTime.MinValue,
                HrmsDatabase.GetDateTime(reader, "VoidedAtUtc"),
                GetNullableString(reader, "VoidedBy"),
                GetNullableString(reader, "VoidReason")));

    public static async Task<bool> UpdateTenantDomainAsync(
        ApplicationDbContext db,
        UpdateTenantDomainCommand input,
        string actorUsername,
        string? ipAddress)
    {
        var subdomain = NormalizeOptional(input.PortalSubdomain, 63)?.ToLowerInvariant();
        var customDomain = NormalizeOptional(input.CustomDomain, 253)?.ToLowerInvariant();

        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var conflict = await HrmsDatabase.ScalarAsync<int>(
            db,
            """
SELECT COUNT(*)
FROM dbo.Tenants WITH (UPDLOCK, HOLDLOCK)
WHERE Id <> @TenantId
  AND ((@PortalSubdomain IS NOT NULL AND PortalSubdomain = @PortalSubdomain)
       OR (@CustomDomain IS NOT NULL AND CustomDomain = @CustomDomain));
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@TenantId", input.TenantId);
                HrmsDatabase.AddParameter(command, "@PortalSubdomain", subdomain);
                HrmsDatabase.AddParameter(command, "@CustomDomain", customDomain);
            });

        if (conflict > 0)
        {
            await transaction.RollbackAsync();
            return false;
        }

        var affected = await HrmsDatabase.ScalarAsync<int>(
            db,
            """
DECLARE @Changed TABLE (Code nvarchar(20) NOT NULL);

UPDATE dbo.Tenants
SET PortalSubdomain = @PortalSubdomain,
    CustomDomain = @CustomDomain,
    DomainStatus = CASE
        WHEN @PortalSubdomain IS NULL AND @CustomDomain IS NULL THEN N'NotConfigured'
        ELSE N'Pending'
    END,
    UpdatedAt = SYSUTCDATETIME()
OUTPUT inserted.Code INTO @Changed(Code)
WHERE Id = @TenantId AND IsDeleted = 0;

INSERT INTO dbo.PlatformAuditEvents
    (ActorUsername, ActionCode, TargetType, TargetKey, Details, IpAddress, CreatedAtUtc)
SELECT @Actor, N'TenantDomainsUpdated', N'Tenant', Code,
       CONCAT(N'Domain routing updated. Subdomain=', COALESCE(@PortalSubdomain, N'-'),
              N'; CustomDomain=', COALESCE(@CustomDomain, N'-'), N'; Verification=pending.'),
       @IpAddress, SYSUTCDATETIME()
FROM @Changed;

SELECT COUNT(*) FROM @Changed;
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@TenantId", input.TenantId);
                HrmsDatabase.AddParameter(command, "@PortalSubdomain", subdomain);
                HrmsDatabase.AddParameter(command, "@CustomDomain", customDomain);
                HrmsDatabase.AddParameter(command, "@Actor", actorUsername);
                HrmsDatabase.AddParameter(command, "@IpAddress", ipAddress);
            });

        if (affected != 1)
        {
            await transaction.RollbackAsync();
            return false;
        }

        await transaction.CommitAsync();
        return true;
    }

    public static async Task<bool> VoidSubscriptionInvoiceAsync(
        ApplicationDbContext db,
        int tenantId,
        long invoiceId,
        string reason,
        string actorUsername,
        string? ipAddress)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var affected = await HrmsDatabase.ScalarAsync<int>(
            db,
            """
DECLARE @Changed TABLE (InvoiceNumber nvarchar(80) NOT NULL, TenantCode nvarchar(20) NOT NULL);

UPDATE invoice WITH (UPDLOCK, HOLDLOCK)
SET Status = N'Voided',
    VoidedAtUtc = SYSUTCDATETIME(),
    VoidedBy = @Actor,
    VoidReason = @Reason
OUTPUT inserted.InvoiceNumber, tenant.Code INTO @Changed(InvoiceNumber, TenantCode)
FROM dbo.PlatformSubscriptionInvoices invoice
INNER JOIN dbo.Tenants tenant ON tenant.Id = invoice.TenantId
WHERE invoice.Id = @InvoiceId AND invoice.TenantId = @TenantId
  AND invoice.Status = N'Paid';

INSERT INTO dbo.PlatformAuditEvents
    (ActorUsername, ActionCode, TargetType, TargetKey, Details, IpAddress, CreatedAtUtc)
SELECT @Actor, N'SubscriptionInvoiceVoided', N'Tenant', TenantCode,
       CONCAT(N'Invoice ', InvoiceNumber, N' voided. Reason: ', @Reason,
              N' License dates were not changed automatically.'),
       @IpAddress, SYSUTCDATETIME()
FROM @Changed;

SELECT COUNT(*) FROM @Changed;
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@TenantId", tenantId);
                HrmsDatabase.AddParameter(command, "@InvoiceId", invoiceId);
                HrmsDatabase.AddParameter(command, "@Reason", reason.Trim()[..Math.Min(reason.Trim().Length, 500)]);
                HrmsDatabase.AddParameter(command, "@Actor", actorUsername);
                HrmsDatabase.AddParameter(command, "@IpAddress", ipAddress);
            });

        if (affected != 1)
        {
            await transaction.RollbackAsync();
            return false;
        }

        await transaction.CommitAsync();
        return true;
    }

    public static async Task<bool> SetTenantArchivedAsync(
        ApplicationDbContext db,
        int tenantId,
        bool archive,
        string reason,
        string actorUsername,
        string? ipAddress)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var affected = await HrmsDatabase.ScalarAsync<int>(
            db,
            """
DECLARE @Changed TABLE (Code nvarchar(20) NOT NULL);

UPDATE dbo.Tenants WITH (UPDLOCK, HOLDLOCK)
SET IsDeleted = @Archive,
    IsActive = 0,
    ArchivedAtUtc = CASE WHEN @Archive = 1 THEN SYSUTCDATETIME() ELSE NULL END,
    ArchivedBy = CASE WHEN @Archive = 1 THEN @Actor ELSE NULL END,
    UpdatedAt = SYSUTCDATETIME()
OUTPUT inserted.Code INTO @Changed(Code)
WHERE Id = @TenantId AND IsDeleted <> @Archive;

UPDATE dbo.TenantLicenses
SET Status = N'Suspended', UpdatedAtUtc = SYSUTCDATETIME()
WHERE TenantId = @TenantId AND EXISTS (SELECT 1 FROM @Changed);

UPDATE dbo.AppLoginUsers
SET SecurityStamp = REPLACE(CONVERT(nvarchar(36), NEWID()), N'-', N''),
    UpdatedAt = SYSUTCDATETIME()
WHERE TenantId = @TenantId AND EXISTS (SELECT 1 FROM @Changed);

INSERT INTO dbo.PlatformAuditEvents
    (ActorUsername, ActionCode, TargetType, TargetKey, Details, IpAddress, CreatedAtUtc)
SELECT @Actor, CASE WHEN @Archive = 1 THEN N'TenantArchived' ELSE N'TenantRestored' END,
       N'Tenant', Code,
       CONCAT(CASE WHEN @Archive = 1
                   THEN N'Tenant archived and access suspended. Reason: '
                   ELSE N'Tenant restored in suspended state. Reason: ' END, @Reason),
       @IpAddress, SYSUTCDATETIME()
FROM @Changed;

SELECT COUNT(*) FROM @Changed;
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@TenantId", tenantId);
                HrmsDatabase.AddParameter(command, "@Archive", archive);
                HrmsDatabase.AddParameter(command, "@Reason", reason.Trim()[..Math.Min(reason.Trim().Length, 500)]);
                HrmsDatabase.AddParameter(command, "@Actor", actorUsername);
                HrmsDatabase.AddParameter(command, "@IpAddress", ipAddress);
            });

        if (affected != 1)
        {
            await transaction.RollbackAsync();
            return false;
        }

        await transaction.CommitAsync();
        return true;
    }

    public static async Task<RenewalResult> RecordPaidRenewalAsync(
        ApplicationDbContext db,
        RecordRenewalCommand input,
        string actorUsername,
        string? ipAddress)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);

        var existingInvoiceId = await HrmsDatabase.ScalarAsync<long>(
            db,
            """
SELECT COALESCE((
    SELECT TOP (1) Id
    FROM dbo.PlatformSubscriptionInvoices WITH (UPDLOCK, HOLDLOCK)
    WHERE IdempotencyKey = @IdempotencyKey
), 0);
""",
            command => HrmsDatabase.AddParameter(command, "@IdempotencyKey", input.IdempotencyKey));

        if (existingInvoiceId > 0)
        {
            await transaction.CommitAsync();
            return new RenewalResult(true, true, existingInvoiceId);
        }

        var utcNow = DateTime.UtcNow;
        var invoiceSuffix = input.IdempotencyKey.ToString("N")[..8].ToUpperInvariant();
        var invoiceId = await HrmsDatabase.ScalarAsync<long>(
            db,
            """
DECLARE @Changed TABLE
(
    TenantId int NOT NULL,
    TenantCode nvarchar(20) NOT NULL,
    PeriodStartsAtUtc datetime2 NOT NULL,
    PeriodEndsAtUtc datetime2 NOT NULL
);

UPDATE license WITH (UPDLOCK, HOLDLOCK)
SET ExpiresAtUtc = DATEADD(MONTH, @Months,
        CASE WHEN license.ExpiresAtUtc IS NULL OR license.ExpiresAtUtc < @NowUtc
             THEN @NowUtc ELSE license.ExpiresAtUtc END),
    GraceEndsAtUtc = DATEADD(DAY, @GraceDays, DATEADD(MONTH, @Months,
        CASE WHEN license.ExpiresAtUtc IS NULL OR license.ExpiresAtUtc < @NowUtc
             THEN @NowUtc ELSE license.ExpiresAtUtc END)),
    Status = N'Active',
    UpdatedAtUtc = @NowUtc
OUTPUT inserted.TenantId, tenant.Code,
       CASE WHEN deleted.ExpiresAtUtc IS NULL OR deleted.ExpiresAtUtc < @NowUtc
            THEN @NowUtc ELSE deleted.ExpiresAtUtc END,
       inserted.ExpiresAtUtc
INTO @Changed(TenantId, TenantCode, PeriodStartsAtUtc, PeriodEndsAtUtc)
FROM dbo.TenantLicenses license
INNER JOIN dbo.Tenants tenant ON tenant.Id = license.TenantId
WHERE license.TenantId = @TenantId
  AND license.Version = @ExpectedVersion
  AND tenant.IsDeleted = 0;

IF NOT EXISTS (SELECT 1 FROM @Changed)
BEGIN
    SELECT CAST(0 AS bigint);
    RETURN;
END;

DECLARE @InvoiceNumber nvarchar(80) = CONCAT(
    N'INV-', (SELECT TenantCode FROM @Changed), N'-',
    CONVERT(char(8), @NowUtc, 112), N'-', @InvoiceSuffix);

INSERT INTO dbo.PlatformSubscriptionInvoices
    (TenantId, InvoiceNumber, IdempotencyKey, PeriodStartsAtUtc, PeriodEndsAtUtc,
     Months, GraceDays, Amount, Currency, PaymentMethod, PaymentReference,
     Notes, Status, PaidAtUtc, CreatedBy, CreatedAtUtc)
SELECT TenantId, @InvoiceNumber, @IdempotencyKey, PeriodStartsAtUtc, PeriodEndsAtUtc,
       @Months, @GraceDays, @Amount, @Currency, @PaymentMethod, @PaymentReference,
       @Notes, N'Paid', @NowUtc, @Actor, @NowUtc
FROM @Changed;

DECLARE @InvoiceId bigint = SCOPE_IDENTITY();

INSERT INTO dbo.PlatformAuditEvents
    (ActorUsername, ActionCode, TargetType, TargetKey, Details, IpAddress, CreatedAtUtc)
SELECT @Actor, N'SubscriptionRenewed', N'Tenant', TenantCode,
       CONCAT(N'Subscription renewed for ', @Months, N' month(s). Invoice=', @InvoiceNumber,
              N'; Amount=', CONVERT(nvarchar(40), @Amount), N' ', @Currency),
       @IpAddress, @NowUtc
FROM @Changed;

SELECT @InvoiceId;
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@TenantId", input.TenantId);
                HrmsDatabase.AddParameter(command, "@ExpectedVersion", input.ExpectedLicenseVersion);
                HrmsDatabase.AddParameter(command, "@IdempotencyKey", input.IdempotencyKey);
                HrmsDatabase.AddParameter(command, "@InvoiceSuffix", invoiceSuffix);
                HrmsDatabase.AddParameter(command, "@Months", input.Months);
                HrmsDatabase.AddParameter(command, "@GraceDays", input.GraceDays);
                HrmsDatabase.AddParameter(command, "@Amount", input.Amount);
                HrmsDatabase.AddParameter(command, "@Currency", input.Currency);
                HrmsDatabase.AddParameter(command, "@PaymentMethod", input.PaymentMethod);
                HrmsDatabase.AddParameter(command, "@PaymentReference", NormalizeOptional(input.PaymentReference, 120));
                HrmsDatabase.AddParameter(command, "@Notes", NormalizeOptional(input.Notes, 500));
                HrmsDatabase.AddParameter(command, "@Actor", actorUsername);
                HrmsDatabase.AddParameter(command, "@IpAddress", ipAddress);
                HrmsDatabase.AddParameter(command, "@NowUtc", utcNow);
            });

        if (invoiceId <= 0)
        {
            await transaction.RollbackAsync();
            return new RenewalResult(false, false, null);
        }

        await transaction.CommitAsync();
        return new RenewalResult(true, false, invoiceId);
    }

    public static async Task<(int TenantId, string Code)> CreateTenantAsync(
        ApplicationDbContext db,
        CreateTenantCommand input,
        string actorUsername,
        string? ipAddress)
    {
        var modules = NormalizeModules(input.EnabledModules);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);

        var existingCodes = await HrmsDatabase.QueryAsync(
            db,
            "SELECT Code FROM dbo.Tenants WITH (UPDLOCK, HOLDLOCK);",
            null,
            reader => HrmsDatabase.GetString(reader, "Code"));
        var used = existingCodes.ToHashSet(StringComparer.Ordinal);
        var code = Enumerable.Range(1, 9999)
            .Select(number => number.ToString("0000"))
            .FirstOrDefault(candidate => !used.Contains(candidate))
            ?? throw new InvalidOperationException("لا توجد أكواد منظومات متاحة ضمن نطاق الأربعة أرقام.");

        var tenantId = await HrmsDatabase.ScalarAsync<int>(
            db,
            """
INSERT INTO dbo.Tenants
    (Code, Name, LegalName, ContactName, ContactEmail, ContactPhone, Country,
     Address, TaxNumber, IsActive, CreatedAt, UpdatedAt, IsDeleted)
VALUES
    (@Code, @Name, @LegalName, @ContactName, @ContactEmail, @ContactPhone, @Country,
     @Address, @TaxNumber, 1, SYSUTCDATETIME(), NULL, 0);
SELECT CAST(SCOPE_IDENTITY() AS int);
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@Code", code);
                HrmsDatabase.AddParameter(command, "@Name", input.Name.Trim());
                HrmsDatabase.AddParameter(command, "@LegalName", NormalizeOptional(input.LegalName, 240));
                HrmsDatabase.AddParameter(command, "@ContactName", input.ContactName.Trim());
                HrmsDatabase.AddParameter(command, "@ContactEmail", input.ContactEmail.Trim());
                HrmsDatabase.AddParameter(command, "@ContactPhone", input.ContactPhone.Trim());
                HrmsDatabase.AddParameter(command, "@Country", input.Country.Trim());
                HrmsDatabase.AddParameter(command, "@Address", NormalizeOptional(input.Address, 500));
                HrmsDatabase.AddParameter(command, "@TaxNumber", NormalizeOptional(input.TaxNumber, 80));
            });

        var salt = SimplePasswordHasher.CreateSalt();
        var hash = SimplePasswordHasher.HashPassword(input.AdminPassword, salt);

        await HrmsDatabase.ExecuteAsync(
            db,
            """
INSERT INTO dbo.TenantLicenses
    (TenantId, PlanCode, Status, StartsAtUtc, ExpiresAtUtc, GraceEndsAtUtc,
     MaxCompanies, MaxEmployees, MaxDevices, EnabledModulesCsv, CreatedAtUtc, UpdatedAtUtc)
VALUES
    (@TenantId, @PlanCode, @Status, @StartsAtUtc, @ExpiresAtUtc, @GraceEndsAtUtc,
     @MaxCompanies, @MaxEmployees, @MaxDevices, @Modules, SYSUTCDATETIME(), NULL);

INSERT INTO dbo.AppLoginUsers
    (TenantId, EmployeeId, Username, PasswordHash, PasswordSalt, Role, IsActive,
     FailedLoginAttempts, SecurityStamp, MustChangePassword, CreatedAt)
VALUES
    (@TenantId, NULL, @AdminUsername, @PasswordHash, @PasswordSalt, N'Admin', 1,
     0, REPLACE(CONVERT(nvarchar(36), NEWID()), N'-', N''), 1, SYSUTCDATETIME());

INSERT INTO dbo.PlatformAuditEvents
    (ActorUsername, ActionCode, TargetType, TargetKey, Details, IpAddress, CreatedAtUtc)
VALUES
    (@Actor, N'TenantCreated', N'Tenant', @Code,
     CONCAT(N'Created tenant and initial administrator. Plan=', @PlanCode),
     @IpAddress, SYSUTCDATETIME());
""",
            command =>
            {
                AddLicenseParameters(command, tenantId, input.PlanCode, input.LicenseStatus,
                    input.StartsAtUtc, input.ExpiresAtUtc, input.GraceEndsAtUtc,
                    input.MaxCompanies, input.MaxEmployees, input.MaxDevices, modules);
                HrmsDatabase.AddParameter(command, "@AdminUsername", input.AdminUsername.Trim());
                HrmsDatabase.AddParameter(command, "@PasswordHash", hash);
                HrmsDatabase.AddParameter(command, "@PasswordSalt", salt);
                HrmsDatabase.AddParameter(command, "@Actor", actorUsername);
                HrmsDatabase.AddParameter(command, "@Code", code);
                HrmsDatabase.AddParameter(command, "@IpAddress", ipAddress);
            });

        await transaction.CommitAsync();
        return (tenantId, code);
    }

    public static async Task<bool> UpdateTenantProfileAsync(
        ApplicationDbContext db,
        UpdateTenantProfileCommand input,
        string actorUsername,
        string? ipAddress)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        var affected = await HrmsDatabase.ScalarAsync<int>(
            db,
            """
DECLARE @Changed TABLE (Code nvarchar(20) NOT NULL);

UPDATE dbo.Tenants
SET Name = @Name,
    LegalName = @LegalName,
    ContactName = @ContactName,
    ContactEmail = @ContactEmail,
    ContactPhone = @ContactPhone,
    Country = @Country,
    Address = @Address,
    TaxNumber = @TaxNumber,
    UpdatedAt = SYSUTCDATETIME()
OUTPUT inserted.Code INTO @Changed(Code)
WHERE Id = @TenantId AND IsDeleted = 0;

INSERT INTO dbo.PlatformAuditEvents
    (ActorUsername, ActionCode, TargetType, TargetKey, Details, IpAddress, CreatedAtUtc)
SELECT @Actor, N'TenantProfileUpdated', N'Tenant', Code,
       N'Customer identity and contact profile updated.', @IpAddress, SYSUTCDATETIME()
FROM @Changed;

SELECT COUNT(*) FROM @Changed;
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@TenantId", input.TenantId);
                HrmsDatabase.AddParameter(command, "@Name", input.Name.Trim());
                HrmsDatabase.AddParameter(command, "@LegalName", NormalizeOptional(input.LegalName, 240));
                HrmsDatabase.AddParameter(command, "@ContactName", input.ContactName.Trim());
                HrmsDatabase.AddParameter(command, "@ContactEmail", input.ContactEmail.Trim());
                HrmsDatabase.AddParameter(command, "@ContactPhone", input.ContactPhone.Trim());
                HrmsDatabase.AddParameter(command, "@Country", input.Country.Trim());
                HrmsDatabase.AddParameter(command, "@Address", NormalizeOptional(input.Address, 500));
                HrmsDatabase.AddParameter(command, "@TaxNumber", NormalizeOptional(input.TaxNumber, 80));
                HrmsDatabase.AddParameter(command, "@Actor", actorUsername);
                HrmsDatabase.AddParameter(command, "@IpAddress", ipAddress);
            });

        if (affected != 1)
        {
            await transaction.RollbackAsync();
            return false;
        }

        await transaction.CommitAsync();
        return true;
    }

    public static async Task<bool> ResetTenantAdminPasswordAsync(
        ApplicationDbContext db,
        int tenantId,
        int adminId,
        string temporaryPassword,
        string actorUsername,
        string? ipAddress)
    {
        var salt = SimplePasswordHasher.CreateSalt();
        var hash = SimplePasswordHasher.HashPassword(temporaryPassword, salt);
        return await UpdateTenantAdminSecurityAsync(
            db,
            """
UPDATE dbo.AppLoginUsers
SET PasswordHash = @PasswordHash,
    PasswordSalt = @PasswordSalt,
    PasswordChangedAt = SYSUTCDATETIME(),
    MustChangePassword = 1,
    FailedLoginAttempts = 0,
    LockoutEndUtc = NULL,
    SecurityStamp = REPLACE(CONVERT(nvarchar(36), NEWID()), N'-', N''),
    UpdatedAt = SYSUTCDATETIME()
OUTPUT inserted.Username INTO @Changed(Username)
WHERE Id = @AdminId AND TenantId = @TenantId AND Role = N'Admin'
  AND EXISTS (SELECT 1 FROM dbo.Tenants WHERE Id = @TenantId AND IsDeleted = 0);
""",
            tenantId,
            adminId,
            actorUsername,
            ipAddress,
            "TenantAdminPasswordReset",
            "Tenant administrator password reset; active sessions invalidated and password change required.",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@PasswordHash", hash);
                HrmsDatabase.AddParameter(command, "@PasswordSalt", salt);
            });
    }

    public static Task<bool> UnlockTenantAdminAsync(
        ApplicationDbContext db,
        int tenantId,
        int adminId,
        string actorUsername,
        string? ipAddress) =>
        UpdateTenantAdminSecurityAsync(
            db,
            """
UPDATE dbo.AppLoginUsers
SET FailedLoginAttempts = 0,
    LockoutEndUtc = NULL,
    SecurityStamp = REPLACE(CONVERT(nvarchar(36), NEWID()), N'-', N''),
    UpdatedAt = SYSUTCDATETIME()
OUTPUT inserted.Username INTO @Changed(Username)
WHERE Id = @AdminId AND TenantId = @TenantId AND Role = N'Admin'
  AND EXISTS (SELECT 1 FROM dbo.Tenants WHERE Id = @TenantId AND IsDeleted = 0);
""",
            tenantId,
            adminId,
            actorUsername,
            ipAddress,
            "TenantAdminUnlocked",
            "Tenant administrator lockout cleared and active sessions invalidated.");

    public static Task<bool> SetTenantAdminActiveAsync(
        ApplicationDbContext db,
        int tenantId,
        int adminId,
        bool isActive,
        string actorUsername,
        string? ipAddress) =>
        UpdateTenantAdminSecurityAsync(
            db,
            """
UPDATE dbo.AppLoginUsers
SET IsActive = @IsActive,
    FailedLoginAttempts = CASE WHEN @IsActive = 1 THEN 0 ELSE FailedLoginAttempts END,
    LockoutEndUtc = CASE WHEN @IsActive = 1 THEN NULL ELSE LockoutEndUtc END,
    SecurityStamp = REPLACE(CONVERT(nvarchar(36), NEWID()), N'-', N''),
    UpdatedAt = SYSUTCDATETIME()
OUTPUT inserted.Username INTO @Changed(Username)
WHERE Id = @AdminId AND TenantId = @TenantId AND Role = N'Admin'
  AND EXISTS (SELECT 1 FROM dbo.Tenants WHERE Id = @TenantId AND IsDeleted = 0);
""",
            tenantId,
            adminId,
            actorUsername,
            ipAddress,
            isActive ? "TenantAdminActivated" : "TenantAdminDeactivated",
            isActive
                ? "Tenant administrator account activated; active sessions invalidated."
                : "Tenant administrator account deactivated; active sessions invalidated.",
            command => HrmsDatabase.AddParameter(command, "@IsActive", isActive));

    public static async Task<bool> UpdateLicenseAsync(
        ApplicationDbContext db,
        UpdateLicenseCommand input,
        string actorUsername,
        string? ipAddress)
    {
        var modules = NormalizeModules(input.EnabledModules);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted);

        var affected = await HrmsDatabase.ScalarAsync<int>(
            db,
            """
UPDATE dbo.TenantLicenses
SET PlanCode = @PlanCode,
    Status = @Status,
    StartsAtUtc = @StartsAtUtc,
    ExpiresAtUtc = @ExpiresAtUtc,
    GraceEndsAtUtc = @GraceEndsAtUtc,
    MaxCompanies = @MaxCompanies,
    MaxEmployees = @MaxEmployees,
    MaxDevices = @MaxDevices,
    EnabledModulesCsv = @Modules,
    UpdatedAtUtc = SYSUTCDATETIME()
WHERE TenantId = @TenantId AND Version = @ExpectedVersion
  AND EXISTS (SELECT 1 FROM dbo.Tenants WHERE Id = @TenantId AND IsDeleted = 0);
SELECT @@ROWCOUNT;
""",
            command =>
            {
                AddLicenseParameters(command, input.TenantId, input.PlanCode, input.LicenseStatus,
                    input.StartsAtUtc, input.ExpiresAtUtc, input.GraceEndsAtUtc,
                    input.MaxCompanies, input.MaxEmployees, input.MaxDevices, modules);
                HrmsDatabase.AddParameter(command, "@ExpectedVersion", input.ExpectedVersion);
            });

        if (affected != 1)
        {
            await transaction.RollbackAsync();
            return false;
        }

        await HrmsDatabase.ExecuteAsync(
            db,
            """
INSERT INTO dbo.PlatformAuditEvents
    (ActorUsername, ActionCode, TargetType, TargetKey, Details, IpAddress, CreatedAtUtc)
SELECT @Actor, N'LicenseUpdated', N'Tenant', Code,
       CONCAT(N'License updated. Plan=', @PlanCode, N'; Status=', @Status),
       @IpAddress, SYSUTCDATETIME()
FROM dbo.Tenants WHERE Id = @TenantId;
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@Actor", actorUsername);
                HrmsDatabase.AddParameter(command, "@PlanCode", input.PlanCode);
                HrmsDatabase.AddParameter(command, "@Status", input.LicenseStatus);
                HrmsDatabase.AddParameter(command, "@IpAddress", ipAddress);
                HrmsDatabase.AddParameter(command, "@TenantId", input.TenantId);
            });

        await transaction.CommitAsync();
        return true;
    }

    public static async Task<bool> SetTenantActiveAsync(
        ApplicationDbContext db,
        int tenantId,
        bool isActive,
        string reason,
        string actorUsername,
        string? ipAddress)
    {
        var affected = await HrmsDatabase.ScalarAsync<int>(
            db,
            """
DECLARE @Changed TABLE (Code nvarchar(20) NOT NULL, IsActive bit NOT NULL);

UPDATE dbo.Tenants
SET IsActive = @IsActive, UpdatedAt = SYSUTCDATETIME()
OUTPUT inserted.Code, inserted.IsActive INTO @Changed(Code, IsActive)
WHERE Id = @TenantId AND IsDeleted = 0;

INSERT INTO dbo.PlatformAuditEvents
    (ActorUsername, ActionCode, TargetType, TargetKey, Details, IpAddress, CreatedAtUtc)
SELECT @Actor, CASE WHEN changed.IsActive = 1 THEN N'TenantActivated' ELSE N'TenantSuspended' END,
       N'Tenant', changed.Code,
       CONCAT(CASE WHEN changed.IsActive = 1 THEN N'تم تفعيل المنظومة. السبب: ' ELSE N'تم إيقاف المنظومة. السبب: ' END, @Reason),
       @IpAddress, SYSUTCDATETIME()
FROM @Changed changed;

SELECT COUNT(*) FROM @Changed;
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@TenantId", tenantId);
                HrmsDatabase.AddParameter(command, "@IsActive", isActive);
                HrmsDatabase.AddParameter(command, "@Reason", reason.Trim()[..Math.Min(reason.Trim().Length, 500)]);
                HrmsDatabase.AddParameter(command, "@Actor", actorUsername);
                HrmsDatabase.AddParameter(command, "@IpAddress", ipAddress);
            });

        return affected > 0;
    }

    public static async Task<bool> IsTenantAccessAllowedAsync(
        ApplicationDbContext db,
        int tenantId,
        DateTime utcNow)
    {
        var rows = await HrmsDatabase.QueryAsync(
            db,
            """
SELECT TOP (1) t.IsActive, l.Status, l.StartsAtUtc, l.ExpiresAtUtc, l.GraceEndsAtUtc
FROM dbo.Tenants t
INNER JOIN dbo.TenantLicenses l ON l.TenantId = t.Id
WHERE t.Id = @TenantId AND t.IsDeleted = 0;
""",
            command => HrmsDatabase.AddParameter(command, "@TenantId", tenantId),
            reader => new
            {
                IsActive = HrmsDatabase.GetBool(reader, "IsActive"),
                Status = HrmsDatabase.GetString(reader, "Status"),
                Starts = HrmsDatabase.GetDateTime(reader, "StartsAtUtc") ?? DateTime.MaxValue,
                Expires = HrmsDatabase.GetDateTime(reader, "ExpiresAtUtc"),
                Grace = HrmsDatabase.GetDateTime(reader, "GraceEndsAtUtc")
            });

        var license = rows.SingleOrDefault();
        return license is not null && license.IsActive && PlatformLicensePolicy.AllowsAccess(
            license.Status, license.Starts, license.Expires, license.Grace, utcNow);
    }

    private static async Task<bool> UpdateTenantAdminSecurityAsync(
        ApplicationDbContext db,
        string mutationSql,
        int tenantId,
        int adminId,
        string actorUsername,
        string? ipAddress,
        string actionCode,
        string auditDetails,
        Action<System.Data.Common.DbCommand>? configure = null)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        var sql =
            "DECLARE @Changed TABLE (Username nvarchar(100) NOT NULL);\n" +
            mutationSql +
            """

INSERT INTO dbo.PlatformAuditEvents
    (ActorUsername, ActionCode, TargetType, TargetKey, Details, IpAddress, CreatedAtUtc)
SELECT @Actor, @ActionCode, N'TenantAdmin',
       CONCAT(CONVERT(nvarchar(20), @TenantId), N':', Username),
       @AuditDetails, @IpAddress, SYSUTCDATETIME()
FROM @Changed;

SELECT COUNT(*) FROM @Changed;
""";

        var affected = await HrmsDatabase.ScalarAsync<int>(
            db,
            sql,
            command =>
            {
                HrmsDatabase.AddParameter(command, "@TenantId", tenantId);
                HrmsDatabase.AddParameter(command, "@AdminId", adminId);
                HrmsDatabase.AddParameter(command, "@Actor", actorUsername);
                HrmsDatabase.AddParameter(command, "@ActionCode", actionCode);
                HrmsDatabase.AddParameter(command, "@AuditDetails", auditDetails);
                HrmsDatabase.AddParameter(command, "@IpAddress", ipAddress);
                configure?.Invoke(command);
            });

        if (affected != 1)
        {
            await transaction.RollbackAsync();
            return false;
        }

        await transaction.CommitAsync();
        return true;
    }

    private const string TenantSummarySql =
        """
SELECT t.Id, t.Code, t.Name, t.LegalName, t.ContactName, t.ContactEmail,
       t.ContactPhone, t.Country, t.Address, t.TaxNumber,
       t.PortalSubdomain, t.CustomDomain, t.DomainStatus,
       t.IsActive, t.IsDeleted, t.ArchivedAtUtc, t.ArchivedBy, t.CreatedAt,
       l.PlanCode, l.Status, l.StartsAtUtc, l.ExpiresAtUtc, l.GraceEndsAtUtc,
       l.MaxCompanies, l.MaxEmployees, l.MaxDevices, l.EnabledModulesCsv, l.Version,
       (SELECT COUNT(*) FROM dbo.Companies c WHERE c.TenantId = t.Id AND c.IsDeleted = 0) AS CompanyCount,
       (SELECT COUNT(*)
          FROM dbo.Employees e
          LEFT JOIN dbo.Branches employeeBranch ON employeeBranch.Id = e.BranchId
          INNER JOIN dbo.Companies c ON c.Id = COALESCE(e.CompanyId, employeeBranch.CompanyId)
          WHERE c.TenantId = t.Id AND e.IsDeleted = 0) AS EmployeeCount,
       (SELECT COUNT(*)
          FROM dbo.Devices d
          INNER JOIN dbo.Branches b ON b.Id = d.BranchId
          INNER JOIN dbo.Companies c ON c.Id = b.CompanyId
          WHERE c.TenantId = t.Id AND d.IsDeleted = 0 AND b.IsDeleted = 0) AS DeviceCount
FROM dbo.Tenants t
INNER JOIN dbo.TenantLicenses l ON l.TenantId = t.Id
""";

    private static TenantSummary MapTenantSummary(System.Data.Common.DbDataReader reader) =>
        new(
            HrmsDatabase.GetInt(reader, "Id"),
            HrmsDatabase.GetString(reader, "Code"),
            HrmsDatabase.GetString(reader, "Name"),
            GetNullableString(reader, "LegalName"),
            GetNullableString(reader, "ContactName"),
            GetNullableString(reader, "ContactEmail"),
            GetNullableString(reader, "ContactPhone"),
            GetNullableString(reader, "Country"),
            GetNullableString(reader, "Address"),
            GetNullableString(reader, "TaxNumber"),
            GetNullableString(reader, "PortalSubdomain"),
            GetNullableString(reader, "CustomDomain"),
            HrmsDatabase.GetString(reader, "DomainStatus"),
            HrmsDatabase.GetBool(reader, "IsActive"),
            HrmsDatabase.GetBool(reader, "IsDeleted"),
            HrmsDatabase.GetDateTime(reader, "ArchivedAtUtc"),
            GetNullableString(reader, "ArchivedBy"),
            HrmsDatabase.GetString(reader, "PlanCode"),
            HrmsDatabase.GetString(reader, "Status"),
            HrmsDatabase.GetDateTime(reader, "StartsAtUtc") ?? DateTime.MinValue,
            HrmsDatabase.GetDateTime(reader, "ExpiresAtUtc"),
            HrmsDatabase.GetDateTime(reader, "GraceEndsAtUtc"),
            HrmsDatabase.GetInt(reader, "MaxCompanies"),
            HrmsDatabase.GetInt(reader, "MaxEmployees"),
            HrmsDatabase.GetInt(reader, "MaxDevices"),
            HrmsDatabase.GetString(reader, "EnabledModulesCsv"),
            HrmsDatabase.GetInt(reader, "CompanyCount"),
            HrmsDatabase.GetInt(reader, "EmployeeCount"),
            HrmsDatabase.GetInt(reader, "DeviceCount"),
            HrmsDatabase.GetDateTime(reader, "CreatedAt") ?? DateTime.MinValue,
            (byte[])reader["Version"]);

    private static string? GetNullableString(System.Data.Common.DbDataReader reader, string name) =>
        reader[name] is DBNull ? null : HrmsDatabase.GetString(reader, name);

    private static string NormalizeModules(IEnumerable<string> modules) =>
        string.Join(',', modules
            .Where(module => ModuleCatalog.ContainsKey(module))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(module => module, StringComparer.OrdinalIgnoreCase));

    private static string EscapeLikePattern(string value) =>
        value
            .Replace("~", "~~", StringComparison.Ordinal)
            .Replace("%", "~%", StringComparison.Ordinal)
            .Replace("_", "~_", StringComparison.Ordinal)
            .Replace("[", "~[", StringComparison.Ordinal);

    private static string? NormalizeOptional(string? value, int maxLength)
    {
        var normalized = value?.Trim();
        return string.IsNullOrEmpty(normalized)
            ? null
            : normalized[..Math.Min(normalized.Length, maxLength)];
    }

    private static void AddLicenseParameters(
        System.Data.Common.DbCommand command,
        int tenantId,
        string planCode,
        string status,
        DateTime startsAtUtc,
        DateTime? expiresAtUtc,
        DateTime? graceEndsAtUtc,
        int maxCompanies,
        int maxEmployees,
        int maxDevices,
        string modules)
    {
        HrmsDatabase.AddParameter(command, "@TenantId", tenantId);
        HrmsDatabase.AddParameter(command, "@PlanCode", planCode.Trim());
        HrmsDatabase.AddParameter(command, "@Status", status);
        HrmsDatabase.AddParameter(command, "@StartsAtUtc", startsAtUtc);
        HrmsDatabase.AddParameter(command, "@ExpiresAtUtc", expiresAtUtc);
        HrmsDatabase.AddParameter(command, "@GraceEndsAtUtc", graceEndsAtUtc);
        HrmsDatabase.AddParameter(command, "@MaxCompanies", maxCompanies);
        HrmsDatabase.AddParameter(command, "@MaxEmployees", maxEmployees);
        HrmsDatabase.AddParameter(command, "@MaxDevices", maxDevices);
        HrmsDatabase.AddParameter(command, "@Modules", modules);
    }
}
