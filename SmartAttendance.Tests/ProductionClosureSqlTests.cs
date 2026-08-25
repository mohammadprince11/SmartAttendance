using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Text.RegularExpressions;
using SmartAttendance.Application.AttendanceImports.Services;
using SmartAttendance.Domain.Entities;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Infrastructure.Repositories;
using SmartAttendance.Infrastructure.Services;
using SmartAttendance.Web.Infrastructure.Hrms;
using SmartAttendance.Web.Infrastructure.Reports;
using SmartAttendance.Web.Infrastructure.Security;

namespace SmartAttendance.Tests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ProductionClosureSqlCollection
{
    public const string Name = "ProductionClosureSql";
}

[Collection(ProductionClosureSqlCollection.Name)]
public sealed class ProductionClosureSqlTests : IAsyncLifetime
{
    private string? _adminConnection;
    private string? _databaseName;
    private string? _connectionString;
    private bool _available;
    private bool _attempted;
    private string? _failure;
    private int _companyA;
    private int _companyB;
    private int _employeeA;
    private int _employeeB;

    private ApplicationDbContext NewContext() => new(
        new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(_connectionString!)
            .ConfigureWarnings(warnings => warnings.Ignore(RelationalEventId.PendingModelChangesWarning))
            .Options);

    public async Task InitializeAsync()
    {
        var configured = Environment.GetEnvironmentVariable("SMARTATTENDANCE_SQL_TEST_MASTER");
        if (string.IsNullOrWhiteSpace(configured) && OperatingSystem.IsWindows())
            configured = @"Server=(localdb)\MSSQLLocalDB;Database=master;Integrated Security=true;TrustServerCertificate=true";
        if (string.IsNullOrWhiteSpace(configured)) return;
        _attempted = true;

        _databaseName = "SmartAttendance_CodexAcceptance_" + Guid.NewGuid().ToString("N");
        var adminBuilder = new SqlConnectionStringBuilder(configured) { InitialCatalog = "master" };
        _adminConnection = adminBuilder.ConnectionString;

        try
        {
            await using (var admin = new SqlConnection(_adminConnection))
            {
                await admin.OpenAsync();
                await using var create = admin.CreateCommand();
                create.CommandText = $"CREATE DATABASE [{_databaseName}];";
                await create.ExecuteNonQueryAsync();
            }

            var databaseBuilder = new SqlConnectionStringBuilder(configured)
            {
                InitialCatalog = _databaseName,
                MultipleActiveResultSets = true
            };
            _connectionString = databaseBuilder.ConnectionString;

            await using var db = NewContext();
            await InitializeModelSchemaAsync(db);
            await SalaryItemStore.EnsureAsync(db);
            await EmployeeAllowanceSchema.EnsureAsync(db);
            await PayrollTransactionStore.EnsureAsync(db);
            await PayrollRunStore.EnsureAsync(db);
            await SqlSchemaMigrator.ApplyAsync(db);
            await SeedCompaniesAsync(db);
            _available = true;
        }
        catch (Exception ex)
        {
            _available = false;
            _failure = ex.ToString();
        }
    }

    public async Task DisposeAsync()
    {
        if (_adminConnection is null || _databaseName is null ||
            !_databaseName.StartsWith("SmartAttendance_CodexAcceptance_", StringComparison.Ordinal)) return;

        try
        {
            SqlConnection.ClearAllPools();
            await using var admin = new SqlConnection(_adminConnection);
            await admin.OpenAsync();
            await using var drop = admin.CreateCommand();
            drop.CommandText = $"ALTER DATABASE [{_databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{_databaseName}];";
            await drop.ExecuteNonQueryAsync();
        }
        catch
        {
            // The database name is unique and explicitly disposable. Cleanup is best effort
            // so a failed assertion is not hidden by a secondary cleanup exception.
        }
    }

