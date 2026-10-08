using Microsoft.Data.SqlClient;
using SmartAttendance.Web.Infrastructure.Notifications;

namespace SmartAttendance.Tests;

[Collection(ProductionClosureSqlCollection.Name)]
public sealed class NotificationPollTests
{
    [Fact]
    public void Publication_identity_is_independent_of_first_voter_and_routes_once()
    {
        var kind = NotificationRuleGenerator.RuleKind.Poll;
        var row = new NotificationEventSources.Event(7, 1, new DateTime(2026, 10, 7));
        Assert.Equal(NotificationEventSources.Key(kind, 3, row), NotificationEventSources.Key(kind, 3, row with { EmployeeId = 11 }));
        Assert.NotEqual(NotificationEventSources.Key(kind, 3, row), NotificationEventSources.Key(kind, 4, row));
        Assert.NotEqual(NotificationEventSources.Key(kind, 3, row), NotificationEventSources.Key(kind, 3, row with { OccurredAt = row.OccurredAt.AddTicks(1) }));
        var plan = NotificationRoutingPolicy.Resolve(NotificationRoutingPolicy.VotersAndSupervisors, 1, null,
            [new(1, "synthetic-hr"), new(4, "synthetic-hr2")], [], null, [1, 1, 2]);
        Assert.Equal([1, 2, 4], plan.EmployeeIds.Order());
        Assert.Equal(2, plan.BackOfficeUsers.Count);
        plan = NotificationRoutingPolicy.Resolve(NotificationRoutingPolicy.Voters, 1, null, [new(4, "synthetic-hr")], [], null, [2]);
        Assert.Equal([2], plan.EmployeeIds);
        Assert.Empty(plan.BackOfficeUsers);
        Assert.True(NotificationRuleCatalog.AllowsAudience("الإنتخابات و إستطلاعات الرأي", NotificationRoutingPolicy.Voters));
        Assert.False(NotificationRuleCatalog.AllowsAudience("عيد ميلاد موظف", NotificationRoutingPolicy.Voters));
    }

    [SkippableFact]
    public async Task Sql_targets_exact_ids_active_company_and_publication_window()
    {
        var configured = Environment.GetEnvironmentVariable("SMARTATTENDANCE_SQL_TEST_MASTER");
        Skip.If(string.IsNullOrWhiteSpace(configured), "Explicit local SQL test connection is required.");
        var builder = new SqlConnectionStringBuilder(configured);
        Assert.StartsWith("(localdb)\\", builder.DataSource, StringComparison.OrdinalIgnoreCase);
        await using var connection = new SqlConnection(builder.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
CREATE TABLE #Employees(Id int PRIMARY KEY, CompanyId int, DepartmentId int, BranchId int, IsActive bit, IsDeleted bit);
CREATE TABLE #EmployeePolls(Id int PRIMARY KEY, CompanyId int NULL, TargetType nvarchar(50), TargetValue nvarchar(max), IsPublished bit, PublishDate datetime2(7));
CREATE TABLE #ZynoraNotificationEvents(EventKey nvarchar(200) PRIMARY KEY);
INSERT #Employees VALUES (1,3,5,9,1,0),(11,3,6,10,1,0),(2,4,5,9,1,0),(3,3,5,9,0,0),(4,3,5,9,1,1);
INSERT #EmployeePolls VALUES
 (1,3,N'Employee',N'11,11,invalid',1,'2026-10-07T12:00:00.1234567'),
 (2,3,N'All',N'',1,'2026-10-07T12:00:00'),
 (3,3,N'Department',N'5',1,'2026-10-07T12:00:00'),
 (4,3,N'Branch',N'10',1,'2026-10-07T12:00:00'),
 (5,4,N'All',N'',1,'2026-10-07T12:00:00'),
 (6,NULL,N'All',N'',1,'2026-10-07T12:00:00'),
 (7,3,N'Unknown',N'',1,'2026-10-07T12:00:00'),
 (8,3,N'All',N'',0,'2026-10-07T12:00:00'),
 (9,3,N'All',N'',1,'2020-01-01'),
 (10,3,N'All',N'',1,'2027-01-01');
""";
        await command.ExecuteNonQueryAsync();
        var rows = await Read();
        Assert.Equal([(1, 11), (2, 1), (2, 11), (3, 1), (4, 11)], rows.Order());
        var source = new NotificationEventSources.Event(2, 1, new DateTime(2026, 10, 7, 12, 0, 0));
        command.Parameters.Clear();
        command.CommandText = "INSERT #ZynoraNotificationEvents VALUES (@Key); UPDATE #Employees SET IsActive = 0 WHERE Id = 1;";
        command.Parameters.AddWithValue("@Key", NotificationEventSources.Key(NotificationRuleGenerator.RuleKind.Poll, 3, source));
        await command.ExecuteNonQueryAsync();
        Assert.Equal([(1, 11), (4, 11)], (await Read()).Order()); // No replay when first voter leaves.

        async Task<List<(int, int)>> Read()
        {
            command.Parameters.Clear();
            command.CommandText = NotificationEventSources.ScopedQuery(NotificationEventSources.Describe(NotificationRuleGenerator.RuleKind.Poll)!)
                .Replace("EmployeePolls", "#EmployeePolls").Replace("Employees", "#Employees").Replace("ZynoraNotificationEvents", "#ZynoraNotificationEvents");
            command.Parameters.AddWithValue("@CompanyId", 3);
            command.Parameters.AddWithValue("@Kind", "Poll");
            command.Parameters.AddWithValue("@EnabledSince", new DateTime(2026, 10, 7));
            command.Parameters.AddWithValue("@Now", new DateTime(2026, 10, 8));
            var found = new List<(int, int)>();
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync()) found.Add((reader.GetInt32(0), reader.GetInt32(1)));
            return found;
        }
    }
}
