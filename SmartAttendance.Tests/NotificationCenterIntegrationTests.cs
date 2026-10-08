using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartAttendance.Domain.Entities;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;
using SmartAttendance.Web.Infrastructure.HrSettings;
using SmartAttendance.Web.Infrastructure.Notifications;
using SmartAttendance.Web.Infrastructure.Security;
using SmartAttendance.Web.Pages.HrSettings;

namespace SmartAttendance.Tests;

[Collection(ProductionClosureSqlCollection.Name)]
public sealed class NotificationCenterIntegrationTests
{
    [SkippableFact]
    public async Task Company_configuration_generation_and_mail_are_scoped_durable_and_idempotent()
    {
        var configured = Environment.GetEnvironmentVariable("SMARTATTENDANCE_SQL_TEST_MASTER");
        Skip.If(string.IsNullOrWhiteSpace(configured), "Explicit local SQL test connection is required.");
        var builder = new SqlConnectionStringBuilder(configured);
        builder.Pooling = false;
        Assert.True(builder.DataSource.StartsWith("(localdb)\\", StringComparison.OrdinalIgnoreCase), "Only LocalDB is permitted for this disposable test.");
        builder.InitialCatalog = "master";
        var database = "SmartAttendance_E2E_Notifications_" + Guid.NewGuid().ToString("N");
        Assert.Matches("^SmartAttendance_E2E_Notifications_[a-f0-9]{32}$", database);
        await using var master = new SqlConnection(builder.ConnectionString);
        await master.OpenAsync();
        var created = false;
        try
        {
            await using (var command = master.CreateCommand())
            {
                command.CommandText = $"CREATE DATABASE [{database}];";
                await command.ExecuteNonQueryAsync(); created = true;
            }
            builder.InitialCatalog = database;
            await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(builder.ConnectionString).Options);
            // Generate the real entity schema in an empty, test-owned database only.
            await Sql("CREATE TABLE HrJobPositions(Id int IDENTITY PRIMARY KEY, CompanyId int, ArabicName nvarchar(400), EnglishName nvarchar(400), DepartmentId int, IsActive bit);");
            foreach (var batch in Regex.Split(db.Database.GenerateCreateScript(), @"^\s*GO\s*;?\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase))
            if (!string.IsNullOrWhiteSpace(batch)) await Sql(batch);
            var preMigrationSender = new CapturingEmailSender();
            await NotificationRuleMailOutbox.DispatchAsync(db, preMigrationSender, 100, default);
            Assert.Empty(preMigrationSender.Messages);
            await Sql(SqlSchemaMigrator.Migrations.Single(m => m.Sql.Contains("PK_ZynoraHrSettings", StringComparison.Ordinal)).Sql);
            await Sql("""
CREATE TABLE TemporaryUnitHeads(Id int PRIMARY KEY, DepartmentId int, HeadEmployeeId int, FromDate date, ToDate date, IsActive bit);
CREATE TABLE HrPolicyOverrides(Id int PRIMARY KEY, PolicyKey nvarchar(80), Name nvarchar(100), SortOrder int, IsActive bit, ConditionsJson nvarchar(max), PayloadJson nvarchar(max));
IF OBJECT_ID('EmployeeFinancialInfos', 'U') IS NULL CREATE TABLE EmployeeFinancialInfos(Id int PRIMARY KEY, EmployeeId int, RetirementAge int);
""");
            await Sql(NotificationRuleMailOutbox.MigrationSql);
            var tenantA = new Tenant { Code = "9001", Name = "Synthetic tenant A" };
            var tenantB = new Tenant { Code = "9002", Name = "Synthetic tenant B" };
            var companyA = new Company { Code = "SYN-A", Name = "Synthetic company A", Tenant = tenantA };
            var companyB = new Company { Code = "SYN-B", Name = "Synthetic company B", Tenant = tenantB };
            var branchA = new Branch { Code = "BA", Name = "Synthetic branch A", Company = companyA };
            var branchB = new Branch { Code = "BB", Name = "Synthetic branch B", Company = companyB };
            var departmentA = new Department { Code = "DA", Name = "Synthetic department A", Company = companyA, Branch = branchA };
            var departmentB = new Department { Code = "DB", Name = "Synthetic department B", Company = companyB, Branch = branchB };
            db.Departments.AddRange(departmentA, departmentB);
            await db.SaveChangesAsync();
            var today = new DateOnly(2026, 10, 7);
            var employeeA = new Employee { EmployeeNo = "SYN-A-1", FullName = "Synthetic employee A", CompanyId = companyA.Id,
                Branch = branchA, Department = departmentA, BirthDate = new(1990, 10, 7), HireDate = new(2020, 1, 1), Email = "synthetic-a@example.invalid", IsActive = true };
            var employeeB = new Employee { EmployeeNo = "SYN-B-1", FullName = "Synthetic employee B", CompanyId = companyB.Id,
                Branch = branchB, Department = departmentB, BirthDate = new(1990, 10, 7), HireDate = new(2020, 1, 1), Email = "synthetic-b@example.invalid", IsActive = true };
            db.Employees.AddRange(employeeA, employeeB);
            await db.SaveChangesAsync();
            await HrSettingsStore.EnsureTablesAsync(db);
            await Sql("UPDATE ZynoraNotificationRules SET IsEnabled = 0;");
            var birthday = (await HrSettingsStore.LoadNotificationRulesAsync(db)).Single(r => r.Name == "عيد ميلاد موظف");
            var settings = new NotificationRuleSettings { IsEnabled = true, Audience = NotificationRoutingPolicy.Self,
                Email = true, TitleTemplate = "تهنئة {{EmployeeName}}", BodyTemplate = "{{RuleName}}: {{Message}}" };
            await NotificationRuleSettings.SaveAsync(db, companyA.Id, birthday.Id, settings);
            await NotificationRuleSettings.SaveAsync(db, companyB.Id, birthday.Id, settings with { IsEnabled = false });
            Assert.True((await NotificationRuleSettings.LoadCompanyAsync(db, companyA.Id))[birthday.Id].IsEnabled);
            Assert.False((await NotificationRuleSettings.LoadCompanyAsync(db, companyB.Id))[birthday.Id].IsEnabled);

            var http = new DefaultHttpContext(); http.Request.Headers.Accept = "application/json";
            var model = new NotificationCenterModel(db, new FixedScope(companyA.Id), new CapturingEmailSender())
                { PageContext = new PageContext { HttpContext = http } };
            Assert.IsType<ForbidResult>(await model.OnPostToggleRuleAsync(birthday.Id, true, companyB.Id));
            Assert.IsType<ForbidResult>(await model.OnGetSearchEmployeesAsync(companyB.Id, "Synthetic"));
            Assert.IsType<BadRequestObjectResult>(await model.OnPostUpdateRuleAsync(birthday.Id, NotificationRoutingPolicy.Group, 0,
                "كل الموظفين", null, companyA.Id, groupMembers: [employeeB.Id]));
            Assert.IsType<JsonResult>(await model.OnPostUpdateRuleAsync(birthday.Id, NotificationRoutingPolicy.Self, 0,
                "كل الموظفين", null, companyA.Id, email: true, titleTemplate: settings.TitleTemplate, bodyTemplate: settings.BodyTemplate));

            var scope = CompanyScope.ForCompanies([companyA.Id]);
            var first = await NotificationRuleGenerator.GenerateAsync(db, new NoOpWebPushSender(), today, companyScope: scope);
            Assert.Equal(1, first.NewEvents);
            var again = await NotificationRuleGenerator.GenerateAsync(db, new NoOpWebPushSender(), today, companyScope: scope);
            Assert.Equal(0, again.NewEvents);
            var recipients = await db.Set<UserNotificationRecipient>().Select(r => r.EmployeeId).ToListAsync();
            Assert.Equal([employeeA.Id], recipients);
            var notification = await db.Set<UserNotification>().SingleAsync();
            Assert.Equal("تهنئة Synthetic employee A", notification.TitleAr);
            Assert.DoesNotContain("Synthetic employee B", notification.MessageAr);
            Assert.Equal(1, await HrmsDatabase.ScalarAsync<int>(db, "SELECT COUNT(*) FROM ZynoraNotificationMailOutbox WHERE Status = N'Pending';", _ => { }));
            var sender = new CapturingEmailSender();
            await NotificationRuleMailOutbox.DispatchAsync(db, sender, 100, default);
            await NotificationRuleMailOutbox.DispatchAsync(db, sender, 100, default);
            Assert.Single(sender.Messages);
            Assert.Equal("synthetic-a@example.invalid", sender.Messages[0].ToAddress);
            Assert.Equal(1, await HrmsDatabase.ScalarAsync<int>(db, "SELECT COUNT(*) FROM ZynoraNotificationMailOutbox WHERE Status = N'Sent';", _ => { }));
            await NotificationRuleMailOutbox.EnqueueAsync(db, "synthetic-failure", companyA.Id, employeeA.Id, "Synthetic subject", "Synthetic body");
            var failingSender = new CapturingEmailSender { Fail = true };
            await NotificationRuleMailOutbox.DispatchAsync(db, failingSender, 100, default);
            await NotificationRuleMailOutbox.DispatchAsync(db, failingSender, 100, default);
            Assert.Single(failingSender.Messages); // An SMTP failure is not blindly retried.
            Assert.Equal(1, await HrmsDatabase.ScalarAsync<int>(db, "SELECT COUNT(*) FROM ZynoraNotificationMailOutbox WHERE Status = N'Failed';", _ => { }));
            await NotificationRuleMailOutbox.EnqueueAsync(db, "synthetic-no-address", companyA.Id, employeeA.Id, "Synthetic subject", "Synthetic body");
            employeeA.Email = ""; employeeA.PersonalEmail = "";
            await db.SaveChangesAsync();
            await NotificationRuleMailOutbox.DispatchAsync(db, sender, 100, default);
            Assert.Single(sender.Messages);
            Assert.Equal(1, await HrmsDatabase.ScalarAsync<int>(db, "SELECT COUNT(*) FROM ZynoraNotificationMailOutbox WHERE Status = N'NoAddress';", _ => { }));
            await NotificationRuleMailOutbox.EnqueueAsync(db, "synthetic-excluded", companyA.Id, employeeA.Id, "Synthetic subject", "Synthetic body");
            employeeA.IsActive = false; await db.SaveChangesAsync();
            await NotificationRuleMailOutbox.DispatchAsync(db, sender, 100, default);
            Assert.Single(sender.Messages);
            Assert.Equal(1, await HrmsDatabase.ScalarAsync<int>(db, "SELECT COUNT(*) FROM ZynoraNotificationMailOutbox WHERE Status = N'Excluded';", _ => { }));
            await Sql("""
CREATE TABLE FormSubmissions(Id int IDENTITY PRIMARY KEY, TemplateId int, EmployeeId int, FormType nvarchar(30), Status nvarchar(20), SubmittedAt datetime2 DEFAULT SYSUTCDATETIME());
CREATE TABLE SystemNotifications(Id int IDENTITY PRIMARY KEY, Title nvarchar(200), Message nvarchar(4000), TargetRole nvarchar(30), TargetUser nvarchar(150), Url nvarchar(500));
""");
            var supervisor = new Employee { EmployeeNo = "SYN-A-2", FullName = "Synthetic supervisor", CompanyId = companyA.Id,
                Branch = branchA, Department = departmentA, HireDate = new(2020, 1, 1), IsActive = true };
            db.Employees.Add(supervisor); await db.SaveChangesAsync();
            db.Set<SystemUser>().Add(new() { TenantId = tenantA.Id, EmployeeId = supervisor.Id, FullName = "Synthetic supervisor",
                UserName = "synthetic-supervisor-a", Role = SmartAttendance.Domain.Enums.SystemUserRole.HR });
            await db.SaveChangesAsync();
            var exitRule = (await HrSettingsStore.LoadNotificationRulesAsync(db)).Single(r => r.Name == "مقابلات نهاية الخدمة");
            Assert.IsType<BadRequestObjectResult>(await model.OnPostUpdateRuleAsync(exitRule.Id, NotificationRoutingPolicy.Self, 0,
                "كل الموظفين", null, companyA.Id));
            await NotificationRuleSettings.SaveAsync(db, companyA.Id, exitRule.Id, new()
                { IsEnabled = true, Audience = NotificationRoutingPolicy.Supervisors, EnabledSinceUtc = DateTime.UtcNow.AddMinutes(-5) });
            await Sql($"INSERT FormSubmissions(TemplateId,EmployeeId,FormType,Status) VALUES (1,{employeeA.Id},N'ExitInterview',N'Submitted'), (1,{employeeB.Id},N'ExitInterview',N'Submitted');");
            var exitResult = await NotificationRuleGenerator.GenerateAsync(db, new NoOpWebPushSender(), today, companyScope: scope);
            Assert.Equal(1, exitResult.NewEvents);
            Assert.Equal(0, (await NotificationRuleGenerator.GenerateAsync(db, new NoOpWebPushSender(), today, companyScope: scope)).NewEvents);
            var exitNotification = await db.Set<UserNotification>().Include(n => n.Recipients).SingleAsync(n => n.TitleAr == "مقابلات نهاية الخدمة");
            Assert.Equal([supervisor.Id], exitNotification.Recipients.Select(r => r.EmployeeId));
            Assert.DoesNotContain(employeeA.Id, exitNotification.Recipients.Select(r => r.EmployeeId)); // Departed subject is not a recipient.
            Assert.Equal(1, await HrmsDatabase.ScalarAsync<int>(db, "SELECT COUNT(*) FROM SystemNotifications WHERE TargetUser = N'synthetic-supervisor-a';", _ => { }));
            await Sql("CREATE TABLE EmployeePolls(Id int PRIMARY KEY, CompanyId int NULL, TargetType nvarchar(50), TargetValue nvarchar(max), IsPublished bit, PublishDate datetime2 DEFAULT SYSUTCDATETIME());");
            var voter = new Employee { EmployeeNo = "SYN-A-3", FullName = "Synthetic voter", CompanyId = companyA.Id,
                Branch = branchA, Department = departmentA, IsActive = true };
            db.Employees.Add(voter); await db.SaveChangesAsync();
            var pollRule = (await HrSettingsStore.LoadNotificationRulesAsync(db)).Single(r => r.Name == "الإنتخابات و إستطلاعات الرأي");
            Assert.IsType<JsonResult>(await model.OnPostUpdateRuleAsync(pollRule.Id, NotificationRoutingPolicy.VotersAndSupervisors,
                0, "كل الموظفين", null, companyA.Id));
            await NotificationRuleSettings.SaveAsync(db, companyA.Id, pollRule.Id, new()
                { IsEnabled = true, Audience = NotificationRoutingPolicy.VotersAndSupervisors, EnabledSinceUtc = DateTime.UtcNow.AddMinutes(-5) });
            await Sql($"INSERT EmployeePolls(Id,CompanyId,TargetType,TargetValue,IsPublished) VALUES (1,{companyA.Id},N'Employee',N'{voter.Id},{supervisor.Id},{voter.Id},{employeeB.Id}',1), (2,{companyB.Id},N'All',N'',1),(3,NULL,N'All',N'',1);");
            Assert.Equal(1, (await NotificationRuleGenerator.GenerateAsync(db, new NoOpWebPushSender(), today, companyScope: scope)).NewEvents);
            var pollNotification = await db.Set<UserNotification>().Include(n => n.Recipients).SingleAsync(n => n.TitleAr == pollRule.Name);
            Assert.Equal(new[] { supervisor.Id, voter.Id }.Order(), pollNotification.Recipients.Select(r => r.EmployeeId).Order());
            Assert.DoesNotContain(employeeB.Id, pollNotification.Recipients.Select(r => r.EmployeeId));
            Assert.DoesNotContain(supervisor.FullName, pollNotification.MessageAr);
            Assert.Equal(1, await HrmsDatabase.ScalarAsync<int>(db, "SELECT COUNT(*) FROM SystemNotifications WHERE Title = N'الإنتخابات و إستطلاعات الرأي';", _ => { }));
            voter.IsActive = false; await db.SaveChangesAsync();
            Assert.Equal(0, (await NotificationRuleGenerator.GenerateAsync(db, new NoOpWebPushSender(), today, companyScope: scope)).NewEvents);
            // Farewell is the single, explicit inactive-subject exception during a limited window.
            await Sql(EndServiceAccessStore.MigrationSql);
            await Sql("IF OBJECT_ID('EmployeeEndServices','U') IS NULL CREATE TABLE EmployeeEndServices(Id int PRIMARY KEY, EmployeeId int);");
            await Sql($"""
INSERT EmployeeEndServices(Id,EmployeeId) VALUES(1,{employeeA.Id}),(2,{employeeB.Id});
INSERT EndServiceAccessSchedules(EndServiceId,EmployeeId,CompanyId,NotificationEligibleAtUtc,AccessEndsAtUtc,Immediate)
VALUES(1,{employeeA.Id},{companyA.Id},SYSUTCDATETIME(),DATEADD(day,1,SYSUTCDATETIME()),0),
 (2,{employeeB.Id},{companyB.Id},SYSUTCDATETIME(),DATEADD(day,1,SYSUTCDATETIME()),0);
""");
            var farewellRule = (await HrSettingsStore.LoadNotificationRulesAsync(db)).Single(r => r.Name == "وداع موظف");
            await Sql($"UPDATE ZynoraNotificationRules SET IsEnabled = 1 WHERE Id = {farewellRule.Id};");
            await NotificationRuleSettings.SaveAsync(db, companyA.Id, farewellRule.Id, new()
                { IsEnabled = true, Audience = NotificationRoutingPolicy.Self, EnabledSinceUtc = DateTime.UtcNow.AddMinutes(-5) });
            Assert.Equal(1, (await NotificationRuleGenerator.GenerateAsync(db, new NoOpWebPushSender(), today, companyScope: scope)).NewEvents);
            var farewell = await db.Set<UserNotification>().Include(n => n.Recipients)
                .SingleAsync(n => n.Url == "/Account/Farewell?serviceId=1");
            Assert.Equal([employeeA.Id], farewell.Recipients.Select(r => r.EmployeeId));
            Assert.DoesNotContain(employeeB.Id, farewell.Recipients.Select(r => r.EmployeeId));
            await Sql($"""
CREATE TABLE AppLoginUsers(Id int PRIMARY KEY, TenantId int, EmployeeId int, Username nvarchar(100),
 PasswordHash nvarchar(100), PasswordSalt nvarchar(100), Role nvarchar(50), IsActive bit,
 FailedLoginAttempts int, LockoutEndUtc datetime2 NULL, SecurityStamp nvarchar(64));
INSERT AppLoginUsers VALUES(1,{tenantA.Id},{employeeA.Id},N'synthetic-farewell',N'hash',N'salt',N'Employee',1,0,NULL,N'stamp');
""");
            var farewellContext = new DefaultHttpContext();
            farewellContext.User = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(
                [new(System.Security.Claims.ClaimTypes.Name, "synthetic-farewell"),
                 new(TenantContext.TenantIdClaimType, tenantA.Id.ToString()), new("EmployeeId", employeeB.Id.ToString())], "test"));
            var ownPage = new SmartAttendance.Web.Pages.Account.FarewellModel(db)
                { PageContext = new PageContext { HttpContext = farewellContext } };
            Assert.IsType<PageResult>(await ownPage.OnGetAsync());
            Assert.Equal([farewell.MessageAr ?? ""], ownPage.Messages); // Only this period's farewell, not its prior inbox.
            farewellContext.User = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(
                [new(System.Security.Claims.ClaimTypes.Name, "synthetic-farewell"), new(TenantContext.TenantIdClaimType, tenantB.Id.ToString())], "test"));
            Assert.IsType<RedirectToPageResult>(await ownPage.OnGetAsync()); // Same username in another tenant owns nothing here.
            Assert.Equal(0, (await NotificationRuleGenerator.GenerateAsync(db, new NoOpWebPushSender(), today, companyScope: scope)).NewEvents);
            await Sql("UPDATE EndServiceAccessSchedules SET AccessEndsAtUtc = DATEADD(second,-1,SYSUTCDATETIME()) WHERE EndServiceId = 1; DELETE ZynoraNotificationEvents WHERE RuleKind = N'Farewell';");
            Assert.Equal(0, (await NotificationRuleGenerator.GenerateAsync(db, new NoOpWebPushSender(), today, companyScope: scope)).NewEvents);
            async Task Sql(string sql) => await db.Database.ExecuteSqlRawAsync(sql);
        }
        finally
        {
            if (created)
            {
                // Exact generated name, created by this test; never a configured application database.
                await using var drop = master.CreateCommand();
                drop.CommandText = $"ALTER DATABASE [{database}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{database}];";
                await drop.ExecuteNonQueryAsync();
            }
        }
    }

    private sealed class CapturingEmailSender : IEmailSender
    {
        public bool IsEnabled => true;
        public bool Fail { get; init; }
        public List<EmailMessage> Messages { get; } = [];
        public Task<EmailResult> SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
        { Messages.Add(message); return Task.FromResult(Fail ? new EmailResult(false, "synthetic failure") : EmailResult.Ok); }
    }
    private sealed class FixedScope(int companyId) : ICompanyScopeProvider
    {
        public Task<CompanyScope> GetAsync(CancellationToken cancellationToken = default) => Task.FromResult(CompanyScope.ForCompanies([companyId]));
    }
}
