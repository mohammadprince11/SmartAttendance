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
using SmartAttendance.Web.Infrastructure.Integrations;
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
            await HrmsDatabase.EnsureCreatedAsync(db);
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
            db, scopeA, _companyA, "Company A custom report", null, "employees", "no,name", "owner-a", false,
            groupColumnKey: "department", sortColumnKey: "name", sortDescending: true);
        await PeopleReportsStore.CreateAsync(
            db, scopeB, _companyB, "Company B custom report", null, "employees", "no,name", "owner-b", false);

        var aRows = await PeopleReportsStore.LoadAllAsync(db, scopeA);
        Assert.Contains(aRows, report => report.Name == "Company A custom report");
        var configured = Assert.Single(aRows, report => report.Name == "Company A custom report");
        Assert.Equal("department", configured.GroupColumnKey);
        Assert.Equal("name", configured.SortColumnKey);
        Assert.True(configured.SortDescending);
        Assert.DoesNotContain(aRows, report => report.Name == "Company B custom report");

        var bId = await ScalarAsync(db,
            $"SELECT Id FROM PeopleReports WHERE CompanyId={_companyB} AND Name=N'Company B custom report';");
        Assert.Null(await PeopleReportsStore.GetAsync(db, scopeA, bId));
        await PeopleReportsStore.DeleteOwnAsync(db, scopeA, bId, "owner-b");
        Assert.Equal(1, await RawIntAsync(db, $"SELECT COUNT(*) FROM PeopleReports WHERE Id={bId} AND IsDeleted=0;"));
    }

    [SkippableFact]
    public async Task Report_schedules_are_tenant_scoped_and_keep_delivery_idempotency()
    {
        RequireSql();
        await using var db = NewContext();
        var scopeA = CompanyScope.ForCompanies(new[] { _companyA });
        var scopeB = CompanyScope.ForCompanies(new[] { _companyB });
        await PeopleReportsStore.EnsureSchemaAsync(db);
        await PeopleReportsStore.CreateAsync(db, scopeA, _companyA, "Scheduled A", null,
            "employees", "no,name", "schedule-owner", false);
        var reportId = await ScalarAsync(db,
            $"SELECT Id FROM PeopleReports WHERE CompanyId={_companyA} AND Name=N'Scheduled A';");
        var user = new SystemUser
        {
            FullName = "Schedule Owner", UserName = "schedule-owner", Email = "owner-a@example.com",
            EmployeeId = _employeeA, Role = SmartAttendance.Domain.Enums.SystemUserRole.HR, IsActive = true
        };
        db.SystemUsers.Add(user);
        await db.SaveChangesAsync();

        await ReportScheduleStore.CreateAsync(db, scopeA, _companyA, reportId, user.Id,
            user.UserName, user.Email!, "Daily", 6, null);
        var schedule = Assert.Single(await ReportScheduleStore.ListAsync(db, scopeA, user.UserName));
        Assert.Empty(await ReportScheduleStore.ListAsync(db, scopeB, user.UserName));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => ReportScheduleStore.CreateAsync(
            db, scopeB, _companyA, reportId, user.Id, user.UserName, user.Email!, "Daily", 6, null));

        var occurrence = new DateTime(2026, 8, 27, 6, 0, 0, DateTimeKind.Utc);
        await ReportScheduleStore.RecordDeliveryAsync(db, schedule.Id, occurrence, user.Email!);
        await ReportScheduleStore.RecordDeliveryAsync(db, schedule.Id, occurrence, user.Email!);
        Assert.Equal(1, await ReportScheduleStore.DeliveryExistsAsync(db, schedule.Id, occurrence, user.Email!));
        Assert.Equal(1, await RawIntAsync(db, $"SELECT COUNT(*) FROM ReportScheduleDeliveries WHERE ScheduleId={schedule.Id};"));
    }

    [SkippableFact]
    public async Task Dashboard_layouts_and_mutations_are_company_isolated()
    {
        RequireSql();
        await using var db = NewContext();
        var scopeA = CompanyScope.ForCompanies(new[] { _companyA });
        var scopeB = CompanyScope.ForCompanies(new[] { _companyB });
        var aRows = await DashboardWidgetStore.ListAsync(db, scopeA, _companyA);
        var bRows = await DashboardWidgetStore.ListAsync(db, scopeB, _companyB);
        Assert.NotEmpty(aRows); Assert.NotEmpty(bRows);
        Assert.All(aRows, row => Assert.Equal(_companyA, row.CompanyId));
        Assert.All(bRows, row => Assert.Equal(_companyB, row.CompanyId));

        await DashboardWidgetStore.AddAsync(db, scopeA, new DashboardWidgetStore.Widget
        { CompanyId = _companyA, Title = "Only A", Metric = "ActiveEmployees", ChartKind = "Number" });
        var customA = Assert.Single(await DashboardWidgetStore.ListAsync(db, scopeA, _companyA), row => row.Title == "Only A");
        await DashboardWidgetStore.DeleteAsync(db, scopeB, _companyB, customA.Id);
        Assert.Contains(await DashboardWidgetStore.ListAsync(db, scopeA, _companyA), row => row.Id == customA.Id);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            DashboardWidgetStore.DeleteAsync(db, scopeB, _companyA, customA.Id));
    }

    [SkippableFact]
    public async Task Approval_templates_reject_cross_company_read_update_delete()
    {
        RequireSql();
        await using var db = NewContext();
        var scopeA = CompanyScope.ForCompanies(new[] { _companyA });
        var scopeB = CompanyScope.ForCompanies(new[] { _companyB });
        var template = new ApprovalTemplateStore.TemplateRow
        {
            CompanyId=_companyA,RequestType="LeaveRequest",Name="Company A leave",IsActive=true,
            Steps=new() { new() { ApproverType="Role",RoleName="HR Manager",DisplayName="HR Manager" } }
        };
        var id = await ApprovalTemplateStore.SaveAsync(db,scopeA,template);
        Assert.Single(await ApprovalTemplateStore.ListAsync(db,_companyA,"LeaveRequest"), item=>item.Id==id);
        Assert.Empty(await ApprovalTemplateStore.ListAsync(db,_companyB,"LeaveRequest"));
        Assert.Null(await ApprovalTemplateStore.GetAsync(db,scopeB,_companyB,id));
        template.Id=id; template.Name="Cross-company overwrite";
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>ApprovalTemplateStore.SaveAsync(db,scopeB,template));
        await ApprovalTemplateStore.DeleteAsync(db,scopeB,_companyB,id);
        Assert.NotNull(await ApprovalTemplateStore.GetAsync(db,scopeA,_companyA,id));
    }

    [SkippableFact]
    public async Task Payroll_profiles_reject_cross_company_reads_updates_and_deletes()
    {
        RequireSql();
        await using var db = NewContext();
        var scopeA = CompanyScope.ForCompanies(new[] { _companyA });
        var scopeB = CompanyScope.ForCompanies(new[] { _companyB });
        await PayrollConfigStore.EnsureAsync(db);

        var aId = await PayrollConfigStore.SaveTaxProfileAsync(db, scopeA, new PayrollConfigStore.TaxProfile
        {
            CompanyId = _companyA, Name = "Company A tax", ExemptionAmount = 10, IsActive = true,
            Brackets = new() { new() { FromAmount = 0, Rate = 5 } }
        });
        var bProfile = new PayrollConfigStore.TaxProfile
        {
            CompanyId = _companyB, Name = "Company B tax", ExemptionAmount = 20, IsActive = true,
            Brackets = new() { new() { FromAmount = 0, Rate = 7 } }
        };
        var bId = await PayrollConfigStore.SaveTaxProfileAsync(db, scopeB, bProfile);

        var aRows = await PayrollConfigStore.ListTaxProfilesAsync(db, scopeA, _companyA);
        Assert.Contains(aRows, profile => profile.Id == aId);
        Assert.DoesNotContain(aRows, profile => profile.Id == bId);

        bProfile.Id = bId;
        bProfile.CompanyId = _companyA;
        bProfile.Name = "Malicious rename";
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            PayrollConfigStore.SaveTaxProfileAsync(db, scopeA, bProfile));
        Assert.Equal("Company B tax", Assert.Single(await RawStringsAsync(
            db, $"SELECT Name FROM PayrollTaxProfiles WHERE Id={bId};")));

        await PayrollConfigStore.DeleteTaxProfileAsync(db, scopeA, bId);
        Assert.Equal(1, await RawIntAsync(db, $"SELECT COUNT(*) FROM PayrollTaxProfiles WHERE Id={bId};"));
        Assert.Equal(1, await RawIntAsync(db, $"SELECT COUNT(*) FROM PayrollTaxBrackets WHERE ProfileId={bId};"));
    }

    [SkippableFact]
    public async Task Attendance_sources_reject_cross_company_reads_updates_and_deletes()
    {
        RequireSql();
        await using var db = NewContext();
        var scopeA = CompanyScope.ForCompanies(new[] { _companyA });
        var scopeB = CompanyScope.ForCompanies(new[] { _companyB });
        await AttendanceSourceStore.EnsureAsync(db);

        var sourceA = new AttendanceSourceStore.AttendanceSource
        {
            CompanyId = _companyA, Name = "Company A import", ReadType = "Excel", IsActive = true
        };
        var sourceB = new AttendanceSourceStore.AttendanceSource
        {
            CompanyId = _companyB, Name = "Company B import", ReadType = "Excel", IsActive = true
        };
        await AttendanceSourceStore.SaveAsync(db, scopeA, sourceA);
        await AttendanceSourceStore.SaveAsync(db, scopeB, sourceB);

        var aRows = await AttendanceSourceStore.ListAsync(db, scopeA, _companyA);
        Assert.Contains(aRows, source => source.Name == "Company A import");
        Assert.DoesNotContain(aRows, source => source.Name == "Company B import");

        var bId = await ScalarAsync(db,
            $"SELECT Id FROM AttendanceSources WHERE CompanyId={_companyB} AND Name=N'Company B import';");
        sourceB.Id = bId;
        sourceB.CompanyId = _companyA;
        sourceB.Name = "Malicious rename";
        await AttendanceSourceStore.SaveAsync(db, scopeA, sourceB);
        await AttendanceSourceStore.DeleteAsync(db, scopeA, _companyA, bId);
        Assert.Equal("Company B import", Assert.Single(await RawStringsAsync(
            db, $"SELECT Name FROM AttendanceSources WHERE Id={bId};")));
    }

    [SkippableFact]
    public async Task Webhook_outbox_is_company_scoped_and_idempotent()
    {
        RequireSql();
        await using var db = NewContext();
        var scopeA = CompanyScope.ForCompanies(new[] { _companyA });
        var scopeB = CompanyScope.ForCompanies(new[] { _companyB });

        await WebhookStore.SaveSubscriptionAsync(db, scopeA, _companyA, 0, "A ERP",
            new Uri("https://events-a.example.com/zynora"), "protected-a", "employee.updated", true);
        await WebhookStore.SaveSubscriptionAsync(db, scopeB, _companyB, 0, "B ERP",
            new Uri("https://events-b.example.com/zynora"), "protected-b", "employee.updated", true);

        var aRows = await WebhookStore.ListSubscriptionsAsync(db, scopeA, _companyA);
        Assert.Single(aRows, subscription => subscription.Name == "A ERP");
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            WebhookStore.ListSubscriptionsAsync(db, scopeA, _companyB));

        await WebhookStore.EnqueueAsync(db, _companyA, "employee.updated",
            new { employeeId = _employeeA }, "employee.updated:test-1");
        await WebhookStore.EnqueueAsync(db, _companyA, "employee.updated",
            new { employeeId = _employeeA }, "employee.updated:test-1");
        Assert.Equal(1, await RawIntAsync(db,
            $"SELECT COUNT(*) FROM WebhookDeliveries WHERE CompanyId={_companyA} AND IdempotencyKey=N'employee.updated:test-1';"));
        Assert.Equal(0, await RawIntAsync(db,
            $"SELECT COUNT(*) FROM WebhookDeliveries WHERE CompanyId={_companyB} AND IdempotencyKey=N'employee.updated:test-1';"));

        var claimed = await WebhookStore.ClaimAsync(db, 10, 8, CancellationToken.None);
        var delivery = Assert.Single(claimed, item => item.CompanyId == _companyA);
        Assert.Equal(1, delivery.AttemptCount);
        await WebhookStore.MarkSentAsync(db, delivery.Id, 204);
        Assert.Equal("Sent", Assert.Single(await RawStringsAsync(
            db, $"SELECT Status FROM WebhookDeliveries WHERE Id={delivery.Id};")));
    }

    [SkippableFact]
    public async Task Device_connector_key_inbox_processor_and_dead_letter_are_tenant_safe()
    {
        RequireSql();
        await using var db = NewContext();
        var scopeA = CompanyScope.ForCompanies(new[] { _companyA });
        var scopeB = CompanyScope.ForCompanies(new[] { _companyB });
        var token = await IntegrationApiKeyStore.IssueAsync(
            db, scopeA, _companyA, "SQL device A", "attendance.write");
        var identity = await IntegrationApiKeyStore.ValidateAsync(db, token, "attendance.write");
        Assert.NotNull(identity);
        Assert.Equal(_companyA, identity!.CompanyId);
        Assert.Null(await IntegrationApiKeyStore.ValidateAsync(db, token, "payroll.write"));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            IntegrationApiKeyStore.ListAsync(db, scopeB, _companyA));

        var employeeNo = Assert.Single(await RawStringsAsync(
            db, $"SELECT EmployeeNo FROM Employees WHERE Id={_employeeA};"));
        var punchAt = DateTimeOffset.UtcNow.AddMinutes(-1);
        var batch = new[]
        {
            new DevicePunchInboxStore.Punch("sql-device-a-1", employeeNo, punchAt, "In", "gate-a")
        };
        var first = await DevicePunchInboxStore.IngestAsync(db, identity, "sql-gateway-a", batch);
        var duplicate = await DevicePunchInboxStore.IngestAsync(db, identity, "sql-gateway-a", batch);
        Assert.Equal(1, first.Accepted);
        Assert.Equal(1, duplicate.Duplicate);
        Assert.Equal(1, await RawIntAsync(db,
            $"SELECT COUNT(*) FROM DevicePunchInbox WHERE CompanyId={_companyA} AND ExternalId=N'sql-device-a-1';"));

        var processed = await DevicePunchProcessorService.ProcessBatchAsync(db, CancellationToken.None);
        Assert.Equal(1, processed.Processed);
        Assert.Equal(1, await RawIntAsync(db, $"""
SELECT COUNT(*) FROM AttendanceRecords r
INNER JOIN Employees e ON e.Id=r.EmployeeId
WHERE e.CompanyId={_companyA} AND r.Notes LIKE N'%sql-device-a-1%';
"""));

        await DevicePunchInboxStore.IngestAsync(db, identity, "sql-gateway-a",
            new[] { new DevicePunchInboxStore.Punch("sql-device-missing", "NO-SUCH-EMPLOYEE", punchAt, "Out", "gate-a") });
        for (var attempt = 0; attempt < 5; attempt++)
        {
            await ExecuteAsync(db, """
UPDATE DevicePunchInbox SET NextAttemptAt=DATEADD(minute,-1,SYSUTCDATETIME())
WHERE ExternalId=N'sql-device-missing';
""");
            await DevicePunchProcessorService.ProcessBatchAsync(db, CancellationToken.None);
        }
        Assert.Equal("DeadLetter", Assert.Single(await RawStringsAsync(db,
            "SELECT Status FROM DevicePunchInbox WHERE ExternalId=N'sql-device-missing';")));
    }

    [SkippableFact]
    public async Task Accounting_mappings_are_isolated_by_company()
    {
        RequireSql();
        await using var db = NewContext();
        var scopeA = CompanyScope.ForCompanies(new[] { _companyA });
        var scopeB = CompanyScope.ForCompanies(new[] { _companyB });
        await AccountingMappingStore.SaveAsync(
            db, scopeA, _companyA, AccountingJournalAdapter.PayrollExpense, "A-5100", "A payroll");
        await AccountingMappingStore.SaveAsync(
            db, scopeB, _companyB, AccountingJournalAdapter.PayrollExpense, "B-5100", "B payroll");

        var aRows = await AccountingMappingStore.ListAsync(db, scopeA, _companyA);
        Assert.Single(aRows, mapping => mapping.AccountCode == "A-5100");
        Assert.DoesNotContain(aRows, mapping => mapping.AccountCode == "B-5100");
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            AccountingMappingStore.ListAsync(db, scopeA, _companyB));
    }

    [SkippableFact]
    public async Task Payroll_settings_are_company_specific_with_legacy_fallback()
    {
        RequireSql();
        await using var db = NewContext();
        const string key = "Payroll.Acceptance.Isolation";
        await SmartAttendance.Web.Infrastructure.HrSettings.HrSettingsStore.SetAsync(db, key, "legacy");
        await SmartAttendance.Web.Infrastructure.HrSettings.HrSettingsStore.SetCompanyAsync(db, _companyA, key, "A");
        await SmartAttendance.Web.Infrastructure.HrSettings.HrSettingsStore.SetCompanyAsync(db, _companyB, key, "B");

        Assert.Equal("A", await SmartAttendance.Web.Infrastructure.HrSettings.HrSettingsStore.GetCompanyAsync(db, _companyA, key));
        Assert.Equal("B", await SmartAttendance.Web.Infrastructure.HrSettings.HrSettingsStore.GetCompanyAsync(db, _companyB, key));
        Assert.Equal("legacy", await SmartAttendance.Web.Infrastructure.HrSettings.HrSettingsStore.GetCompanyAsync(db, 999_999, key));
    }

    [SkippableFact]
    public async Task Allowance_identity_audit_is_unambiguous_and_fk_backed()
    {
        RequireSql();
        await using var db = NewContext();
        var salaryItemId = await ScalarAsync(db, $"""
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

    [SkippableFact]
    public async Task Approval_return_and_resubmit_are_tenant_safe_and_keep_the_original_snapshot()
    {
        RequireSql();
        await using var db = NewContext();
        await HrmsDatabase.EnsureCreatedAsync(db);
        var requestId = await ScalarAsync(db, $"""
INSERT INTO SelfServiceRequests(EmployeeId,RequestType,FromDate,ToDate,Reason,Status,CreatedBy)
VALUES({_employeeA},N'إجازة','2099-05-01','2099-05-02',N'السبب الأصلي','Pending',N'employee-a');
SELECT CAST(SCOPE_IDENTITY() AS int);
""");
        await ApprovalWorkflowEngine.StartAsync(db, requestId, "إجازة", _employeeA);
        var original = Assert.IsType<ApprovalWorkflowEngine.FlowState>(
            await ApprovalWorkflowEngine.GetFlowAsync(db, requestId));
        var originalTemplate = original.TemplateName;
        var originalSteps = original.Steps.Select(x => (x.StepOrder, x.ApproverType, x.RoleName, x.UserName, x.DisplayName)).ToArray();

        var missingNote = await ApprovalWorkflowEngine.ReturnForRevisionAsync(
            db, CompanyScope.ForCompanies(new[] { _companyA }), requestId, "hr-a", " ", new[] { "HR Manager" }, null);
        Assert.False(missingNote.Ok);
        var wrongTenant = await ApprovalWorkflowEngine.ReturnForRevisionAsync(
            db, CompanyScope.ForCompanies(new[] { _companyB }), requestId, "hr-b", "fix", new[] { "HR Manager" }, null);
        Assert.False(wrongTenant.Ok);
        var selfReturn = await ApprovalWorkflowEngine.ReturnForRevisionAsync(
            db, CompanyScope.ForCompanies(new[] { _companyA }), requestId, "employee-a", "fix", new[] { "Admin" }, _employeeA);
        Assert.False(selfReturn.Ok);

        var returned = await ApprovalWorkflowEngine.ReturnForRevisionAsync(
            db, CompanyScope.ForCompanies(new[] { _companyA }), requestId, "hr-a", "أكمل التفاصيل", new[] { "HR Manager" }, null);
        Assert.True(returned.Ok, returned.Message);
        Assert.Equal("Returned", await ScalarObjectAsync(db, $"SELECT Status FROM SelfServiceRequests WHERE Id={requestId};"));

        var wrongOwner = await ApprovalWorkflowEngine.ResubmitReturnedAsync(
            db, requestId, _employeeB, "تعديل غير مصرح", new DateTime(2099, 5, 3), new DateTime(2099, 5, 4));
        Assert.False(wrongOwner.Ok);
        var resubmitted = await ApprovalWorkflowEngine.ResubmitReturnedAsync(
            db, requestId, _employeeA, "السبب المعدل", new DateTime(2099, 5, 3), new DateTime(2099, 5, 4));
        Assert.True(resubmitted.Ok, resubmitted.Message);

        var current = Assert.IsType<ApprovalWorkflowEngine.FlowState>(
            await ApprovalWorkflowEngine.GetFlowAsync(db, requestId));
        Assert.Equal(originalTemplate, current.TemplateName);
        Assert.Equal(originalSteps, current.Steps.Select(x => (x.StepOrder, x.ApproverType, x.RoleName, x.UserName, x.DisplayName)).ToArray());
        Assert.NotNull(current.Current);
        Assert.Equal("Pending", await ScalarObjectAsync(db, $"SELECT Status FROM SelfServiceRequests WHERE Id={requestId};"));
        Assert.Equal(2, await RawIntAsync(db, $"SELECT COUNT(*) FROM ApprovalHistories WHERE RequestId={requestId} AND Action IN ('Returned','Resubmitted');"));
    }

    [SkippableFact]
    public async Task Approval_temporary_delegation_is_time_tenant_and_revocation_safe()
    {
        RequireSql();
        await using var db=NewContext();
        await HrmsDatabase.EnsureCreatedAsync(db);
        var employee=await db.Employees.SingleAsync(x=>x.Id==_employeeA);
        var manager=new Employee{EmployeeNo="DG-M-"+Guid.NewGuid().ToString("N")[..8],FullName="Delegating Manager",CompanyId=_companyA,BranchId=employee.BranchId,DepartmentId=employee.DepartmentId,HireDate=new DateOnly(2090,1,1),IsActive=true};
        var substitute=new Employee{EmployeeNo="DG-S-"+Guid.NewGuid().ToString("N")[..8],FullName="Temporary Delegate",CompanyId=_companyA,BranchId=employee.BranchId,DepartmentId=employee.DepartmentId,HireDate=new DateOnly(2090,1,1),IsActive=true};
        db.AddRange(manager,substitute); await db.SaveChangesAsync();
        employee.DirectManagerId=manager.Id;
        db.SystemUsers.AddRange(
            new SystemUser{FullName=manager.FullName,UserName="manager-a-"+manager.Id,Role=SmartAttendance.Domain.Enums.SystemUserRole.Viewer,IsActive=true,EmployeeId=manager.Id},
            new SystemUser{FullName=substitute.FullName,UserName="delegate-a-"+substitute.Id,Role=SmartAttendance.Domain.Enums.SystemUserRole.Viewer,IsActive=true,EmployeeId=substitute.Id});
        await db.SaveChangesAsync();
        var managerUser="manager-a-"+manager.Id; var delegateUser="delegate-a-"+substitute.Id;
        var scopeA=CompanyScope.ForCompanies(new[]{_companyA});

        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>ApprovalDelegationStore.CreateAsync(
            db,CompanyScope.ForCompanies(new[]{_companyB}),_companyA,managerUser,delegateUser,DateTime.UtcNow.AddMinutes(-1),DateTime.UtcNow.AddDays(2),"sql-test"));
        var delegation=await ApprovalDelegationStore.CreateAsync(db,scopeA,_companyA,managerUser,delegateUser,
            DateTime.UtcNow.AddMinutes(-1),DateTime.UtcNow.AddDays(2),"sql-test");
        Assert.True(delegation.Ok,delegation.Message);

        var requestId=await ScalarAsync(db,$"""
INSERT INTO SelfServiceRequests(EmployeeId,RequestType,Reason,Status,CreatedBy)
VALUES({_employeeA},N'إجازة',N'اختبار التفويض','Pending',N'employee-a');
SELECT CAST(SCOPE_IDENTITY() AS int);
""");
        await ApprovalWorkflowEngine.StartAsync(db,requestId,"إجازة",_employeeA);
        var approved=await ApprovalWorkflowEngine.ApproveAsync(db,scopeA,requestId,delegateUser,"delegated",Array.Empty<string>(),substitute.Id);
        Assert.True(approved.Ok,approved.Message);
        Assert.Equal(delegateUser,await ScalarObjectAsync(db,$"SELECT ActionBy FROM ApprovalRequestSteps WHERE RequestId={requestId} AND StepOrder=1;"));
        Assert.Equal(managerUser,await ScalarObjectAsync(db,$"SELECT DelegatedFrom FROM ApprovalRequestSteps WHERE RequestId={requestId} AND StepOrder=1;"));
        Assert.Equal(managerUser,await ScalarObjectAsync(db,$"SELECT TOP(1) DelegatedFrom FROM ApprovalHistories WHERE RequestId={requestId} ORDER BY Id DESC;"));

        Assert.True(await ApprovalDelegationStore.RevokeAsync(db,scopeA,_companyA,delegation.Id,"sql-test"));
        var secondId=await ScalarAsync(db,$"""
INSERT INTO SelfServiceRequests(EmployeeId,RequestType,Reason,Status,CreatedBy)
VALUES({_employeeA},N'إجازة',N'بعد الإلغاء','Pending',N'employee-a');
SELECT CAST(SCOPE_IDENTITY() AS int);
""");
        await ApprovalWorkflowEngine.StartAsync(db,secondId,"إجازة",_employeeA);
        var denied=await ApprovalWorkflowEngine.ApproveAsync(db,scopeA,secondId,delegateUser,null,Array.Empty<string>(),substitute.Id);
        Assert.False(denied.Ok);
    }

    [SkippableFact]
    public async Task Approval_parallel_stage_waits_for_every_actor_then_advances_once()
    {
        RequireSql();
        await using var db=NewContext();
        await HrmsDatabase.EnsureCreatedAsync(db);
        var employee=await db.Employees.AsNoTracking().SingleAsync(x=>x.Id==_employeeA);
        var actors=Enumerable.Range(1,3).Select(index=>new Employee
        {
            EmployeeNo=$"PAR-{index}-"+Guid.NewGuid().ToString("N")[..6],FullName=$"Parallel Actor {index}",
            CompanyId=_companyA,BranchId=employee.BranchId,DepartmentId=employee.DepartmentId,
            HireDate=new DateOnly(2090,1,1),IsActive=true
        }).ToArray();
        db.AddRange(actors); await db.SaveChangesAsync();
        var users=actors.Select((actor,index)=>new SystemUser
        {
            FullName=actor.FullName,UserName=$"parallel-{index+1}-{actor.Id}",Role=SmartAttendance.Domain.Enums.SystemUserRole.Viewer,
            IsActive=true,EmployeeId=actor.Id
        }).ToArray();
        db.SystemUsers.AddRange(users); await db.SaveChangesAsync();
        var scope=CompanyScope.ForCompanies(new[]{_companyA});
        await ApprovalTemplateStore.SaveAsync(db,scope,new ApprovalTemplateStore.TemplateRow
        {
            CompanyId=_companyA,RequestType="CustomRequest",Name="SQL parallel "+Guid.NewGuid().ToString("N"),IsActive=true,
            Steps=
            {
                new(){StageOrder=1,ApproverType="User",UserName=users[0].UserName,DisplayName="Parallel A"},
                new(){StageOrder=1,ApproverType="User",UserName=users[1].UserName,DisplayName="Parallel B"},
                new(){StageOrder=2,ApproverType="User",UserName=users[2].UserName,DisplayName="Final C"}
            }
        });
        var requestId=await ScalarAsync(db,$"""
INSERT INTO SelfServiceRequests(EmployeeId,RequestType,Reason,Status,CreatedBy)
VALUES({_employeeA},N'CustomRequest',N'مرحلة متوازية','Pending',N'employee-a');
SELECT CAST(SCOPE_IDENTITY() AS int);
""");
        await ApprovalWorkflowEngine.StartAsync(db,requestId,"CustomRequest",_employeeA);
        var initial=Assert.IsType<ApprovalWorkflowEngine.FlowState>(await ApprovalWorkflowEngine.GetFlowAsync(db,requestId));
        Assert.Equal(2,initial.CurrentSteps.Count);
        Assert.All(initial.CurrentSteps,step=>Assert.Equal(1,step.StageOrder));

        var first=await ApprovalWorkflowEngine.ApproveAsync(db,scope,requestId,users[0].UserName,null,Array.Empty<string>(),actors[0].Id);
        Assert.True(first.Ok,first.Message); Assert.False(first.FinalApproved);
        var earlyFinal=await ApprovalWorkflowEngine.ApproveAsync(db,scope,requestId,users[2].UserName,null,Array.Empty<string>(),actors[2].Id);
        Assert.False(earlyFinal.Ok);
        var second=await ApprovalWorkflowEngine.ApproveAsync(db,scope,requestId,users[1].UserName,null,Array.Empty<string>(),actors[1].Id);
        Assert.True(second.Ok,second.Message); Assert.False(second.FinalApproved);
        var advanced=Assert.IsType<ApprovalWorkflowEngine.FlowState>(await ApprovalWorkflowEngine.GetFlowAsync(db,requestId));
        Assert.Single(advanced.CurrentSteps); Assert.Equal(2,advanced.CurrentSteps[0].StageOrder);
        var final=await ApprovalWorkflowEngine.ApproveAsync(db,scope,requestId,users[2].UserName,null,Array.Empty<string>(),actors[2].Id);
        Assert.True(final.Ok,final.Message); Assert.True(final.FinalApproved);
        Assert.Equal("Approved",await ScalarObjectAsync(db,$"SELECT Status FROM SelfServiceRequests WHERE Id={requestId};"));

        var raceId=await ScalarAsync(db,$"""
INSERT INTO SelfServiceRequests(EmployeeId,RequestType,Reason,Status,CreatedBy)
VALUES({_employeeA},N'CustomRequest',N'اختبار التزامن','Pending',N'employee-a');
SELECT CAST(SCOPE_IDENTITY() AS int);
""");
        await ApprovalWorkflowEngine.StartAsync(db,raceId,"CustomRequest",_employeeA);
        var decisions=await Task.WhenAll(Enumerable.Range(0,2).Select(async index=>
        {
            await using var concurrentDb=NewContext();
            return await ApprovalWorkflowEngine.ApproveAsync(concurrentDb,CompanyScope.ForCompanies(new[]{_companyA}),
                raceId,users[index].UserName,null,Array.Empty<string>(),actors[index].Id);
        }));
        Assert.All(decisions,decision=>Assert.True(decision.Ok,decision.Message));
        await using var verificationDb=NewContext();
        var raceFlow=Assert.IsType<ApprovalWorkflowEngine.FlowState>(await ApprovalWorkflowEngine.GetFlowAsync(verificationDb,raceId));
        Assert.Single(raceFlow.CurrentSteps); Assert.Equal(2,raceFlow.CurrentSteps[0].StageOrder);
        Assert.Equal(2,await RawIntAsync(verificationDb,$"SELECT COUNT(*) FROM ApprovalRequestSteps WHERE RequestId={raceId} AND StageOrder=1 AND Status='Approved';"));
    }

    [SkippableFact]
    public async Task Approval_sla_reminds_once_then_grants_the_configured_alternate()
    {
        RequireSql();
        await using var db=NewContext();
        await HrmsDatabase.EnsureCreatedAsync(db);
        var employee=await db.Employees.AsNoTracking().SingleAsync(x=>x.Id==_employeeA);
        var approvers=Enumerable.Range(1,2).Select(index=>new Employee
        {
            EmployeeNo=$"SLA-{index}-"+Guid.NewGuid().ToString("N")[..6],FullName=$"SLA User {index}",
            CompanyId=_companyA,BranchId=employee.BranchId,DepartmentId=employee.DepartmentId,
            HireDate=new DateOnly(2090,1,1),IsActive=true
        }).ToArray();
        db.AddRange(approvers); await db.SaveChangesAsync();
        var users=approvers.Select((approver,index)=>new SystemUser
        {
            FullName=approver.FullName,UserName=$"sla-{index+1}-{approver.Id}",Role=SmartAttendance.Domain.Enums.SystemUserRole.Viewer,
            IsActive=true,EmployeeId=approver.Id
        }).ToArray();
        db.SystemUsers.AddRange(users); await db.SaveChangesAsync();
        var scope=CompanyScope.ForCompanies(new[]{_companyA});
        await ApprovalTemplateStore.SaveAsync(db,scope,new ApprovalTemplateStore.TemplateRow
        {
            CompanyId=_companyA,RequestType="DocumentRequest",Name="SQL SLA "+Guid.NewGuid().ToString("N"),IsActive=true,
            ReminderHours=1,EscalationDays=1,EscalationTo="HR Manager",EscalationAlternateUser=users[1].UserName,
            Steps={new(){StageOrder=1,ApproverType="User",UserName=users[0].UserName,DisplayName="Document owner"}}
        });
        var requestId=await ScalarAsync(db,$"""
INSERT INTO SelfServiceRequests(EmployeeId,RequestType,Reason,Status,CreatedBy)
VALUES({_employeeA},N'DocumentRequest',N'اختبار SLA','Pending',N'employee-a');
SELECT CAST(SCOPE_IDENTITY() AS int);
""");
        await ApprovalWorkflowEngine.StartAsync(db,requestId,"DocumentRequest",_employeeA);
        await ExecuteAsync(db,$"UPDATE ApprovalRequestSteps SET CurrentSince=DATEADD(day,-2,SYSUTCDATETIME()) WHERE RequestId={requestId};");
        var processed=await ApprovalWorkflowEngine.ProcessSlaAsync(db);
        Assert.True(processed.Reminded>=1); Assert.True(processed.Escalated>=1);
        var secondPass=await ApprovalWorkflowEngine.ProcessSlaAsync(db);
        Assert.Equal(0,secondPass.Reminded); Assert.Equal(0,secondPass.Escalated);
        var flow=Assert.IsType<ApprovalWorkflowEngine.FlowState>(await ApprovalWorkflowEngine.GetFlowAsync(db,requestId));
        var step=Assert.Single(flow.CurrentSteps);
        Assert.NotNull(step.ReminderSentAt); Assert.NotNull(step.EscalatedAt); Assert.Equal(users[1].UserName,step.EscalatedToUser);
        var alternateDecision=await ApprovalWorkflowEngine.ApproveAsync(db,scope,requestId,users[1].UserName,"alternate",Array.Empty<string>(),approvers[1].Id);
        Assert.True(alternateDecision.Ok,alternateDecision.Message); Assert.True(alternateDecision.FinalApproved);
    }

    [SkippableFact]
    public async Task Approval_template_conditions_resolve_by_amount_and_changed_field()
    {
        RequireSql();
        await using var db=NewContext();
        await HrmsDatabase.EnsureCreatedAsync(db);
        var scope=CompanyScope.ForCompanies(new[]{_companyA});
        var amountName="High amount "+Guid.NewGuid().ToString("N");
        var fallbackName="Amount fallback "+Guid.NewGuid().ToString("N");
        await ApprovalTemplateStore.SaveAsync(db,scope,new ApprovalTemplateStore.TemplateRow
        {
            CompanyId=_companyA,RequestType="Loan",Name=amountName,IsActive=true,HasConditions=true,CondMinAmount=1000,
            Steps={new(){StageOrder=1,ApproverType="Role",RoleName="HR Manager",DisplayName="High value committee"}}
        });
        await ApprovalTemplateStore.SaveAsync(db,scope,new ApprovalTemplateStore.TemplateRow
        {
            CompanyId=_companyA,RequestType="Loan",Name=fallbackName,IsActive=true,
            Steps={new(){StageOrder=1,ApproverType="Role",RoleName="HR Manager",DisplayName="Normal committee"}}
        });
        var highRequest=await FinancialRequestStore.SubmitAsync(db,new FinancialRequestStore.Detail
        {Kind=FinancialRequestStore.Loan,Amount=1500,InstallmentCount=3,StartYear=2099,StartMonth=7,Reason="high"},_employeeA,"employee-a");
        var lowRequest=await FinancialRequestStore.SubmitAsync(db,new FinancialRequestStore.Detail
        {Kind=FinancialRequestStore.Loan,Amount=500,InstallmentCount=1,StartYear=2099,StartMonth=7,Reason="low"},_employeeA,"employee-a");
        Assert.Equal(amountName,(await ApprovalWorkflowEngine.GetFlowAsync(db,highRequest))!.TemplateName);
        Assert.Equal(fallbackName,(await ApprovalWorkflowEngine.GetFlowAsync(db,lowRequest))!.TemplateName);

        var fieldName="Email field "+Guid.NewGuid().ToString("N");
        var fieldFallback="Field fallback "+Guid.NewGuid().ToString("N");
        await ApprovalTemplateStore.SaveAsync(db,scope,new ApprovalTemplateStore.TemplateRow
        {
            CompanyId=_companyA,RequestType="InfoChange",Name=fieldName,IsActive=true,HasConditions=true,CondChangedFieldKey="Email",
            Steps={new(){StageOrder=1,ApproverType="Role",RoleName="HR Manager",DisplayName="Sensitive field committee"}}
        });
        await ApprovalTemplateStore.SaveAsync(db,scope,new ApprovalTemplateStore.TemplateRow
        {
            CompanyId=_companyA,RequestType="InfoChange",Name=fieldFallback,IsActive=true,
            Steps={new(){StageOrder=1,ApproverType="Role",RoleName="HR Manager",DisplayName="Normal field committee"}}
        });
        var fieldRequest=await ScalarAsync(db,$"""
INSERT INTO SelfServiceRequests(EmployeeId,RequestType,Reason,Status,CreatedBy)
VALUES({_employeeA},N'تعديل البيانات',N'بريد جديد','Pending',N'employee-a');
SELECT CAST(SCOPE_IDENTITY() AS int);
""");
        await DataChangeRequestStore.SaveFieldsAsync(db,fieldRequest,new[]{new DataChangeRequestStore.ProposedField{Key="Email",OldValue="old@example.test",NewValue="new@example.test"}});
        await ApprovalWorkflowEngine.StartAsync(db,fieldRequest,DataChangeRequestStore.RequestTypeLabel,_employeeA);
        Assert.Equal(fieldName,(await ApprovalWorkflowEngine.GetFlowAsync(db,fieldRequest))!.TemplateName);
    }

    [SkippableFact]
    public async Task Approval_visible_policies_enforce_attachment_cancellation_watchers_and_unknown_committee()
    {
        RequireSql();
        await using var db=NewContext();
        await HrmsDatabase.EnsureCreatedAsync(db);
        var employee=await db.Employees.AsNoTracking().SingleAsync(x=>x.Id==_employeeA);
        var watcherEmployee=new Employee
        {
            EmployeeNo="WATCH-"+Guid.NewGuid().ToString("N")[..8],FullName="Approval Watcher",CompanyId=_companyA,
            BranchId=employee.BranchId,DepartmentId=employee.DepartmentId,HireDate=new DateOnly(2090,1,1),IsActive=true
        };
        db.Employees.Add(watcherEmployee); await db.SaveChangesAsync();
        var watcher=new SystemUser
        {
            FullName=watcherEmployee.FullName,UserName="watch-"+watcherEmployee.Id,
            Role=SmartAttendance.Domain.Enums.SystemUserRole.Viewer,IsActive=true,EmployeeId=watcherEmployee.Id
        };
        db.SystemUsers.Add(watcher); await db.SaveChangesAsync();
        var scope=CompanyScope.ForCompanies(new[]{_companyA});
        await ApprovalTemplateStore.SaveAsync(db,scope,new ApprovalTemplateStore.TemplateRow
        {
            CompanyId=_companyA,RequestType="Resignation",Name="Policy SQL "+Guid.NewGuid().ToString("N"),IsActive=true,
            AttachmentRequiredOnRequest=true,CancelLimitDays=1,NotifyJson="{\"Employee\":[\"Submit\",\"Cancel\"],\"Committee\":[\"Submit\"]}",
            Watchers={new(){UserName=watcher.UserName}},
            Steps={new(){StageOrder=1,ApproverType="Role",RoleName="HR Manager",DisplayName="HR committee"}}
        });
        var requestId=await ScalarAsync(db,$"""
INSERT INTO SelfServiceRequests(EmployeeId,RequestType,Reason,Status,CreatedBy)
VALUES({_employeeA},N'Resignation',N'policy check','Pending',N'employee-a');
SELECT CAST(SCOPE_IDENTITY() AS int);
""");
        var missing=await ApprovalWorkflowEngine.StartAsync(db,requestId,"Resignation",_employeeA);
        Assert.False(missing.Ok); Assert.Contains("مرفق",missing.Message);
        Assert.Equal("Draft",await ScalarStringAsync(db,$"SELECT Status FROM SelfServiceRequests WHERE Id={requestId}"));

        await ExecuteAsync(db,$"UPDATE SelfServiceRequests SET AttachmentPath=N'protected/request/{requestId}',Status='Pending' WHERE Id={requestId};");
        var started=await ApprovalWorkflowEngine.StartAsync(db,requestId,"Resignation",_employeeA);
        Assert.True(started.Ok,started.Message);
        var flow=Assert.IsType<ApprovalWorkflowEngine.FlowState>(await ApprovalWorkflowEngine.GetFlowAsync(db,requestId));
        Assert.True(flow.AttachmentRequiredOnRequest); Assert.Equal(1,flow.CancelLimitDays);
        Assert.Equal(1,await ScalarAsync(db,$"SELECT COUNT(1) FROM ApprovalRequestWatchers WHERE RequestId={requestId} AND UserName=N'{watcher.UserName}'"));
        var wrongOwner=await ApprovalWorkflowEngine.CancelByRequesterAsync(db,requestId,_employeeB,"employee-b");
        Assert.False(wrongOwner.Ok);
        var cancelled=await ApprovalWorkflowEngine.CancelByRequesterAsync(db,requestId,_employeeA,"employee-a","changed mind");
        Assert.True(cancelled.Ok,cancelled.Message);
        Assert.Equal("Cancelled",await ScalarStringAsync(db,$"SELECT Status FROM SelfServiceRequests WHERE Id={requestId}"));
        Assert.True(await ScalarAsync(db,$"SELECT COUNT(1) FROM SystemNotifications WHERE TargetUser=N'{watcher.UserName}' AND Message LIKE N'%{requestId}%'")>=2);

        var expiredId=await ScalarAsync(db,$"""
INSERT INTO SelfServiceRequests(EmployeeId,RequestType,Reason,Status,CreatedBy,CreatedAt,AttachmentPath)
VALUES({_employeeA},N'Resignation',N'expired cancel','Pending',N'employee-a',DATEADD(day,-2,SYSUTCDATETIME()),N'protected/request/expired');
SELECT CAST(SCOPE_IDENTITY() AS int);
""");
        Assert.True((await ApprovalWorkflowEngine.StartAsync(db,expiredId,"Resignation",_employeeA)).Ok);
        Assert.False((await ApprovalWorkflowEngine.CancelByRequesterAsync(db,expiredId,_employeeA,"employee-a")).Ok);

        await ApprovalTemplateStore.SaveAsync(db,scope,new ApprovalTemplateStore.TemplateRow
        {
            CompanyId=_companyA,RequestType="Transfer",Name="Unknown committee "+Guid.NewGuid().ToString("N"),IsActive=true,
            AutoRejectUnknownCommittee=true,
            Steps={new(){StageOrder=1,ApproverType="DirectManager",DisplayName="Missing direct manager"}}
        });
        await ExecuteAsync(db,$"UPDATE Employees SET DirectManagerId=NULL WHERE Id={_employeeA};");
        var unknownId=await ScalarAsync(db,$"""
INSERT INTO SelfServiceRequests(EmployeeId,RequestType,Reason,Status,CreatedBy)
VALUES({_employeeA},N'Transfer',N'unknown committee','Pending',N'employee-a');
SELECT CAST(SCOPE_IDENTITY() AS int);
""");
        var autoRejected=await ApprovalWorkflowEngine.StartAsync(db,unknownId,"Transfer",_employeeA);
        Assert.False(autoRejected.Ok); Assert.True(autoRejected.Rejected);
        Assert.Equal("Rejected",await ScalarStringAsync(db,$"SELECT Status FROM SelfServiceRequests WHERE Id={unknownId}"));
    }

    [SkippableFact]
    public async Task Payroll_calculation_refuses_unapproved_month_attendance_before_writing_lines()
    {
        RequireSql();
        await using var db = NewContext();
        var scope = CompanyScope.ForCompanies(new[] { _companyA });

        var (created, _, runId) = await PayrollRunStore.CreateRunAsync(
            db,
            scope,
            _companyA,
            2026,
            7,
            PayrollRunScope.ModeAll,
            Array.Empty<int>());
        Assert.True(created);
        Assert.True(runId > 0);

        var (calculated, message) = await PayrollRunStore.CalculateAsync(
            db, runId, "payroll-operator-a");

        Assert.False(calculated);
        Assert.Contains("اعتماد حضور شهري", message);
        Assert.Equal(0, await RawIntAsync(
            db, $"SELECT COUNT(*) FROM PayrollRunLines WHERE RunId={runId};"));
        Assert.Equal("Draft", await ScalarStringAsync(
            db, $"SELECT Status FROM PayrollRuns WHERE Id={runId};"));
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

    private static async Task<string> ScalarStringAsync(ApplicationDbContext db, string sql) =>
        Convert.ToString(await ScalarObjectAsync(db, sql)) ?? string.Empty;

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
