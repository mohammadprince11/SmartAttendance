using Microsoft.Data.SqlClient;
using SmartAttendance.Web.Infrastructure.Notifications;

namespace SmartAttendance.Tests;

[Collection(ProductionClosureSqlCollection.Name)]
public sealed class NotificationEventSourceSqlTests
{
    [SkippableFact]
    public async Task Scoped_source_excludes_history_other_companies_and_exact_delivered_revision()
    {
        // Explicit dedicated LOCAL test connection only. No application configuration or production connection.
        var configured = Environment.GetEnvironmentVariable("SMARTATTENDANCE_SQL_TEST_MASTER");
        Skip.If(string.IsNullOrWhiteSpace(configured), "Dedicated SQL test connection is not configured.");
        var builder = new SqlConnectionStringBuilder(configured);
        var server = builder.DataSource;
        Assert.True(server.StartsWith("(localdb)\\", StringComparison.OrdinalIgnoreCase)
            || server.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            || server.Equals(".", StringComparison.OrdinalIgnoreCase)
            || server.Equals(Environment.MachineName, StringComparison.OrdinalIgnoreCase), "Only a local dedicated test server is permitted.");
        await using var connection = new SqlConnection(builder.ConnectionString);
        await connection.OpenAsync();
        // Connection-local temporary fixture tables disappear on close. No permanent database changes.
        await using (var setup = connection.CreateCommand())
        {
            setup.CommandText = """
CREATE TABLE #Employees(Id int PRIMARY KEY, CompanyId int, IsActive bit, IsDeleted bit);
CREATE TABLE #EmployeeFeedbackItems(Id int PRIMARY KEY, EmployeeId int, CreatedAt datetime2(7), RepliedAt datetime2(7), AdminReply nvarchar(200));
CREATE TABLE #ZynoraNotificationEvents(EventKey nvarchar(200) PRIMARY KEY);
CREATE TABLE #FormSubmissions(Id int PRIMARY KEY, EmployeeId int, FormType nvarchar(30), Status nvarchar(20), SubmittedAt datetime2(7));
ALTER TABLE #Employees ADD CreatedAt datetime2, HireDate date, JoiningDate date;
CREATE TABLE #EmployeeContracts(Id int PRIMARY KEY, EmployeeId int, FromDate date, IsCurrent bit, IsDeleted bit);
INSERT #Employees(Id,CompanyId,IsActive,IsDeleted) VALUES (1, 3, 1, 0), (2, 4, 1, 0), (3, 3, 0, 0), (4, 3, 1, 1);
INSERT #EmployeeFeedbackItems VALUES
 (11, 1, '2026-10-07T12:00:00.1234567', '2026-10-07T12:01:00.7654321', N'synthetic reply'),
 (12, 2, '2026-10-07T12:00:00', NULL, NULL),
 (13, 3, '2026-10-07T12:00:00', NULL, NULL),
 (14, 4, '2026-10-07T12:00:00', NULL, NULL),
 (15, 1, '2020-01-01', NULL, NULL);
UPDATE #Employees SET CreatedAt = '2026-10-07T12:00:00', HireDate = '2026-10-07', JoiningDate = '2026-10-08';
INSERT #EmployeeContracts VALUES (1,1,'2026-10-07',1,0),(2,1,'2027-01-01',0,0),(3,1,'2028-01-01',1,1),(4,2,'2026-10-07',1,0);
INSERT #FormSubmissions VALUES
 (31,1,N'ExitInterview',N'Submitted','2026-10-07T12:00:00'),
 (32,3,N'ExitInterview',N'Submitted','2026-10-07T12:00:00'),
 (33,2,N'ExitInterview',N'Submitted','2026-10-07T12:00:00'),
 (34,4,N'ExitInterview',N'Submitted','2026-10-07T12:00:00'),
 (35,1,N'ExitInterview',N'Cancelled','2026-10-07T12:00:00'),
 (36,1,N'ExitInterview',N'Submitted','2020-01-01'),
 (37,1,N'Survey',N'Submitted','2026-10-07T12:00:00');
""";
            await setup.ExecuteNonQueryAsync();
        }
        var kind = NotificationRuleGenerator.RuleKind.Feedback;
        Assert.Equal(new[] { 11 }, await ReadAsync(kind));
        var source = new NotificationEventSources.Event(11, 1, new DateTime(2026, 10, 7, 12, 0, 0).AddTicks(1234567));
        await InsertKeyAsync(NotificationEventSources.Key(kind, 3, source));
        Assert.Empty(await ReadAsync(kind));
        kind = NotificationRuleGenerator.RuleKind.FeedbackReply;
        Assert.Equal(new[] { 11 }, await ReadAsync(kind));
        source = source with { OccurredAt = new DateTime(2026, 10, 7, 12, 1, 0).AddTicks(7654321) };
        await InsertKeyAsync(NotificationEventSources.Key(kind, 3, source));
        Assert.Empty(await ReadAsync(kind));
        await using (var update = connection.CreateCommand())
        {
            update.CommandText = "UPDATE #EmployeeFeedbackItems SET RepliedAt = '2026-10-07T12:01:00.7654322' WHERE Id = 11;";
            await update.ExecuteNonQueryAsync();
        }
        Assert.Equal(new[] { 11 }, await ReadAsync(kind));

        Assert.Equal([31, 32], (await ReadAsync(NotificationRuleGenerator.RuleKind.ExitInterview)).Order());
        foreach (var basis in NotificationRuleCatalog.WelcomeBases)
            Assert.Equal([1], await ReadAsync(NotificationRuleGenerator.RuleKind.Welcome, basis.Value));

        async Task<List<int>> ReadAsync(NotificationRuleGenerator.RuleKind rule, string basis = "CreatedAt")
        {
            await using var command = connection.CreateCommand();
            command.CommandText = NotificationEventSources.ScopedQuery(NotificationEventSources.Describe(rule, basis)!)
                .Replace("EmployeeFeedbackItems", "#EmployeeFeedbackItems", StringComparison.Ordinal)
                .Replace("ZynoraNotificationEvents", "#ZynoraNotificationEvents", StringComparison.Ordinal)
                .Replace("EmployeeContracts", "#EmployeeContracts", StringComparison.Ordinal)
                .Replace("FormSubmissions", "#FormSubmissions", StringComparison.Ordinal)
                .Replace("Employees", "#Employees", StringComparison.Ordinal);
            command.Parameters.AddWithValue("@CompanyId", 3);
            command.Parameters.AddWithValue("@Kind", rule.ToString());
            command.Parameters.AddWithValue("@EnabledSince", rule == NotificationRuleGenerator.RuleKind.Welcome ? new DateTime(2026, 10, 6) : new DateTime(2026, 10, 7));
            command.Parameters.AddWithValue("@Now", new DateTime(2026, 10, 8));
            var ids = new List<int>();
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync()) ids.Add(reader.GetInt32(0));
            return ids;
        }
        async Task InsertKeyAsync(string key)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "INSERT #ZynoraNotificationEvents VALUES (@Key);";
            command.Parameters.AddWithValue("@Key", key);
            await command.ExecuteNonQueryAsync();
        }
    }
}
