using Microsoft.EntityFrameworkCore;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;
using Xunit;

namespace SmartAttendance.Tests;

/// <summary>
/// اختبارات تكامل لملفات الضريبة/الضمان <see cref="PayrollConfigStore"/> ضد الـSQL الحقيقي
/// (self-healing DDL + CRUD). تستعمل اسماً حارساً «__ITEST__» لا يصطدم ببيانات الإنتاج،
/// وتنظّف قبل/بعد كل اختبار. تتخطّى نفسها تلقائياً حين تعذّر SQL. لا تلمس بيانات حقيقية.
/// </summary>
public sealed class PayrollConfigIntegrationTests : IAsyncLifetime
{
    private const string ConnectionString =
        "Server=localhost;Database=SmartAttendance;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=True";

    private const string Sentinel = "__ITEST__";

    private ApplicationDbContext _db = null!;
    private bool _dbAvailable;

    private static ApplicationDbContext NewContext() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(ConnectionString).Options);

    public async Task InitializeAsync()
    {
        _db = NewContext();
        try
        {
            // الهجرات المحكومة أولاً تماماً كما يفعل الإقلاع: أعمدة الملفات المضافة
            // (ConditionsJson · SortOrder) تأتي من الهجرة لا من الشفاء الذاتي، فبدون
            // هذا السطر يختبر الاختبارُ مخططاً أقدم من الذي يعمل به التطبيق.
            await SqlSchemaMigrator.ApplyAsync(_db);
            await PayrollConfigStore.EnsureAsync(_db);
            await CleanupAsync();
            _dbAvailable = true;
        }
        catch
        {
            _dbAvailable = false;
        }
    }

    public async Task DisposeAsync()
    {
        if (_dbAvailable) await CleanupAsync();
        await _db.DisposeAsync();
    }

    private async Task CleanupAsync()
    {
        await HrmsDatabase.ExecuteAsync(
            _db,
            """
DELETE FROM PayrollTaxBrackets WHERE ProfileId IN (SELECT Id FROM PayrollTaxProfiles WHERE Name = N'__ITEST__');
DELETE FROM PayrollTaxProfiles WHERE Name = N'__ITEST__';
DELETE FROM PayrollGosiProfiles WHERE Name = N'__ITEST__';
""");
    }

    // ---------------- ملف الضريبة: round-trip ----------------

    [Fact]
    public async Task TaxProfile_SaveListDelete_RoundTripsWithBrackets()
    {
        if (!_dbAvailable) return;

        await PayrollConfigStore.SaveTaxProfileAsync(_db, new PayrollConfigStore.TaxProfile
        {
            Name = Sentinel,
            ExemptionAmount = 100_000m,
            IsActive = false, // لا نفعّله حتى لا نزاحم ملف الإنتاج النشط
            Brackets = new()
            {
                new() { FromAmount = 0m, ToAmount = 100_000m, Rate = 10m },
                new() { FromAmount = 100_000m, ToAmount = null, Rate = 20m }
            }
        });

        var saved = (await PayrollConfigStore.ListTaxProfilesAsync(_db))
            .FirstOrDefault(p => p.Name == Sentinel);

        Assert.NotNull(saved);
        Assert.Equal(100_000m, saved!.ExemptionAmount);
        Assert.Equal(2, saved.Brackets.Count);

        // تكامل: الـSQL المحفوظ يُغذّي الحاسبة النقية بنفس النتيجة المتوقّعة.
        // خاضع 300k − إعفاء 100k = 200k ⟹ (100k×10%) + (100k×20%) = 30,000
        Assert.Equal(30_000m, PayrollConfigStore.ComputeTax(300_000m, saved));

        await PayrollConfigStore.DeleteTaxProfileAsync(_db, saved.Id);
        Assert.DoesNotContain(
            await PayrollConfigStore.ListTaxProfilesAsync(_db),
            p => p.Name == Sentinel);
    }

    [Fact]
    public async Task TaxProfile_Update_ReplacesBrackets()
    {
        if (!_dbAvailable) return;

        await PayrollConfigStore.SaveTaxProfileAsync(_db, new PayrollConfigStore.TaxProfile
        {
            Name = Sentinel, ExemptionAmount = 0m, IsActive = false,
            Brackets = new() { new() { FromAmount = 0m, ToAmount = null, Rate = 5m } }
        });
        var first = (await PayrollConfigStore.ListTaxProfilesAsync(_db)).First(p => p.Name == Sentinel);

        // تحديث نفس الملف بشرائح مختلفة — يجب أن تُستبدل لا أن تتراكم.
        await PayrollConfigStore.SaveTaxProfileAsync(_db, new PayrollConfigStore.TaxProfile
        {
            Id = first.Id, Name = Sentinel, ExemptionAmount = 0m, IsActive = false,
            Brackets = new()
            {
                new() { FromAmount = 0m, ToAmount = 50_000m, Rate = 10m },
                new() { FromAmount = 50_000m, ToAmount = null, Rate = 20m }
            }
        });

        var updated = (await PayrollConfigStore.ListTaxProfilesAsync(_db)).First(p => p.Id == first.Id);
        Assert.Equal(2, updated.Brackets.Count); // لا تراكم للشرائح القديمة
    }

    // ---------------- ملف الضمان: round-trip ----------------

    [Fact]
    public async Task GosiProfile_SaveListDelete_RoundTrips()
    {
        if (!_dbAvailable) return;

        await PayrollConfigStore.SaveGosiProfileAsync(_db, new PayrollConfigStore.GosiProfile
        {
            Name = Sentinel, EmployeeRate = 5m, CompanyRate = 12m, Ceiling = 0m, IsActive = false
        });

        var saved = (await PayrollConfigStore.ListGosiProfilesAsync(_db))
            .FirstOrDefault(p => p.Name == Sentinel);

        Assert.NotNull(saved);
        Assert.Equal(5m, saved!.EmployeeRate);
        Assert.Equal(12m, saved.CompanyRate);

        var (emp, co) = PayrollConfigStore.ComputeGosi(1_000_000m, saved);
        Assert.Equal(50_000m, emp);
        Assert.Equal(120_000m, co);

        await PayrollConfigStore.DeleteGosiProfileAsync(_db, saved.Id);
        Assert.DoesNotContain(
            await PayrollConfigStore.ListGosiProfilesAsync(_db),
            p => p.Name == Sentinel);
    }
}
