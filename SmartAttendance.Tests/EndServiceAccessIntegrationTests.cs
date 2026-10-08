using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;
using SmartAttendance.Web.Infrastructure.Security;

namespace SmartAttendance.Tests;

[Collection(ProductionClosureSqlCollection.Name)]
public sealed class EndServiceAccessIntegrationTests
{
    [SkippableFact]
    public async Task Scoped_schedule_restricts_cached_sessions_and_closes_only_current_service_period()
    {
        var configured = Environment.GetEnvironmentVariable("SMARTATTENDANCE_SQL_TEST_MASTER");
        Skip.If(string.IsNullOrWhiteSpace(configured), "Explicit LocalDB test connection required.");
        var builder = new SqlConnectionStringBuilder(configured) { InitialCatalog = "master", Pooling = false };
        Assert.StartsWith("(localdb)\\", builder.DataSource, StringComparison.OrdinalIgnoreCase);
        var database = "SmartAttendance_E2E_Notifications_" + Guid.NewGuid().ToString("N");
        Assert.Matches("^SmartAttendance_E2E_Notifications_[a-f0-9]{32}$", database);
        await using var master = new SqlConnection(builder.ConnectionString);
        await master.OpenAsync();
        bool created = false;
        try
        {
            await using (var create = master.CreateCommand())
            {
                create.CommandText = $"CREATE DATABASE [{database}];";
                await create.ExecuteNonQueryAsync(); created = true;
            }
            builder.InitialCatalog = database;
            await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(builder.ConnectionString).Options);
            async Task Sql(string sql) => await HrmsDatabase.ExecuteAsync(db, sql, _ => { });
            Assert.False(await EndServiceAccessStore.IsReadyAsync(db));
            await Sql("""
CREATE TABLE Companies(Id int PRIMARY KEY, TenantId int);
CREATE TABLE Employees(Id int PRIMARY KEY, CompanyId int, IsActive bit, IsDeleted bit, FullName nvarchar(200));
CREATE TABLE EmployeeEndServices(Id int PRIMARY KEY, EmployeeId int);
CREATE TABLE AppLoginUsers(Id int PRIMARY KEY, EmployeeId int, TenantId int, Username nvarchar(100),
    IsActive bit, Role nvarchar(50), SecurityStamp nvarchar(64), LockoutEndUtc datetime2 NULL,
    MustChangePassword bit, UpdatedAt datetime2 NULL, PasswordHash nvarchar(100), PasswordSalt nvarchar(100), FailedLoginAttempts int);
CREATE TABLE SystemUsers(Id int PRIMARY KEY, TenantId int, EmployeeId int, IsActive bit);
CREATE TABLE ApiTokens(Id int PRIMARY KEY, TenantId int, Username nvarchar(100), RevokedAt datetime2 NULL);
INSERT Companies VALUES(1,10),(2,20);
INSERT Employees VALUES(1,1,0,0,N'Synthetic A'),(2,2,0,0,N'Synthetic B'),(3,1,1,0,N'Synthetic rehire'),(4,1,0,0,N'Synthetic disabled');
INSERT EmployeeEndServices VALUES(1,1),(2,2),(3,3),(4,4);
INSERT AppLoginUsers VALUES(1,1,10,N'synthetic',1,N'Employee',N'stamp',NULL,0,NULL,N'hash',N'salt',0),
 (2,2,20,N'synthetic',1,N'Employee',N'stamp',NULL,0,NULL,N'hash',N'salt',0),
 (3,3,10,N'rehire',1,N'Employee',N'stamp',NULL,0,NULL,N'hash',N'salt',0),
 (4,4,10,N'disabled',0,N'Employee',N'stamp',NULL,0,NULL,N'hash',N'salt',0);
INSERT SystemUsers VALUES(1,10,1,1),(2,20,2,1),(3,10,3,1),(4,10,4,0);
INSERT ApiTokens VALUES(1,10,N'synthetic',NULL),(2,20,N'synthetic',NULL),(3,10,N'rehire',NULL);
""");
            await Sql(EndServiceAccessStore.MigrationSql);
            await Sql(EndServiceAccessStore.MigrationSql); // Reapplication changes no data.
            using var cache = new MemoryCache(new MemoryCacheOptions());
            Assert.True((await AccountSecurityStore.GetStateAsync(db, cache, 10, "synthetic")).IsActive);
            Assert.False((await AccountSecurityStore.GetStateAsync(db, cache, 10, "synthetic")).FarewellOnly);
            await Sql("""
INSERT EndServiceAccessSchedules(EndServiceId,EmployeeId,CompanyId,NotificationEligibleAtUtc,AccessEndsAtUtc,Immediate)
VALUES (1,1,1,DATEADD(day,-1,SYSUTCDATETIME()),DATEADD(day,1,SYSUTCDATETIME()),0),
 (2,2,2,SYSUTCDATETIME(),DATEADD(day,2,SYSUTCDATETIME()),0),
 (3,3,1,SYSUTCDATETIME(),DATEADD(day,-1,SYSUTCDATETIME()),0),
 (4,4,1,SYSUTCDATETIME(),DATEADD(day,1,SYSUTCDATETIME()),0);
""");
            var schedule = Assert.IsType<EndServiceAccessStore.Schedule>(await EndServiceAccessStore.GetAsync(db, 10, "synthetic"));
            Assert.Equal(1, schedule.EmployeeId);
            Assert.Equal(2, (await EndServiceAccessStore.GetAsync(db, 20, "synthetic"))!.EmployeeId);
            Assert.Null(await EndServiceAccessStore.GetAsync(db, 30, "synthetic"));
            Assert.Null(await EndServiceAccessStore.GetAsync(db, 10, "rehire"));
            var live = await AccountSecurityStore.GetStateAsync(db, cache, 10, "synthetic");
            Assert.True(live.FarewellOnly); // Cache populated before termination is not a bypass.
            Assert.True(live.IsActive);
            Assert.False((await LoginDatabase.GetByUsernameAsync(db, 10, "disabled"))!.IsActive);
            Assert.True((await LoginDatabase.GetByUsernameAsync(db, 10, "synthetic"))!.FarewellOnly);

            var auth = new StubAuthentication();
            using var services = new ServiceCollection().AddSingleton<IAuthenticationService>(auth).BuildServiceProvider();
            bool passed = false;
            var middleware = new EndServiceAccessMiddleware(_ => { passed = true; return Task.CompletedTask; });
            async Task<DefaultHttpContext> Request(string path, string method)
            {
                passed = false;
                var context = new DefaultHttpContext { RequestServices = services };
                context.User = new ClaimsPrincipal(new ClaimsIdentity([new(ClaimTypes.Name, "synthetic"),
                    new(TenantContext.TenantIdClaimType, "10"), new("EmployeeId", "2")], "test")); // Forged claim cannot change owner.
                context.Request.Path = path; context.Request.Method = method;
                await middleware.InvokeAsync(context, db);
                return context;
            }
            Assert.Equal(302, (await Request("/Employees", "GET")).Response.StatusCode);
            Assert.False(passed);
            Assert.Equal(403, (await Request("/Employees", "POST")).Response.StatusCode);
            Assert.Equal(403, (await Request("/api/employee", "GET")).Response.StatusCode);
            await Request("/Account/Farewell", "GET"); Assert.True(passed);
            await Request("/Account/Logout", "GET"); Assert.True(passed);
            await Request("/Platform/Login", "GET"); Assert.True(passed); // Independent platform scheme/policy still applies downstream.
            Assert.Equal(403, (await Request("/Account/Farewell", "POST")).Response.StatusCode);
            // Exact cutoff is already tested by the pure policy; expired DB state must deny before any worker.
            await Sql("UPDATE EndServiceAccessSchedules SET AccessEndsAtUtc = DATEADD(second,-1,SYSUTCDATETIME()) WHERE EndServiceId = 1;");
            Assert.False((await AccountSecurityStore.GetStateAsync(db, cache, 10, "synthetic")).IsActive);
            Assert.False((await LoginDatabase.GetByUsernameAsync(db, 10, "synthetic"))!.IsActive);
            Assert.Equal(401, (await Request("/Account/Farewell", "GET")).Response.StatusCode);
            Assert.Equal(1, auth.SignOuts);
            await EndServiceAccessStore.CloseDueAsync(db, DateTimeOffset.UtcNow);
            await EndServiceAccessStore.CloseDueAsync(db, DateTimeOffset.UtcNow); // idempotent retry
            Assert.Equal(0, await Count("SELECT COUNT(*) FROM AppLoginUsers WHERE Id = 1 AND IsActive = 1;"));
            Assert.Equal(1, await Count("SELECT COUNT(*) FROM ApiTokens WHERE Id = 1 AND RevokedAt IS NOT NULL;"));
            Assert.Equal(1, await Count("SELECT COUNT(*) FROM SystemUsers WHERE Id = 1 AND IsActive = 0;"));
            Assert.Equal(1, await Count("SELECT COUNT(*) FROM AppLoginUsers WHERE Id = 2 AND IsActive = 1;"));
            Assert.Equal(1, await Count("SELECT COUNT(*) FROM ApiTokens WHERE Id = 2 AND RevokedAt IS NULL;"));
            Assert.Equal(1, await Count("SELECT COUNT(*) FROM AppLoginUsers WHERE Id = 3 AND IsActive = 1;"));
            Assert.False((await LoginDatabase.GetByUsernameAsync(db, 10, "disabled"))!.IsActive);
            // A new service period without a schedule must never inherit old grace/deadline.
            await Sql("INSERT EmployeeEndServices VALUES(5,1);");
            Assert.Null(await EndServiceAccessStore.GetAsync(db, 10, "synthetic"));
            async Task<int> Count(string sql) => await HrmsDatabase.ScalarAsync<int>(db, sql, _ => { });
        }
        finally
        {
            if (created)
            {
                await using var cleanup = master.CreateCommand();
                cleanup.CommandText = $"ALTER DATABASE [{database}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{database}];";
                await cleanup.ExecuteNonQueryAsync();
            }
        }
    }

    private sealed class StubAuthentication : IAuthenticationService
    {
        public int SignOuts { get; private set; }
        public Task<AuthenticateResult> AuthenticateAsync(HttpContext context, string? scheme) => Task.FromResult(AuthenticateResult.NoResult());
        public Task ChallengeAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) => Task.CompletedTask;
        public Task ForbidAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) => Task.CompletedTask;
        public Task SignInAsync(HttpContext context, string? scheme, ClaimsPrincipal principal, AuthenticationProperties? properties) => Task.CompletedTask;
        public Task SignOutAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) { SignOuts++; return Task.CompletedTask; }
    }
}
