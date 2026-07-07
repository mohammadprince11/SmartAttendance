using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;

namespace SmartAttendance.Web.Pages;

public class IndexModel : PageModel
{
    private const int DistributionTopLimit = 5;
    private readonly ApplicationDbContext _dbContext;

    public IndexModel(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public int Companies { get; set; }
    public int Branches { get; set; }
    public int Departments { get; set; }
    public int Employees { get; set; }
    public int ActiveEmployees { get; set; }
    public int InactiveEmployees { get; set; }
    public int Devices { get; set; }
    public int Shifts { get; set; }
    public int AttendanceRecords { get; set; }

    public int TodayPresent { get; set; }
    public int TodayLate { get; set; }
    public int TodayAbsent { get; set; }
    public int TodayMissingOut { get; set; }

    public int PendingRequests { get; set; }
    public int ApprovedRequests30 { get; set; }
    public int RejectedRequests30 { get; set; }

    public int Contracts30 { get; set; }
    public int MissingPunches30 { get; set; }
    public int Late30 { get; set; }
    public int Absent30 { get; set; }

    public List<NameCountRow> ByBranch { get; set; } = new();
    public List<NameCountRow> ByDepartment { get; set; } = new();
    public List<NameCountRow> ByGender { get; set; } = new();
    public List<NameCountRow> ByCountry { get; set; } = new();
    public List<NameCountRow> ByNationality { get; set; } = new();
    public List<NameCountRow> ByContractType { get; set; } = new();
    public List<NameCountRow> RequestsByStatus { get; set; } = new();
    public List<ContractRow> ExpiringContracts { get; set; } = new();
    public List<RequestRow> LatestRequests { get; set; } = new();

    public async Task OnGetAsync()
    {
        await HrmsDatabase.EnsureCreatedAsync(_dbContext);

        Companies = await CountAsync("SELECT COUNT(*) FROM Companies");
        Branches = await CountAsync("SELECT COUNT(*) FROM Branches");
        Departments = await CountAsync("SELECT COUNT(*) FROM Departments");
        Employees = await CountAsync("SELECT COUNT(*) FROM Employees");
        ActiveEmployees = await CountAsync("SELECT COUNT(*) FROM Employees WHERE IsActive = 1");
        InactiveEmployees = await CountAsync("SELECT COUNT(*) FROM Employees WHERE IsActive = 0");
        Devices = await CountAsync("SELECT COUNT(*) FROM Devices");
        Shifts = await CountAsync("SELECT COUNT(*) FROM Shifts");
        AttendanceRecords = await CountAsync("SELECT COUNT(*) FROM AttendanceRecords");

        TodayPresent = await CountAsync("SELECT COUNT(*) FROM AttendanceRecords WHERE AttendanceDate = CAST(GETDATE() AS date) AND Status = 1");
        TodayLate = await CountAsync("SELECT COUNT(*) FROM AttendanceRecords WHERE AttendanceDate = CAST(GETDATE() AS date) AND Status = 2");
        TodayAbsent = await CountAsync("SELECT COUNT(*) FROM AttendanceRecords WHERE AttendanceDate = CAST(GETDATE() AS date) AND Status = 3");
        TodayMissingOut = await CountAsync("SELECT COUNT(*) FROM AttendanceRecords WHERE AttendanceDate = CAST(GETDATE() AS date) AND CheckOut IS NULL");

        PendingRequests = await CountAsync("SELECT COUNT(*) FROM SelfServiceRequests WHERE Status = 'Pending'");
        ApprovedRequests30 = await CountAsync("SELECT COUNT(*) FROM SelfServiceRequests WHERE Status = 'Approved' AND CreatedAt >= DATEADD(day, -30, SYSUTCDATETIME())");
        RejectedRequests30 = await CountAsync("SELECT COUNT(*) FROM SelfServiceRequests WHERE Status = 'Rejected' AND CreatedAt >= DATEADD(day, -30, SYSUTCDATETIME())");

        Contracts30 = await CountAsync("SELECT COUNT(*) FROM Employees WHERE ContractEndDate IS NOT NULL AND ContractEndDate >= CAST(GETDATE() AS date) AND ContractEndDate <= DATEADD(day, 30, CAST(GETDATE() AS date))");
        MissingPunches30 = await CountAsync("SELECT COUNT(*) FROM AttendanceRecords WHERE AttendanceDate >= DATEADD(day, -30, CAST(GETDATE() AS date)) AND CheckOut IS NULL");
        Late30 = await CountAsync("SELECT COUNT(*) FROM AttendanceRecords WHERE AttendanceDate >= DATEADD(day, -30, CAST(GETDATE() AS date)) AND Status = 2");
        Absent30 = await CountAsync("SELECT COUNT(*) FROM AttendanceRecords WHERE AttendanceDate >= DATEADD(day, -30, CAST(GETDATE() AS date)) AND Status = 3");

        ByBranch = await LoadDistributionSummaryAsync("""
SELECT ISNULL(NULLIF(b.Name, ''), 'Not Set') AS Name, COUNT(*) AS Total
FROM Employees e
LEFT JOIN Departments d ON e.DepartmentId = d.Id
LEFT JOIN Branches b ON d.BranchId = b.Id
GROUP BY ISNULL(NULLIF(b.Name, ''), 'Not Set')
ORDER BY Total DESC, Name;
""");

        ByDepartment = await LoadDistributionSummaryAsync("""
SELECT ISNULL(NULLIF(d.Name, ''), 'Not Set') AS Name, COUNT(*) AS Total
FROM Employees e
LEFT JOIN Departments d ON e.DepartmentId = d.Id
GROUP BY ISNULL(NULLIF(d.Name, ''), 'Not Set')
ORDER BY Total DESC, Name;
""");

        ByGender = await LoadDistributionSummaryAsync("SELECT ISNULL(NULLIF(Gender, ''), 'Not Set') AS Name, COUNT(*) AS Total FROM Employees GROUP BY ISNULL(NULLIF(Gender, ''), 'Not Set') ORDER BY Total DESC, Name");
        ByCountry = await LoadDistributionSummaryAsync("SELECT ISNULL(NULLIF(Country, ''), 'Not Set') AS Name, COUNT(*) AS Total FROM Employees GROUP BY ISNULL(NULLIF(Country, ''), 'Not Set') ORDER BY Total DESC, Name");
        ByNationality = await LoadDistributionSummaryAsync("SELECT ISNULL(NULLIF(Nationality, ''), 'Not Set') AS Name, COUNT(*) AS Total FROM Employees GROUP BY ISNULL(NULLIF(Nationality, ''), 'Not Set') ORDER BY Total DESC, Name");
        ByContractType = await LoadDistributionSummaryAsync("SELECT ISNULL(NULLIF(ContractType, ''), 'Not Set') AS Name, COUNT(*) AS Total FROM Employees GROUP BY ISNULL(NULLIF(ContractType, ''), 'Not Set') ORDER BY Total DESC, Name");
        RequestsByStatus = await LoadNameCountsAsync("SELECT Status AS Name, COUNT(*) AS Total FROM SelfServiceRequests GROUP BY Status ORDER BY Total DESC");

        ExpiringContracts = await HrmsDatabase.QueryAsync(
            _dbContext,
            """
SELECT TOP 12
    EmployeeNo,
    FullName,
    ISNULL(Position, '') AS Position,
    ContractEndDate
FROM Employees
WHERE ContractEndDate IS NOT NULL
  AND ContractEndDate >= CAST(GETDATE() AS date)
  AND ContractEndDate <= DATEADD(day, 60, CAST(GETDATE() AS date))
ORDER BY ContractEndDate;
""",
            null,
            reader => new ContractRow
            {
                EmployeeNo = HrmsDatabase.GetString(reader, "EmployeeNo"),
                EmployeeName = HrmsDatabase.GetString(reader, "FullName"),
                Position = HrmsDatabase.GetString(reader, "Position"),
                ContractEndDate = HrmsDatabase.GetDateOnly(reader, "ContractEndDate")
            });

        LatestRequests = await HrmsDatabase.QueryAsync(
            _dbContext,
            """
SELECT TOP 10
    r.Id,
    e.EmployeeNo,
    e.FullName,
    r.RequestType,
    r.Status,
    ISNULL(r.CurrentStep, '') AS CurrentStep,
    r.CreatedAt
FROM SelfServiceRequests r
INNER JOIN Employees e ON r.EmployeeId = e.Id
ORDER BY r.CreatedAt DESC;
""",
            null,
            reader => new RequestRow
            {
                Id = HrmsDatabase.GetInt(reader, "Id"),
                EmployeeNo = HrmsDatabase.GetString(reader, "EmployeeNo"),
                EmployeeName = HrmsDatabase.GetString(reader, "FullName"),
                RequestType = HrmsDatabase.GetString(reader, "RequestType"),
                Status = HrmsDatabase.GetString(reader, "Status"),
                CurrentStep = HrmsDatabase.GetString(reader, "CurrentStep"),
                CreatedAt = HrmsDatabase.GetDateTime(reader, "CreatedAt")
            });
    }

    private async Task<int> CountAsync(string sql)
    {
        return await HrmsDatabase.ScalarAsync<int>(_dbContext, sql);
    }

    private async Task<List<NameCountRow>> LoadNameCountsAsync(string sql)
    {
        return await HrmsDatabase.QueryAsync(
            _dbContext,
            sql,
            null,
            reader => new NameCountRow
            {
                Name = HrmsDatabase.GetString(reader, "Name"),
                Total = HrmsDatabase.GetInt(reader, "Total")
            });
    }

    private async Task<List<NameCountRow>> LoadDistributionSummaryAsync(string sql)
    {
        var rows = await LoadNameCountsAsync(sql);

        var normalizedRows = rows
            .Select(row => new NameCountRow
            {
                Name = NormalizeDistributionName(row.Name),
                Total = row.Total
            })
            .GroupBy(row => row.Name)
            .Select(group => new NameCountRow
            {
                Name = group.Key,
                Total = group.Sum(row => row.Total)
            })
            .OrderByDescending(row => row.Total)
            .ThenBy(row => row.Name)
            .ToList();

        if (normalizedRows.Count <= DistributionTopLimit)
        {
            return normalizedRows;
        }

        var topRows = normalizedRows.Take(DistributionTopLimit).ToList();
        var otherTotal = normalizedRows.Skip(DistributionTopLimit).Sum(row => row.Total);

        if (otherTotal > 0)
        {
            topRows.Add(new NameCountRow
            {
                Name = "أخرى",
                Total = otherTotal
            });
        }

        return topRows;
    }

    private static string NormalizeDistributionName(string? name)
    {
        return string.IsNullOrWhiteSpace(name) || name.Trim().Equals("Not Set", StringComparison.OrdinalIgnoreCase)
            ? "غير محدد"
            : name.Trim();
    }

    public class NameCountRow
    {
        public string Name { get; set; } = string.Empty;
        public int Total { get; set; }
    }

    public class ContractRow
    {
        public string EmployeeNo { get; set; } = string.Empty;
        public string EmployeeName { get; set; } = string.Empty;
        public string Position { get; set; } = string.Empty;
        public DateOnly? ContractEndDate { get; set; }
    }

    public class RequestRow
    {
        public int Id { get; set; }
        public string EmployeeNo { get; set; } = string.Empty;
        public string EmployeeName { get; set; } = string.Empty;
        public string RequestType { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string CurrentStep { get; set; } = string.Empty;
        public DateTime? CreatedAt { get; set; }
    }
}

