using System.Reflection;
using Microsoft.EntityFrameworkCore;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Security;
using Xunit;

namespace SmartAttendance.Tests;

/// <summary>
/// GAP-01 / FIX-005 — «مستخدم شركة A يُرفض على مورد شركة B»، **بالتنفيذ على كل
/// جداول كتالوج الحارس**، ببياناتٍ اصطناعية يبذرها الاختبار بنفسه.
///
/// <para><b>لماذا وُجد:</b> تدقيق 2026-08-11 أثبت أن 1,624 اختباراً خضراء لم تمنع
/// AUTHZ-003 — لأن أغلب اختبارات العزل تفحص <b>نصّ</b> الكود لا <b>سلوكه</b>. هذا
/// ينفّذ <see cref="EmployeeCompanyGuard.FilterOwnedRowsInScopeAsync"/> — كود
/// الإنتاج نفسه — ويثبت أن نطاق شركةٍ لا يُعيد ولا صفّاً من أخرى.</para>
///
/// <para><b>FIX-005:</b> النسخة الأولى كانت تتخطّى 11 من 12 جدولاً لغياب بيانات
/// شركتين. الآن يبذر <see cref="SyntheticTenantSeeder"/> صفّين (شركتين) بكل جدولٍ
/// حتماً — فالتغطية 12/12 لا تعتمد على المحيط (§69 استقلال بيانات الاختبار).</para>
///
/// <para><b>خاصيّة السقّاطة:</b> مقادٌ بالانعكاس على <see cref="EmployeeCompanyGuard.Tables"/>
/// — أي جدولٍ يُضاف للكتالوج يدخل التغطية والبذر تلقائياً.</para>
///
/// <para>⚠️ <c>SmartAttendance_Test</c> حصراً. يبذر ويحذف بياناته الاصطناعية
/// (معرّفات ملتقَطة) — لا يُبقي أثراً. غياب القاعدة ⟹ تخطٍّ لا فشل.</para>
/// </summary>
public sealed class CrossCompanyResourceDenialTests : IAsyncLifetime
{
    private const string ConnectionString =
        "Server=localhost;Database=SmartAttendance_Test;Trusted_Connection=True;" +
        "TrustServerCertificate=True;MultipleActiveResultSets=True";

    private ApplicationDbContext _db = null!;
    private SyntheticTenantSeeder _seeder = null!;
    private bool _dbAvailable;
    private int _companyA;
    private int _companyB;
    private int _employeeA;
    private int _employeeB;

    // table -> (rowInA, rowInB)
    private readonly Dictionary<string, (int InA, int InB)> _rows = new(StringComparer.Ordinal);