    [SkippableFact]
    public async Task Attendance_import_duplicate_number_resolves_only_selected_company()
    {
        RequireSql();
        var directory = Path.Combine(Path.GetTempPath(), "zynora-sql-acceptance", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var sharedFile = Path.Combine(directory, "shared.csv");
            await File.WriteAllTextAsync(sharedFile,
                "EmployeeCardNumber,AttendanceDate\n10001,2099-01-10 08:00:00\n10001,2099-01-10 17:00:00\n");

            await using var db = NewContext();
            using var unit = new UnitOfWork(db);
            var service = new AttendanceImportService(unit, db);
            var scopeA = AttendanceImportScope.Restricted(new[] { _companyA }, _companyA);

            var preview = await service.PreviewAsync(sharedFile, "test", "shared.csv", scopeA);
            var row = Assert.Single(preview.Rows);
            Assert.Equal(_employeeA, row.EmployeeId);

            var bBefore = await db.AttendanceRecords.CountAsync(x => x.EmployeeId == _employeeB);
            var result = await service.ImportAsync(sharedFile, "shared.csv", scopeA);
            db.ChangeTracker.Clear();

            Assert.Equal(1, result.ImportedCount);
            Assert.Equal(1, await db.AttendanceRecords.CountAsync(x =>
                x.EmployeeId == _employeeA && x.AttendanceDate == new DateOnly(2099, 1, 10)));
            Assert.Equal(bBefore, await db.AttendanceRecords.CountAsync(x => x.EmployeeId == _employeeB));

            var bOnlyFile = Path.Combine(directory, "b-only.csv");
            await File.WriteAllTextAsync(bOnlyFile,
                "EmployeeCardNumber,AttendanceDate\n20002,2099-01-11 08:00:00\n");
            var hidden = await service.PreviewAsync(bOnlyFile, "test", "b-only.csv", scopeA);
            Assert.Null(Assert.Single(hidden.Rows).EmployeeId);
            var hiddenImport = await service.ImportAsync(bOnlyFile, "b-only.csv", scopeA);
            Assert.Equal(0, hiddenImport.ImportedCount);
            Assert.Equal(bBefore, await db.AttendanceRecords.CountAsync(x => x.EmployeeId == _employeeB));
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [SkippableFact]
    public async Task Payroll_run_sequence_survives_deletion_and_twenty_parallel_creates()
    {
        RequireSql();
        var scope = CompanyScope.ForCompanies(new[] { _companyA });

        await using (var db = NewContext())
        {
            var created = new List<int>();
            for (var i = 0; i < 3; i++)
            {
                var result = await PayrollRunStore.CreateRunAsync(db, scope, _companyA, 2097, 8);
                Assert.True(result.Ok, result.Message);
                created.Add(result.RunId);
            }
            Assert.True((await PayrollRunStore.DeleteRunAsync(db, created[1])).Item1);
            var fourth = await PayrollRunStore.CreateRunAsync(db, scope, _companyA, 2097, 8);
            Assert.True(fourth.Ok, fourth.Message);
            Assert.EndsWith("-4", (await PayrollRunStore.GetRunAsync(db, fourth.RunId))!.BatchNo);
        }

        var tasks = Enumerable.Range(0, 20).Select(async _ =>
        {
            await using var db = NewContext();
            return await PayrollRunStore.CreateRunAsync(
                db, scope, _companyA, 2098, 9, PayrollRunScope.ModeManual, new[] { _employeeA });
        });
        var results = await Task.WhenAll(tasks);
        Assert.All(results, x => Assert.True(x.Ok, x.Message));
        Assert.Equal(20, results.Select(x => x.RunId).Distinct().Count());

        await using var verify = NewContext();
        var batches = await RawStringsAsync(verify,
            "SELECT BatchNo FROM PayrollRuns WHERE [Year]=2098 AND [Month]=9;");
        Assert.Equal(20, batches.Count);
        Assert.Equal(20, batches.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(20, await RawIntAsync(verify,
            "SELECT COUNT(*) FROM PayrollRunScopeMembers s INNER JOIN PayrollRuns r ON r.Id=s.RunId WHERE r.[Year]=2098 AND r.[Month]=9;"));
        Assert.Equal(0, await RawIntAsync(verify,
            "SELECT COUNT(*) FROM PayrollRunScopeMembers s LEFT JOIN PayrollRuns r ON r.Id=s.RunId WHERE r.Id IS NULL;"));
    }

    [SkippableFact]
    public async Task Payroll_transactions_reject_cross_company_and_allocate_unique_references()
    {
        RequireSql();
        var scopeA = CompanyScope.ForCompanies(new[] { _companyA });

        int bId;
        await using (var db = NewContext())
        {
            bId = await PayrollTransactionStore.SaveAsync(db, CompanyScope.Unrestricted(),
                Transaction(_employeeB, 2096, 7), "sql-test");
            Assert.Empty(await PayrollTransactionStore.ListAsync(
                db, scopeA, 2096, 7, PayrollTransactionStore.Income, null));
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                PayrollTransactionStore.SaveAsync(db, scopeA, Transaction(_employeeB, 2096, 7), "malicious-a"));
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                PayrollTransactionStore.DeleteAsync(db, scopeA, bId));
            Assert.Equal(1, await RawIntAsync(db, $"SELECT COUNT(*) FROM PayrollTransactions WHERE Id={bId};"));
        }

        var createTasks = Enumerable.Range(0, 20).Select(async _ =>
        {
            await using var db = NewContext();
            return await PayrollTransactionStore.SaveAsync(
                db, scopeA, Transaction(_employeeA, 2096, 8), "sql-test");
        });
        var ids = await Task.WhenAll(createTasks);

        await using var verify = NewContext();
        var refs = await RawStringsAsync(verify,
            "SELECT ReferenceNo FROM PayrollTransactions WHERE [Year]=2096 AND [Month]=8;");
        Assert.Equal(20, refs.Count);
        Assert.Equal(20, refs.Distinct(StringComparer.Ordinal).Count());

        var maxBefore = refs.Max(ParseReferenceSuffix);
        await PayrollTransactionStore.DeleteAsync(verify, scopeA, ids[5]);
        var nextId = await PayrollTransactionStore.SaveAsync(
            verify, scopeA, Transaction(_employeeA, 2096, 8), "sql-test");
        var nextRef = Assert.Single(await RawStringsAsync(verify,
            $"SELECT ReferenceNo FROM PayrollTransactions WHERE Id={nextId};"));
        Assert.True(ParseReferenceSuffix(nextRef) > maxBefore);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            PayrollTransactionStore.DeleteManyAsync(verify, scopeA, new[] { ids[0], bId }));
        Assert.Equal(1, await RawIntAsync(verify,
            $"SELECT COUNT(*) FROM PayrollTransactions WHERE Id={ids[0]};"));
        Assert.Equal(1, await RawIntAsync(verify,
            $"SELECT COUNT(*) FROM PayrollTransactions WHERE Id={bId};"));
    }

