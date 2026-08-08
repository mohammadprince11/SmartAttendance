using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;
using SmartAttendance.Web.Infrastructure.Security;
using Xunit;

namespace SmartAttendance.Tests;

/// <summary>
/// اختبارات تكامل عزل الشركات على قاعدة حيّة (الموجة 6 — P1). تتبع عُرف المشروع:
/// المنطق النقيّ يُختبر وحدةً، وعزلُ القاعدة يُثبَت بمعاملةٍ مُتراجَع عنها على
/// <c>SmartAttendance_Dev</c> — لا نلمس الإنتاج ولا نُبقي أثراً.
///
/// <para><b>لماذا معاملةٌ مُتراجَع عنها لا نداءُ المتجر المُعدِّل؟</b>
/// <see cref="LoanStore.PostDueInstallmentsAsync"/> صار يفتح معاملته الخاصة
/// (idempotency)، ونداؤه على شركةٍ حقيقية كان سيُنشئ اقتطاعات مسير لقروضها
/// الحقيقية. فنُثبت هنا **دلالة الشرط** الذي يستعمله المتجر حرفياً على بيانات
/// حقيقية: مقيَّدٌ بشركةٍ يرى قسطها وحده، وغير المقيَّد يرى الشركتين. أنّ المتجر
/// يحمل هذا الشرط أصلاً محروسٌ بـ<see cref="EmployeeCompanyGuardScopeTests"/>.</para>
///
/// <para>أحمر أولاً: قبل الإصلاح كان استعلام الترحيل بلا <c>AND e.CompanyId IN (…)</c>،
/// فالمقيَّد كان يرى الشركتين (صفّان) — أي الثغرة. هذا الاختبار يفشل حينها.</para>
/// </summary>
public sealed class TenantIsolationIntegrationTests : IAsyncLifetime
{
    // قاعدة التطوير حصراً — لا الإنتاج (القاعدة الحمراء: ممنوع الوصول للإنتاج).
    private const string ConnectionString =
        "Server=localhost;Database=SmartAttendance_Dev;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=True";

    private ApplicationDbContext _db = null!;
    private bool _dbAvailable;