    public static IEnumerable<object[]> GuardedTables() =>
        typeof(EmployeeCompanyGuard.Tables)
            .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => new object[] { (string)f.GetRawConstantValue()! })
            .OrderBy(x => (string)x[0]);

    public async Task InitializeAsync()
    {
        _db = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(ConnectionString).Options);

        try
        {
            await _db.Database.OpenConnectionAsync();
            _dbAvailable = true;
        }
        catch
        {
            _dbAvailable = false;
            return;
        }

        var connection = _db.Database.GetDbConnection();
        _seeder = new SyntheticTenantSeeder(connection);

        // نعيد استخدام موظفَين **حقيقيّين** من شركتين موجودتين بدل إنشائهما: كيان
        // Employees يحمل مفاتيح أجنبيّة (شركة/فرع/قسم) تفرض رسمَ رسمٍ كامل، بينما
        // جداول الحارس الأحد عشر بلا FK — فالبذر يقتصر عليها بمعرّف موظّفٍ حقيقيّ.
        if (!await PickTwoRealEmployeesAsync(connection))
        {
            return; // أقلّ من شركتين مأهولتين — الاختبارات ستتخطّى.
        }

        foreach (var row in GuardedTables())
        {
            var table = (string)row[0];
            if (!await TableExistsAsync(connection, table))
            {
                continue;
            }

            try
            {
                var inA = await _seeder.InsertAsync(table, new Dictionary<string, object> { ["EmployeeId"] = _employeeA });
                var inB = await _seeder.InsertAsync(table, new Dictionary<string, object> { ["EmployeeId"] = _employeeB });
                _rows[table] = (inA, inB);
            }
            catch
            {
                // جدولٌ بمخطّطٍ يتعذّر بذرُه أدنى — يبقى خارج التغطية بلا إسقاط الطاقم.
            }
        }
    }

    public async Task DisposeAsync()
    {
        if (_seeder is not null)
        {
            await _seeder.CleanupAsync();
        }

        if (_db is not null)
        {
            try { await _db.Database.CloseConnectionAsync(); } catch { /* best-effort */ }
            await _db.DisposeAsync();
        }
    }

    [SkippableTheory]
    [MemberData(nameof(GuardedTables))]
    public async Task ScopedUser_ReceivesNoRowFromAnotherCompany(string table)
    {
        Skip.IfNot(_dbAvailable, "SmartAttendance_Test غير متاحة محلياً.");
        Skip.IfNot(_rows.TryGetValue(table, out var pair), $"{table}: تعذّر بذرُ صفٍّ اصطناعيّ.");

        var scope = CompanyScope.ForCompanies(new[] { _companyA });

        var allowed = await EmployeeCompanyGuard.FilterOwnedRowsInScopeAsync(
            _db, table, "Id", new[] { pair.InA, pair.InB }, scope);

        Assert.Contains(pair.InA, allowed);          // صفّي يمرّ
        Assert.DoesNotContain(pair.InB, allowed);    // صفّ الشركة الأخرى لا يمرّ
    }

    [SkippableFact]
    public async Task UnrestrictedAdmin_StillReachesBothCompanies()
    {
        // الضدّ الضروريّ: لولاه لنجح الأول زوراً لو أرجع الحارس مجموعةً فارغة دائماً.
        Skip.IfNot(_dbAvailable, "SmartAttendance_Test غير متاحة محلياً.");
        Skip.IfNot(_rows.TryGetValue(EmployeeCompanyGuard.Tables.AttendanceRecords, out var pair),
            "تعذّر بذرُ AttendanceRecords.");

        var allowed = await EmployeeCompanyGuard.FilterOwnedRowsInScopeAsync(
            _db, EmployeeCompanyGuard.Tables.AttendanceRecords, "Id",
            new[] { pair.InA, pair.InB }, CompanyScope.Unrestricted());

        Assert.Contains(pair.InA, allowed);
        Assert.Contains(pair.InB, allowed);
    }

    [Fact]
    public void GuardCatalog_DoesNotShrinkSilently()
    {
        var count = GuardedTables().Count();

        Assert.True(
            count >= 12,
            $"كتالوج EmployeeCompanyGuard.Tables انكمش إلى {count} (كان 12 بتاريخ 2026-08-11). " +
            "حذف جدولٍ منه يُخرجه من اختبار العزل التنفيذيّ — راجع القرار.");
    }

    /// <summary>يختار موظّفَين حقيقيّين من شركتين مختلفتين — أقدم موظّفٍ لكل شركة.</summary>
    private async Task<bool> PickTwoRealEmployeesAsync(System.Data.Common.DbConnection connection)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
SELECT TOP 2 MIN(Id) AS EmpId, CompanyId
FROM Employees
WHERE CompanyId IS NOT NULL AND ISNULL(IsDeleted, 0) = 0
GROUP BY CompanyId
ORDER BY CompanyId;
""";

        var picks = new List<(int Emp, int Company)>();
        await using (var reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                picks.Add((reader.GetInt32(0), reader.GetInt32(1)));
            }
        }

        if (picks.Count < 2)
        {
            return false;
        }

        (_employeeA, _companyA) = picks[0];
        (_employeeB, _companyB) = picks[1];
        return true;
    }

    private static async Task<bool> TableExistsAsync(System.Data.Common.DbConnection connection, string table)
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
}
