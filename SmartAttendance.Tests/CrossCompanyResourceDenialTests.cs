using System.Reflection;
using Microsoft.EntityFrameworkCore;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Security;
using Xunit;

namespace SmartAttendance.Tests;

/// <summary>
/// GAP-01 — «مستخدم شركة A يُرفض على مورد شركة B»، **بالتنفيذ على بيانات حقيقية**
/// لا بمسحٍ نصّيّ.
///
/// <para><b>لماذا وُجد هذا الملف:</b> تدقيق 2026-08-11 أثبت أن منظومةً من 1,624
/// اختباراً خضراء بالكامل لم تمنع AUTHZ-003 — حذفَ أي طلب إجازة بأي شركة من أدنى
/// دور. السبب أن أغلب اختبارات العزل تفحص **نصّ** الكود (تبحث عن كلمة
/// <c>CompanyScope</c> بملفّ) لا **سلوكه**. سقّاطةٌ نصّيّة تنجح حتى لو مُرِّر
/// النطاق خطأً.</para>
///
/// <para><b>المنهج:</b> يُنفَّذ <see cref="EmployeeCompanyGuard.FilterOwnedRowsInScopeAsync"/>
/// — وهو كود الإنتاج نفسه الذي تستعمله الشاشات — على صفوفٍ حقيقية من **شركتين**،
/// ويُتحقَّق أن نطاق الشركة الأولى **لا يُعيد ولا صفّاً واحداً** من الثانية.</para>
///
/// <para><b>خاصيّة السقّاطة:</b> الاختبار مقاد بالبيانات عبر انعكاسٍ على
/// <see cref="EmployeeCompanyGuard.Tables"/>. أي جدولٍ يُضاف للكتالوج مستقبلاً
/// **يدخل التغطية تلقائياً** بلا كتابة اختبارٍ جديد. ويحرس اختبارٌ ثانٍ ألّا
/// ينكمش الكتالوج بصمت.</para>
///
/// <para>⚠️ يعمل على <c>SmartAttendance_Test</c> حصراً، و<b>قراءةً فقط</b> (SELECT
/// وحده) — فلا يُبقي أثراً ولا يحتاج معاملة. غياب القاعدة ⟹ تخطٍّ لا فشل.</para>
/// </summary>
public sealed class CrossCompanyResourceDenialTests : IAsyncLifetime
{
    // قاعدة الاختبار حصراً — القاعدة الحمراء: ممنوع لمس الإنتاج.
    private const string ConnectionString =
        "Server=localhost;Database=SmartAttendance_Test;Trusted_Connection=True;" +
        "TrustServerCertificate=True;MultipleActiveResultSets=True";

    private ApplicationDbContext _db = null!;
    private bool _dbAvailable;

    public async Task InitializeAsync()
    {
        _db = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseSqlServer(ConnectionString).Options);

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