    [SkippableFact]
    public async Task Salary_items_reject_cross_company_reads_writes_and_deletes()
    {
        RequireSql();
        await using var db = NewContext();
        var scopeA = CompanyScope.ForCompanies(new[] { _companyA });
        var scopeB = CompanyScope.ForCompanies(new[] { _companyB });

        var itemA = new SalaryItemStore.SalaryItem
        {
            CompanyId = _companyA, Name = "Company A allowance", ItemType = "Income",
            ValueKind = "Fixed", DefaultValue = 100, IsActive = true
        };
        var itemB = new SalaryItemStore.SalaryItem
        {
            CompanyId = _companyB, Name = "Company B allowance", ItemType = "Income",
            ValueKind = "Fixed", DefaultValue = 200, IsActive = true
        };
        Assert.True(await SalaryItemStore.SaveAsync(db, scopeA, itemA));
        Assert.True(await SalaryItemStore.SaveAsync(db, scopeB, itemB));

        var aRows = await SalaryItemStore.ListAsync(db, scopeA, _companyA);
        Assert.Contains(aRows, row => row.Name == itemA.Name);
        Assert.DoesNotContain(aRows, row => row.Name == itemB.Name);

        var bId = await ScalarAsync(db,
            $"SELECT Id FROM SalaryItems WHERE CompanyId={_companyB} AND Name=N'Company B allowance';");
        itemB.Id = bId;
        itemB.Name = "Malicious rename";
        Assert.False(await SalaryItemStore.SaveAsync(db, scopeA, itemB));
        Assert.False(await SalaryItemStore.DeleteAsync(db, scopeA, bId));
        Assert.Equal("Company B allowance", Assert.Single(await RawStringsAsync(
            db, $"SELECT Name FROM SalaryItems WHERE Id={bId};")));
    }

