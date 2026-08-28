using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using SmartAttendance.Domain.Entities;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;
using SmartAttendance.Web.Infrastructure.Security;

if (args.Length != 1 || args[0] is not ("setup" or "teardown"))
    throw new InvalidOperationException("Usage: SmartAttendance.E2E.Bootstrap setup|teardown");

var databaseName = Environment.GetEnvironmentVariable("ZYNORA_E2E_DATABASE_NAME");
if (string.IsNullOrWhiteSpace(databaseName) ||
    !Regex.IsMatch(databaseName, "^SmartAttendance_E2E_[A-Za-z0-9_]+$"))
    throw new InvalidOperationException("ZYNORA_E2E_DATABASE_NAME must be a disposable SmartAttendance_E2E_* name.");

var masterConnection = Environment.GetEnvironmentVariable("SMARTATTENDANCE_SQL_TEST_MASTER");
if (string.IsNullOrWhiteSpace(masterConnection) && OperatingSystem.IsWindows())
    masterConnection = @"Server=(localdb)\MSSQLLocalDB;Database=master;Integrated Security=true;TrustServerCertificate=true";
if (string.IsNullOrWhiteSpace(masterConnection))
    throw new InvalidOperationException("SMARTATTENDANCE_SQL_TEST_MASTER is required on non-Windows hosts.");

static string Identifier(string value) => "[" + value.Replace("]", "]]" ) + "]";

var adminBuilder = new SqlConnectionStringBuilder(masterConnection) { InitialCatalog = "master" };
await using var admin = new SqlConnection(adminBuilder.ConnectionString);
await admin.OpenAsync();

if (args[0] == "teardown")
{
    SqlConnection.ClearAllPools();
    await using var drop = admin.CreateCommand();
    drop.CommandText = $"IF DB_ID(N'{databaseName}') IS NOT NULL BEGIN ALTER DATABASE {Identifier(databaseName)} SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE {Identifier(databaseName)}; END";
    await drop.ExecuteNonQueryAsync();
    Console.WriteLine("Disposable E2E database removed.");
    return;
}

await using (var create = admin.CreateCommand())
{
    create.CommandText = $"IF DB_ID(N'{databaseName}') IS NOT NULL THROW 51000, 'Disposable E2E database already exists.', 1; CREATE DATABASE {Identifier(databaseName)};";
    await create.ExecuteNonQueryAsync();
}

var databaseBuilder = new SqlConnectionStringBuilder(masterConnection)
{
    InitialCatalog = databaseName,
    MultipleActiveResultSets = true
};
var options = new DbContextOptionsBuilder<ApplicationDbContext>()
    .UseSqlServer(databaseBuilder.ConnectionString)
    .ConfigureWarnings(warnings => warnings.Ignore(RelationalEventId.PendingModelChangesWarning))
    .Options;

await using var db = new ApplicationDbContext(options);
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
foreach (var batch in Regex.Split(
             db.Database.GenerateCreateScript(),
             @"^\s*GO\s*;?\s*$",
             RegexOptions.Multiline | RegexOptions.IgnoreCase))
{
    if (!string.IsNullOrWhiteSpace(batch)) await ExecuteAsync(db, batch);
}

await SalaryItemStore.EnsureAsync(db);
await EmployeeAllowanceSchema.EnsureAsync(db);
await EmployeeEngagementSchema.EnsureAsync(db);
await PayrollTransactionStore.EnsureAsync(db);
await PayrollRunStore.EnsureAsync(db);
await HrmsDatabase.EnsureCreatedAsync(db);
await DayAttendanceStore.EnsureAsync(db);
await EndOfServiceStore.EnsureAsync(db);
await LoanStore.EnsureAsync(db);
await MissingPunchRequestStore.EnsureAsync(db);
await SalaryRaiseStore.EnsureAsync(db);
await ShiftOverrideStore.EnsureAsync(db);
await WebAuthnCredentialStore.EnsureAsync(db);
await SqlSchemaMigrator.ApplyAsync(db);

var companyA = new Company { Name = "ZYNORA E2E A", Code = "E2E-A" };
var companyB = new Company { Name = "ZYNORA E2E B", Code = "E2E-B" };
var branchA = new Branch { Name = "E2E Branch A", Code = "E2E-BA", Company = companyA };
var branchB = new Branch { Name = "E2E Branch B", Code = "E2E-BB", Company = companyB };
var departmentA = new Department
{
    Name = "E2E Department A", Code = "E2E-DA", Company = companyA, Branch = branchA
};
var departmentB = new Department
{
    Name = "E2E Department B", Code = "E2E-DB", Company = companyB, Branch = branchB
};
db.AddRange(companyA, companyB, branchA, branchB, departmentA, departmentB);
await db.SaveChangesAsync();
var employeeA = new Employee
{
    EmployeeNo = "E2E-001", FullName = "Synthetic Employee A",
    CompanyId = companyA.Id, BranchId = branchA.Id, DepartmentId = departmentA.Id,
    HireDate = new DateOnly(2026, 1, 1), IsActive = true
};
var employeeB = new Employee
{
    EmployeeNo = "E2E-002", FullName = "Synthetic Employee B",
    CompanyId = companyB.Id, BranchId = branchB.Id, DepartmentId = departmentB.Id,
    HireDate = new DateOnly(2026, 1, 1), IsActive = true
};
db.AddRange(employeeA, employeeB);
await db.SaveChangesAsync();
await db.Database.ExecuteSqlInterpolatedAsync($"""
    INSERT INTO DayAttendances(EmployeeId, WorkDate, Status, IsAnalyzed, AnalyzedAt)
    VALUES ({employeeB.Id}, {new DateOnly(2026, 1, 5)}, N'Present', 1, {new DateTime(2026, 1, 5, 17, 0, 0)});
    """);
await LoginDatabase.EnsureCreatedAsync(db);

Console.WriteLine("Disposable E2E database initialized.");
Console.WriteLine(databaseBuilder.ConnectionString);

static async Task ExecuteAsync(ApplicationDbContext db, string sql)
{
    var connection = db.Database.GetDbConnection();
    if (connection.State != System.Data.ConnectionState.Open) await connection.OpenAsync();
    await using var command = connection.CreateCommand();
    command.CommandText = sql;
    await command.ExecuteNonQueryAsync();
}