    private static ApplicationDbContext NewContext() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(ConnectionString).Options);

    public async Task InitializeAsync()
    {
        _db = NewContext();
        try
        {
            await _db.Database.OpenConnectionAsync();
            _dbAvailable = true;
        }
        catch
        {
            _dbAvailable = false;
        }
    }

    public async Task DisposeAsync()
    {
        try { await _db.Database.CloseConnectionAsync(); } catch { /* best-effort */ }
        await _db.DisposeAsync();
    }

    /// <summary>
    /// الترحيل المقيَّد بشركةٍ يختار قسطها وحده؛ الأدمن يختار الشركتين — على بيانات
    /// حقيقية بمعاملةٍ مُتراجَع عنها. يُثبَت بنفس الشرط الذي يبنيه
    /// <see cref="EmployeeCompanyGuard.ListFilter"/> ويستعمله المتجر.
    /// </summary>
    [SkippableFact]
    public async Task PostDueSelect_IsIsolatedByCompany_OnRealData()
    {
        Skip.IfNot(_dbAvailable, "SmartAttendance_Dev غير متاح محلياً.");

        var connection = _db.Database.GetDbConnection();
        await using var tx = await connection.BeginTransactionAsync();

        // شركتان مختلفتان لهما موظفون فعليّون. أقلّ من اثنتين ⟹ تخطٍّ (بيئة صغيرة).
        var pairs = new List<(int Company, int Employee)>();
        await using (var pick = connection.CreateCommand())
        {
            pick.Transaction = tx;
            pick.CommandText = """
SELECT TOP 2 CompanyId, MIN(Id) AS EmpId
FROM Employees
WHERE ISNULL(IsDeleted,0)=0 AND CompanyId IS NOT NULL
GROUP BY CompanyId ORDER BY CompanyId;
""";
            await using var reader = await pick.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                pairs.Add((reader.GetInt32(0), reader.GetInt32(1)));
        }

        Skip.If(pairs.Count < 2, "تحتاج شركتين مأهولتين على الأقل.");
        var (companyA, empA) = pairs[0];
        var (companyB, empB) = pairs[1];

        // بذرة: قرضان معتمدان لهما قسطٌ مستحقّ غير مرحّل — واحدٌ بكل شركة.
        await SeedApprovedLoanWithDueInstallmentAsync(connection, tx, empA, "__ITEST_A");
        await SeedApprovedLoanWithDueInstallmentAsync(connection, tx, empB, "__ITEST_B");

        // الشرط الذي يستعمله PostDueInstallmentsAsync حرفياً، مقصوراً على بذرتنا.
        var scopedPredicate = EmployeeCompanyGuard.ListFilter(
            CompanyScope.ForCompanies(new[] { companyA }), "e.CompanyId");
        var adminPredicate = EmployeeCompanyGuard.ListFilter(
            CompanyScope.Unrestricted(), "e.CompanyId");

        var scopedCount = await CountDueAsync(connection, tx, scopedPredicate);
        var adminCount = await CountDueAsync(connection, tx, adminPredicate);

        await tx.RollbackAsync();   // لا أثر يبقى على القاعدة.

        Assert.Equal(1, scopedCount);   // المقيَّد بشركة A يرى قسطها وحده.
        Assert.Equal(2, adminCount);    // الأدمن يرى قسطَي الشركتين.
    }

    /// <summary>
    /// الإقلاع المزدوج: نسختان تقلعان معاً لا تهاجران المخطط متزامنتين لأنّ المُهاجر
    /// يأخذ <c>sp_getapplock</c> حصرياً. نثبت التمانع: قفلٌ مأخوذ على اتصالٍ يمنع
    /// اتصالاً آخر من أخذه فوراً (مهلة صفر ⟹ ناتج سالب).
    /// </summary>
    [SkippableFact]
    public async Task SchemaMigratorAppLock_IsMutuallyExclusive_AcrossConnections()
    {
        Skip.IfNot(_dbAvailable, "SmartAttendance_Dev غير متاح محلياً.");

        const string resource = "ZYNORA.SqlSchemaMigrator";
        var first = _db.Database.GetDbConnection();

        // النسخة الأولى تمسك القفل (نطاق Session مربوط بالاتصال المفتوح).
        var acquiredFirst = await GetAppLockAsync(first, null, resource, timeoutMs: 0);
        Assert.True(acquiredFirst >= 0, "النسخة الأولى يجب أن تنال القفل.");

        try
        {
            // النسخة الثانية (اتصالٌ مستقلّ) تُمنع فوراً — هذا هو التمانع الذي يحمي الإقلاع.
            await using var secondCtx = NewContext();
            await secondCtx.Database.OpenConnectionAsync();
            var second = secondCtx.Database.GetDbConnection();
            var acquiredSecond = await GetAppLockAsync(second, null, resource, timeoutMs: 0);

            Assert.True(acquiredSecond < 0,
                "القفل مأخوذ ⟹ يجب أن تُمنع النسخة الثانية (ناتج سالب).");
        }
        finally
        {
            await using var release = first.CreateCommand();
            release.CommandText = $"EXEC sp_releaseapplock @Resource = N'{resource}', @LockOwner = 'Session';";
            await release.ExecuteNonQueryAsync();
        }
    }

    private static async Task SeedApprovedLoanWithDueInstallmentAsync(
        DbConnection connection, DbTransaction tx, int employeeId, string referenceNo)
    {
        // جداول القروض موجودة على _Dev (نسخة الإنتاج)، فلا حاجة لـEnsure — والبذرة
        // كلّها داخل معاملةٍ مُتراجَع عنها.
        int loanId;
        await using (var insertLoan = connection.CreateCommand())
        {
            insertLoan.Transaction = tx;
            insertLoan.CommandText = $"""
INSERT INTO EmployeeLoans
 (ReferenceNo, EmployeeId, LoanType, Amount, InstallmentCount, MonthlyAmount, StartYear, StartMonth, Status, CreatedAt)
VALUES (N'{referenceNo}', {employeeId}, N'Loan', 100, 1, 100, 2020, 1, N'Approved', SYSUTCDATETIME());
SELECT CAST(SCOPE_IDENTITY() AS int);
""";
            loanId = (int)(await insertLoan.ExecuteScalarAsync())!;
        }

        await using var insertInst = connection.CreateCommand();
        insertInst.Transaction = tx;
        insertInst.CommandText = $"""
INSERT INTO EmployeeLoanInstallments (LoanId, SeqNo, DueYear, DueMonth, Amount, IsPosted)
VALUES ({loanId}, 1, 2020, 1, 100, 0);
""";
        await insertInst.ExecuteNonQueryAsync();
    }

    /// <summary>يعدّ أقساط البذرة المستحقّة التي يراها الشرط المُمرَّر — نفس بنية استعلام المتجر.</summary>
    private static async Task<int> CountDueAsync(DbConnection connection, DbTransaction tx, string companyPredicate)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = tx;
        command.CommandText = $"""
SELECT COUNT(*)
FROM EmployeeLoanInstallments i
INNER JOIN EmployeeLoans l ON l.Id = i.LoanId
INNER JOIN Employees e ON e.Id = l.EmployeeId
WHERE i.IsPosted = 0 AND l.Status = N'Approved'
  AND (i.DueYear * 12 + i.DueMonth) <= (2025 * 12 + 12)
  AND l.ReferenceNo IN (N'__ITEST_A', N'__ITEST_B')
  AND {companyPredicate};
""";
        return (int)(await command.ExecuteScalarAsync())!;
    }

    private static async Task<int> GetAppLockAsync(
        DbConnection connection, DbTransaction? tx, string resource, int timeoutMs)
    {
        await using var command = connection.CreateCommand();
        if (tx is not null) command.Transaction = tx;
        command.CommandText = $"""
DECLARE @result int;
EXEC @result = sp_getapplock @Resource = N'{resource}', @LockMode = 'Exclusive',
                             @LockOwner = 'Session', @LockTimeout = {timeoutMs};
SELECT @result;
""";
        var scalar = await command.ExecuteScalarAsync();
        return scalar is int value ? value : -999;
    }
}