    [SkippableFact]
    public async Task Saved_reports_reject_cross_company_reads_and_deletes()
    {
        RequireSql();
        await using var db = NewContext();
        var scopeA = CompanyScope.ForCompanies(new[] { _companyA });
        var scopeB = CompanyScope.ForCompanies(new[] { _companyB });

        await PeopleReportsStore.EnsureSchemaAsync(db);
        await PeopleReportsStore.CreateAsync(
            db, scopeA, _companyA, "Company A custom report", null, "employees", "no,name", "owner-a", false);
        await PeopleReportsStore.CreateAsync(
            db, scopeB, _companyB, "Company B custom report", null, "employees", "no,name", "owner-b", false);

        var aRows = await PeopleReportsStore.LoadAllAsync(db, scopeA);
        Assert.Contains(aRows, report => report.Name == "Company A custom report");
        Assert.DoesNotContain(aRows, report => report.Name == "Company B custom report");

        var bId = await ScalarAsync(db,
            $"SELECT Id FROM PeopleReports WHERE CompanyId={_companyB} AND Name=N'Company B custom report';");
        Assert.Null(await PeopleReportsStore.GetAsync(db, scopeA, bId));
        await PeopleReportsStore.DeleteOwnAsync(db, scopeA, bId, "owner-b");
        Assert.Equal(1, await RawIntAsync(db, $"SELECT COUNT(*) FROM PeopleReports WHERE Id={bId} AND IsDeleted=0;"));
    }

    [SkippableFact]
    public async Task Allowance_identity_audit_is_unambiguous_and_fk_backed()
    {
        RequireSql();
        await using var db = NewContext();
        var salaryItemId = await ScalarAsync(db, """
INSERT INTO SalaryItems (CompanyId, Name, ItemType, ValueKind, DefaultValue, Taxable, GosiEligible, InGross, Prorated, OvertimeEligible, UnpaidLeaveEligible, IsSystem, IsActive, SortOrder, CreatedAt)
VALUES ({_companyA}, N'Housing', N'Income', N'PerEmployee', 0, 0, 1, 1, 1, 1, 0, 0, 1, 10, SYSUTCDATETIME());
SELECT CAST(SCOPE_IDENTITY() AS int);
""");
        await ExecuteAsync(db, $"""
INSERT INTO EmployeeAllowances (EmployeeId, SalaryItemId, ItemName, Amount, FromDate, EndAfterDate, CreatedAt, IsDeleted)
VALUES ({_employeeA}, {salaryItemId}, N'Housing', 1600000, '2095-01-01', 0, SYSUTCDATETIME(), 0);
""");

        Assert.Equal(1, await RawIntAsync(db, "SELECT COUNT(*) FROM EmployeeAllowances;"));
        Assert.Equal(1, await RawIntAsync(db, "SELECT COUNT(*) FROM EmployeeAllowances a INNER JOIN SalaryItems s ON s.Id=a.SalaryItemId;"));
        Assert.Equal(0, await RawIntAsync(db, "SELECT COUNT(*) FROM EmployeeAllowances WHERE SalaryItemId IS NULL;"));
        Assert.Equal(0, await RawIntAsync(db, "SELECT COUNT(*) FROM (SELECT Name FROM SalaryItems GROUP BY Name HAVING COUNT(*)>1) d;"));
        Assert.Equal(1, await RawIntAsync(db, "SELECT COUNT(*) FROM sys.foreign_keys WHERE name='FK_EmployeeAllowances_SalaryItems_SalaryItemId';"));
    }

