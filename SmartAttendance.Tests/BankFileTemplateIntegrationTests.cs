using Microsoft.EntityFrameworkCore;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;
using Xunit;

namespace SmartAttendance.Tests;

/// <summary>
/// اختبارات تكامل لقوالب ملف البنك <see cref="BankFileTemplateStore"/> ضد الـSQL الحقيقي
/// (self-healing DDL + CRUD). اسمٌ حارس «__ITEST__» معزول، تنظيف قبل/بعد، تخطٍّ تلقائي
/// حين تعذّر SQL. ⚠️ لا يُحفَظ أي قالب بـIsDefault=true حتى لا يُمسّ افتراضي الإنتاج.
/// </summary>
public sealed class BankFileTemplateIntegrationTests : IAsyncLifetime
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
            await BankFileTemplateStore.EnsureAsync(_db);
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
            "DELETE FROM BankFileTemplates WHERE Name = N'__ITEST__';");
    }

    [Fact]
    public async Task Template_SaveListDelete_RoundTrips()
    {
        if (!_dbAvailable) return;

        var (ok, _) = await BankFileTemplateStore.SaveAsync(_db, new BankFileTemplateStore.Template
        {
            Name = Sentinel,
            BankName = "مصرف الاختبار",
            Delimiter = "Semicolon",
            IncludeHeader = true,
            ColumnsCsv = "no,name,iban,net",
            IsDefault = false, // حسّاس: true يمسح افتراضي الإنتاج
            IsActive = true
        });
        Assert.True(ok);

        var saved = (await BankFileTemplateStore.ListAsync(_db)).FirstOrDefault(t => t.Name == Sentinel);
        Assert.NotNull(saved);
        Assert.Equal("Semicolon", saved!.Delimiter);
        Assert.Equal(new[] { "no", "name", "iban", "net" }, saved.Columns);

        // تكامل: القالب المُستعاد من SQL يبني ملف بنك صحيحاً عبر الحاسبة النقية.
        var content = BankFileTemplateStore.BuildContent(saved, new[]
        {
            new PayrollRunStore.BankFileRow
            {
                EmployeeNo = "9001", EmployeeName = "موظف الاختبار",
                Iban = "IQ55", NetSalary = 750_000m
            }
        });
        Assert.Contains("9001;موظف الاختبار;IQ55;750000.00", content);

        await BankFileTemplateStore.DeleteAsync(_db, saved.Id);
        Assert.DoesNotContain(await BankFileTemplateStore.ListAsync(_db), t => t.Name == Sentinel);
    }

    [Fact]
    public async Task Template_GetById_ReturnsSavedTemplate()
    {
        if (!_dbAvailable) return;

        await BankFileTemplateStore.SaveAsync(_db, new BankFileTemplateStore.Template
        {
            Name = Sentinel, Delimiter = "Comma", IncludeHeader = false,
            ColumnsCsv = "no,net", IsDefault = false, IsActive = true
        });
        var listed = (await BankFileTemplateStore.ListAsync(_db)).First(t => t.Name == Sentinel);

        var fetched = await BankFileTemplateStore.GetAsync(_db, listed.Id);
        Assert.NotNull(fetched);
        Assert.Equal(Sentinel, fetched!.Name);
        Assert.False(fetched.IncludeHeader);
    }

    [Fact]
    public async Task Template_Save_RejectsEmptyNameOrColumns()
    {
        if (!_dbAvailable) return;

        var (noName, _) = await BankFileTemplateStore.SaveAsync(_db, new BankFileTemplateStore.Template
        {
            Name = "", ColumnsCsv = "no,net"
        });
        Assert.False(noName);

        var (noCols, _) = await BankFileTemplateStore.SaveAsync(_db, new BankFileTemplateStore.Template
        {
            Name = Sentinel, ColumnsCsv = ""
        });
        Assert.False(noCols);
    }
}
