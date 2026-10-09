using Microsoft.Data.SqlClient;
using SmartAttendance.Web.Infrastructure.Hrms;

namespace SmartAttendance.Tests;

public sealed class PollAudienceTests
{
    [Fact]
    public void BothListsAndBothVoteHandlersUseTheSameScopedPredicate()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "SmartAttendance.slnx")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        foreach (var relative in new[] { "Controllers/Api/MeController.cs", "Pages/EmployeePortal/Index.cshtml.cs" })
        {
            var source = File.ReadAllText(Path.Combine(directory!.FullName, "SmartAttendance.Web", relative));
            Assert.Equal(2, source.Split("{PollAudience.SqlPredicate}").Length - 1);
            Assert.DoesNotContain("p.TargetValue LIKE @EmployeeIdLike", source);
        }
        Assert.Contains("pollEmployee.CompanyId", PollAudience.SqlPredicate);
        Assert.Contains("STRING_SPLIT", PollAudience.SqlPredicate);
        Assert.DoesNotContain("LIKE", PollAudience.SqlPredicate);
    }

    [SkippableFact]
    public async Task SqlExactAudienceRejectsOtherCompaniesPartialIdsDeletedSitesAndDeletedEmployees()
    {
        var configured = Environment.GetEnvironmentVariable("SMARTATTENDANCE_SQL_TEST_MASTER");
        Skip.If(string.IsNullOrWhiteSpace(configured), "Explicit isolated LocalDB connection required.");
        var builder = new SqlConnectionStringBuilder(configured);
        Assert.StartsWith("(localdb)\\", builder.DataSource, StringComparison.OrdinalIgnoreCase);
        await using var connection = new SqlConnection(builder.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
CREATE TABLE #Employees(Id int PRIMARY KEY, CompanyId int NULL, DepartmentId int NULL, BranchId int NULL, EmployeeNo nvarchar(80), IsDeleted bit);
CREATE TABLE #Departments(Id int PRIMARY KEY, CompanyId int, Name nvarchar(80));
CREATE TABLE #Branches(Id int PRIMARY KEY, CompanyId int, Name nvarchar(80), IsDeleted bit);
CREATE TABLE #EmployeePolls(Id int PRIMARY KEY, CompanyId int NULL, TargetType nvarchar(50), TargetValue nvarchar(max));
INSERT #Departments VALUES (5,3,N'synthetic-department'),(6,4,N'synthetic-department');
INSERT #Branches VALUES (9,3,N'synthetic-site',0),(10,4,N'synthetic-site',0),(12,3,N'deleted-site',1);
INSERT #Employees VALUES (1,3,5,9,N'CODE-A',0),(11,3,5,9,N'CODE-B',0),(2,4,6,10,N'CODE-C',0),(3,3,5,12,N'CODE-D',0),(4,3,5,9,N'CODE-E',1);
INSERT #EmployeePolls VALUES
 (1,3,N'Employee',N'11,invalid'),(2,3,N'Employee',N' 1 ,11'),
 (3,3,N'Department',N'5'),(4,3,N'Branch',N'9'),
 (5,4,N'All',N''),(6,NULL,N'All',N''),(7,3,N'Unknown',N''),
 (8,3,N'Department',N'synthetic-department'),(9,3,N'Branch',N'synthetic-site'),
 (10,3,N'Employee',N'CODE-A'),(11,3,N'Branch',N'12'),(12,3,N'Branch',N'deleted-site');
""";
        await command.ExecuteNonQueryAsync();
        Assert.Equal(new[] {2,3,4,6,8,9,10}, await Read(1));
        Assert.Equal(new[] {1,2,3,4,6,8,9}, await Read(11));
        Assert.Equal(new[] {5,6}, await Read(2));
        Assert.Equal(new[] {3,6,8}, await Read(3));
        Assert.Empty(await Read(4));
        Assert.Empty(await Read(999));

        async Task<int[]> Read(int employeeId)
        {
            command.Parameters.Clear();
            command.Parameters.AddWithValue("@EmployeeId", employeeId);
            command.CommandText = ("SELECT p.Id FROM #EmployeePolls p WHERE " + PollAudience.SqlPredicate + " ORDER BY p.Id;")
                .Replace("FROM Employees ", "FROM #Employees ")
                .Replace("JOIN Departments ", "JOIN #Departments ")
                .Replace("JOIN Branches ", "JOIN #Branches ");
            var ids = new List<int>();
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync()) ids.Add(reader.GetInt32(0));
            return ids.ToArray();
        }
    }
}