    private async Task SeedCompaniesAsync(ApplicationDbContext db)
    {
        var a = new Company { Name = "SQL Company A", Code = "SQL-A" };
        var b = new Company { Name = "SQL Company B", Code = "SQL-B" };
        var branchA = new Branch { Name = "A Branch", Code = "SQL-BA", Company = a };
        var branchB = new Branch { Name = "B Branch", Code = "SQL-BB", Company = b };
        var deptA = new Department { Name = "A Department", Code = "SQL-DA", Company = a, Branch = branchA };
        var deptB = new Department { Name = "B Department", Code = "SQL-DB", Company = b, Branch = branchB };
        var empA = new Employee
        {
            EmployeeNo = "10001", FullName = "Employee A", CompanyId = 0,
            Branch = branchA, Department = deptA,
            HireDate = new DateOnly(2090, 1, 1), IsActive = true
        };
        var empB = new Employee
        {
            EmployeeNo = "20002", FullName = "Employee B", CompanyId = 0,
            Branch = branchB, Department = deptB,
            HireDate = new DateOnly(2090, 1, 1), IsActive = true
        };
        db.AddRange(a, b, branchA, branchB, deptA, deptB);
        await db.SaveChangesAsync();
        empA.CompanyId = a.Id;
        empB.CompanyId = b.Id;
        db.AddRange(empA, empB);
        await db.SaveChangesAsync();
        var secondB = new Employee
        {
            EmployeeNo = "10001", FullName = "Employee B Shared", CompanyId = b.Id,
            BranchId = branchB.Id, DepartmentId = deptB.Id,
            HireDate = new DateOnly(2090, 1, 1), IsActive = true
        };
        db.Add(secondB);
        await db.SaveChangesAsync();

        _companyA = a.Id;
        _companyB = b.Id;
        _employeeA = empA.Id;
        _employeeB = secondB.Id;
    }

    private static async Task InitializeModelSchemaAsync(ApplicationDbContext db)
    {
        // HrJobPositions is intentionally excluded from EF migrations, while modeled
        // relationships still reference it. A fresh disposable database therefore needs
        // this legacy-owned table before the generated current-model DDL is executed.
        await ExecuteAsync(db, """
CREATE TABLE dbo.HrJobPositions
(
    Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_HrJobPositions PRIMARY KEY,
    CompanyId int NOT NULL,
    ArabicName nvarchar(400) NOT NULL,
    EnglishName nvarchar(400) NULL,
    DepartmentId int NULL,
    IsActive bit NOT NULL
);
""");

        var script = db.Database.GenerateCreateScript();
        foreach (var batch in Regex.Split(
                     script,
                     @"^\s*GO\s*;?\s*$",
                     RegexOptions.Multiline | RegexOptions.IgnoreCase))
        {
            if (!string.IsNullOrWhiteSpace(batch)) await ExecuteAsync(db, batch);
        }
    }

    private void RequireSql()
    {
        Skip.IfNot(_attempted, "Set SMARTATTENDANCE_SQL_TEST_MASTER to run the disposable SQL suite.");
        Assert.True(_available, $"Disposable SQL Server/LocalDB initialization failed. {_failure}");
    }

    private static PayrollTransactionStore.Transaction Transaction(int employeeId, int year, int month) => new()
    {
        EmployeeId = employeeId,
        Year = year,
        Month = month,
        ItemName = "SQL acceptance income",
        Amount = 100,
        TxType = PayrollTransactionStore.Income,
        PaymentType = "InSalary",
        Status = "Approved",
        Source = "SQL acceptance"
    };

    private static int ParseReferenceSuffix(string value) =>
        int.Parse(value[(value.LastIndexOf('-') + 1)..]);

    private static async Task<List<string>> RawStringsAsync(ApplicationDbContext db, string sql)
    {
        var result = new List<string>();
        var connection = db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open) await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) result.Add(reader.GetString(0));
        return result;
    }

    private static async Task<int> RawIntAsync(ApplicationDbContext db, string sql) =>
        Convert.ToInt32(await ScalarObjectAsync(db, sql));

    private static async Task<int> ScalarAsync(ApplicationDbContext db, string sql) =>
        Convert.ToInt32(await ScalarObjectAsync(db, sql));

    private static async Task<object?> ScalarObjectAsync(ApplicationDbContext db, string sql)
    {
        var connection = db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open) await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return await command.ExecuteScalarAsync();
    }

    private static async Task ExecuteAsync(ApplicationDbContext db, string sql)
    {
        var connection = db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open) await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }
}