    /// <summary>كل أسماء الجداول بكتالوج الحارس — مصدر الحقيقة، بالانعكاس.</summary>
    public static IEnumerable<object[]> GuardedTables() =>
        typeof(EmployeeCompanyGuard.Tables)
            .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => new object[] { (string)f.GetRawConstantValue()! })
            .OrderBy(x => (string)x[0]);

    [SkippableTheory]
    [MemberData(nameof(GuardedTables))]
    public async Task ScopedUser_ReceivesNoRowFromAnotherCompany(string table)
    {
        Skip.IfNot(_dbAvailable, "SmartAttendance_Test غير متاحة محلياً.");
        var connection = _db.Database.GetDbConnection();

        // الجدول قد لا يكون موجوداً بهذه القاعدة (شفاء ذاتي عند أول استعمال).
        if (!await TableExistsAsync(connection, table))
        {
            Skip.If(true, $"الجدول {table} غير موجود بقاعدة الاختبار.");
        }

        // نلتقط صفّاً حقيقياً من شركتين **مختلفتين** بنفس الجدول.
        var rows = await PickRowsFromTwoCompaniesAsync(connection, table);

        Skip.If(rows.Count < 2, $"{table}: لا توجد صفوف من شركتين مختلفتين بالبيانات المتاحة.");

        var mine = rows[0];
        var other = rows[1];

        Assert.NotEqual(mine.CompanyId, other.CompanyId);

        var scope = CompanyScope.ForCompanies(new[] { mine.CompanyId });

        var allowed = await EmployeeCompanyGuard.FilterOwnedRowsInScopeAsync(
            _db, table, "Id", new[] { mine.RowId, other.RowId }, scope);

        // الجوهر: صفّي يمرّ، وصفّ الشركة الأخرى **لا يمرّ**.
        Assert.Contains(mine.RowId, allowed);
        Assert.DoesNotContain(other.RowId, allowed);
    }

    [SkippableFact]
    public async Task UnrestrictedAdmin_StillReachesBothCompanies()
    {
        // الضدّ الضروريّ: لو أرجع الحارس مجموعةً فارغة دائماً لنجح الاختبار الأول
        // زوراً. هذا يُثبت أن الحارس يميّز ولا يمنع الجميع.
        Skip.IfNot(_dbAvailable, "SmartAttendance_Test غير متاحة محلياً.");
        var connection = _db.Database.GetDbConnection();

        const string table = EmployeeCompanyGuard.Tables.AttendanceRecords;
        Skip.IfNot(await TableExistsAsync(connection, table), $"{table} غير موجود.");

        var rows = await PickRowsFromTwoCompaniesAsync(connection, table);
        Skip.If(rows.Count < 2, "لا توجد صفوف من شركتين مختلفتين.");

        var allowed = await EmployeeCompanyGuard.FilterOwnedRowsInScopeAsync(
            _db, table, "Id",
            new[] { rows[0].RowId, rows[1].RowId },
            CompanyScope.Unrestricted());

        Assert.Contains(rows[0].RowId, allowed);
        Assert.Contains(rows[1].RowId, allowed);
    }

    [Fact]
    public void GuardCatalog_DoesNotShrinkSilently()
    {
        // خطّ أساسٍ يمنع حذف جدولٍ من الكتالوج (فيخرج من التغطية بصمت).
        // زيادة العدد مقبولة ومرغوبة — النقصان يجب أن يكون قراراً واعياً.
        var count = GuardedTables().Count();

        Assert.True(
            count >= 12,
            $"كتالوج EmployeeCompanyGuard.Tables انكمش إلى {count} (كان 12 بتاريخ 2026-08-11). " +
            "حذف جدولٍ منه يُخرجه من اختبار العزل التنفيذيّ — راجع القرار.");
    }

    // ─────────────────────────────── أدوات ───────────────────────────────

    private sealed record OwnedRow(int RowId, int CompanyId);

    private static async Task<bool> TableExistsAsync(
        System.Data.Common.DbConnection connection,
        string table)
    {
        EmployeeCompanyGuard.GuardIdentifier(table);

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT CASE WHEN OBJECT_ID(@t, 'U') IS NULL THEN 0 ELSE 1 END;";

        var parameter = command.CreateParameter();
        parameter.ParameterName = "@t";
        parameter.Value = table;
        command.Parameters.Add(parameter);

        return Convert.ToInt32(await command.ExecuteScalarAsync()) == 1;
    }

    /// <summary>صفٌّ واحد من كل شركة — أقدم صفٍّ لكل شركة، ليكون الاختيار حتمياً.</summary>
    private static async Task<List<OwnedRow>> PickRowsFromTwoCompaniesAsync(
        System.Data.Common.DbConnection connection,
        string table)
    {
        EmployeeCompanyGuard.GuardIdentifier(table);

        await using var command = connection.CreateCommand();
        command.CommandText = $"""
SELECT TOP 2 MIN(t.Id) AS RowId, e.CompanyId
FROM {table} t
INNER JOIN Employees e ON e.Id = t.EmployeeId
WHERE e.CompanyId IS NOT NULL
GROUP BY e.CompanyId
ORDER BY e.CompanyId;
""";

        var rows = new List<OwnedRow>();
        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            rows.Add(new OwnedRow(reader.GetInt32(0), reader.GetInt32(1)));
        }

        return rows;
    }
}
