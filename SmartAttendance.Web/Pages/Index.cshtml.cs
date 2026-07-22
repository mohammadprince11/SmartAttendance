using System.Data;
using System.Data.Common;
using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.CompanyContext;
using SmartAttendance.Web.Infrastructure.Hrms;

namespace SmartAttendance.Web.Pages;

/// <summary>
/// لوحة التحكم التنفيذية (الصفحة الرئيسية): KPIs القوى العاملة، حالة اليوم،
/// التوزيعات (فروع/أقسام/جنس/جنسية)، والتنبيهات — تتغذى من عدة خدمات إحصائية.
/// </summary>
public class IndexModel : PageModel
{
    private const int DistributionTopLimit = 5;

    private readonly ApplicationDbContext _dbContext;
    private readonly ILogger<IndexModel> _logger;

    public IndexModel(
        ApplicationDbContext dbContext,
        ILogger<IndexModel> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    [BindProperty(SupportsGet = true)]
    public int? CompanyId { get; set; }

    public List<CompanyOption> CompanyOptions { get; set; } = new();

    public string SelectedCompanyName =>
        CompanyId.HasValue
            ? CompanyOptions.FirstOrDefault(x =>
                  x.Id == CompanyId.Value)?.Name ?? "-"
            : "-";

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

    /// <summary>أرقام اليوم من يوميات محرك الحضور الجديد (لا من السجلات الخام).</summary>
    public bool PulseFromEngine { get; set; }

    public int PendingRequests { get; set; }
    public int ApprovedRequests30 { get; set; }
    public int RejectedRequests30 { get; set; }

    public int Contracts30 { get; set; }
    public int MissingPunches30 { get; set; }
    public int Late30 { get; set; }
    public int Absent30 { get; set; }

    public List<TodayEmployeeRow> TodayLateList { get; set; } = new();
    public List<TodayEmployeeRow> TodayMissingOutList { get; set; } = new();

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
        var stopwatch = Stopwatch.StartNew();

        // ترتيب الشركات بعدد موظفيها تنازلياً: الافتراضي (First بـ Resolve) يصير
        // الشركة الفعلية ذات البيانات بدل شركة فارغة تُظهر اللوحة أصفاراً.
        CompanyOptions = await _dbContext.Companies
            .AsNoTracking()
            .Where(x => !x.IsDeleted && x.IsActive)
            .Select(x => new CompanyOption
            {
                Id = x.Id,
                Name = x.Name,
                IsActive = x.IsActive,
                EmployeeCount = _dbContext.Employees.Count(e =>
                    !e.IsDeleted && e.Branch != null && e.Branch.CompanyId == x.Id)
            })
            .OrderByDescending(x => x.EmployeeCount)
            .ThenBy(x => x.Name)
            .ToListAsync();

        CompanyId = CompanySelectionContext.Resolve(
            HttpContext,
            CompanyId,
            CompanyOptions.Select(x => x.Id).ToArray());

        if (!CompanyId.HasValue)
        {
            stopwatch.Stop();
            _logger.LogInformation(
                "Dashboard loaded without a company in {ElapsedMilliseconds} ms.",
                stopwatch.ElapsedMilliseconds);
            return;
        }

        await DayAttendanceStore.EnsureAsync(_dbContext); // جدول اليوميات قد لا يكون أُنشئ بعد
        await LoadDashboardDataAsync();

        stopwatch.Stop();
        _logger.LogInformation(
            "Dashboard loaded for company {CompanyId} in {ElapsedMilliseconds} ms using one dashboard SQL batch.",
            CompanyId.Value,
            stopwatch.ElapsedMilliseconds);
    }

    private async Task LoadDashboardDataAsync()
    {
        const string sql = """
SET NOCOUNT ON;

DECLARE @Today date = CAST(GETDATE() AS date);
DECLARE @FromDate date = DATEADD(day, -30, @Today);
DECLARE @UtcFrom datetime2 = DATEADD(day, -30, SYSUTCDATETIME());

SELECT
    Companies = (
        SELECT COUNT(*)
        FROM Companies
        WHERE IsDeleted = 0
          AND Id = @CompanyId
    ),
    Branches = (
        SELECT COUNT(*)
        FROM Branches
        WHERE IsDeleted = 0
          AND CompanyId = @CompanyId
    ),
    Departments = (
        SELECT COUNT(*)
        FROM Departments
        WHERE IsDeleted = 0
          AND CompanyId = @CompanyId
    ),
    Employees = (
        SELECT COUNT(*)
        FROM Employees e
        INNER JOIN Branches b ON e.BranchId = b.Id
        WHERE e.IsDeleted = 0
          AND b.IsDeleted = 0
          AND b.CompanyId = @CompanyId
    ),
    ActiveEmployees = (
        SELECT COUNT(*)
        FROM Employees e
        INNER JOIN Branches b ON e.BranchId = b.Id
        WHERE e.IsDeleted = 0
          AND e.IsActive = 1
          AND b.IsDeleted = 0
          AND b.CompanyId = @CompanyId
    ),
    Devices = (
        SELECT COUNT(*)
        FROM Devices d
        INNER JOIN Branches b ON d.BranchId = b.Id
        WHERE d.IsDeleted = 0
          AND b.IsDeleted = 0
          AND b.CompanyId = @CompanyId
    ),
    Shifts = (
        SELECT COUNT(*)
        FROM Shifts
        WHERE IsDeleted = 0
    ),
    AttendanceRecords = (
        SELECT COUNT(*)
        FROM AttendanceRecords a
        INNER JOIN Employees e ON a.EmployeeId = e.Id
        INNER JOIN Branches b ON e.BranchId = b.Id
        WHERE a.IsDeleted = 0
          AND e.IsDeleted = 0
          AND b.IsDeleted = 0
          AND b.CompanyId = @CompanyId
    ),
    TodayPresent = (
        SELECT COUNT(*)
        FROM AttendanceRecords a
        INNER JOIN Employees e ON a.EmployeeId = e.Id
        INNER JOIN Branches b ON e.BranchId = b.Id
        WHERE a.IsDeleted = 0
          AND e.IsDeleted = 0
          AND b.IsDeleted = 0
          AND b.CompanyId = @CompanyId
          AND a.AttendanceDate = @Today
          AND a.Status = 1
    ),
    TodayLate = (
        SELECT COUNT(*)
        FROM AttendanceRecords a
        INNER JOIN Employees e ON a.EmployeeId = e.Id
        INNER JOIN Branches b ON e.BranchId = b.Id
        WHERE a.IsDeleted = 0
          AND e.IsDeleted = 0
          AND b.IsDeleted = 0
          AND b.CompanyId = @CompanyId
          AND a.AttendanceDate = @Today
          AND a.Status = 2
    ),
    TodayAbsent = (
        SELECT COUNT(*)
        FROM AttendanceRecords a
        INNER JOIN Employees e ON a.EmployeeId = e.Id
        INNER JOIN Branches b ON e.BranchId = b.Id
        WHERE a.IsDeleted = 0
          AND e.IsDeleted = 0
          AND b.IsDeleted = 0
          AND b.CompanyId = @CompanyId
          AND a.AttendanceDate = @Today
          AND a.Status = 3
    ),
    TodayMissingOut = (
        SELECT COUNT(*)
        FROM AttendanceRecords a
        INNER JOIN Employees e ON a.EmployeeId = e.Id
        INNER JOIN Branches b ON e.BranchId = b.Id
        WHERE a.IsDeleted = 0
          AND e.IsDeleted = 0
          AND b.IsDeleted = 0
          AND b.CompanyId = @CompanyId
          AND a.AttendanceDate = @Today
          AND a.Status IN (1, 2)
          AND a.CheckIn IS NOT NULL
          AND a.CheckOut IS NULL
    ),
    EnginePresent = (
        SELECT COUNT(*)
        FROM DayAttendances d
        INNER JOIN Employees e ON d.EmployeeId = e.Id
        INNER JOIN Branches b ON e.BranchId = b.Id
        WHERE b.CompanyId = @CompanyId AND d.WorkDate = @Today
          AND d.IsAnalyzed = 1 AND d.Status = N'Present'
    ),
    EngineLate = (
        SELECT COUNT(*)
        FROM DayAttendances d
        INNER JOIN Employees e ON d.EmployeeId = e.Id
        INNER JOIN Branches b ON e.BranchId = b.Id
        WHERE b.CompanyId = @CompanyId AND d.WorkDate = @Today
          AND d.IsAnalyzed = 1 AND d.Status = N'Late'
    ),
    EngineAbsent = (
        SELECT COUNT(*)
        FROM DayAttendances d
        INNER JOIN Employees e ON d.EmployeeId = e.Id
        INNER JOIN Branches b ON e.BranchId = b.Id
        WHERE b.CompanyId = @CompanyId AND d.WorkDate = @Today
          AND d.IsAnalyzed = 1 AND d.Status = N'Absent'
    ),
    EngineIncomplete = (
        SELECT COUNT(*)
        FROM DayAttendances d
        INNER JOIN Employees e ON d.EmployeeId = e.Id
        INNER JOIN Branches b ON e.BranchId = b.Id
        WHERE b.CompanyId = @CompanyId AND d.WorkDate = @Today
          AND d.IsAnalyzed = 1 AND d.Status = N'Incomplete'
    ),
    EngineAnalyzedToday = (
        SELECT COUNT(*)
        FROM DayAttendances d
        INNER JOIN Employees e ON d.EmployeeId = e.Id
        INNER JOIN Branches b ON e.BranchId = b.Id
        WHERE b.CompanyId = @CompanyId AND d.WorkDate = @Today AND d.IsAnalyzed = 1
    ),
    PendingRequests = (
        SELECT COUNT(*)
        FROM SelfServiceRequests r
        INNER JOIN Employees e ON r.EmployeeId = e.Id
        INNER JOIN Branches b ON e.BranchId = b.Id
        WHERE e.IsDeleted = 0
          AND b.IsDeleted = 0
          AND b.CompanyId = @CompanyId
          AND r.Status = 'Pending'
    ),
    ApprovedRequests30 = (
        SELECT COUNT(*)
        FROM SelfServiceRequests r
        INNER JOIN Employees e ON r.EmployeeId = e.Id
        INNER JOIN Branches b ON e.BranchId = b.Id
        WHERE e.IsDeleted = 0
          AND b.IsDeleted = 0
          AND b.CompanyId = @CompanyId
          AND r.Status = 'Approved'
          AND r.CreatedAt >= @UtcFrom
    ),
    RejectedRequests30 = (
        SELECT COUNT(*)
        FROM SelfServiceRequests r
        INNER JOIN Employees e ON r.EmployeeId = e.Id
        INNER JOIN Branches b ON e.BranchId = b.Id
        WHERE e.IsDeleted = 0
          AND b.IsDeleted = 0
          AND b.CompanyId = @CompanyId
          AND r.Status = 'Rejected'
          AND r.CreatedAt >= @UtcFrom
    ),
    Contracts30 = (
        SELECT COUNT(*)
        FROM Employees e
        INNER JOIN Branches b ON e.BranchId = b.Id
        WHERE e.IsDeleted = 0
          AND e.IsActive = 1
          AND b.IsDeleted = 0
          AND b.CompanyId = @CompanyId
          AND e.ContractEndDate IS NOT NULL
          AND e.ContractEndDate >= @Today
          AND e.ContractEndDate <= DATEADD(day, 30, @Today)
    ),
    MissingPunches30 = (
        SELECT COUNT(*)
        FROM AttendanceRecords a
        INNER JOIN Employees e ON a.EmployeeId = e.Id
        INNER JOIN Branches b ON e.BranchId = b.Id
        WHERE a.IsDeleted = 0
          AND e.IsDeleted = 0
          AND b.IsDeleted = 0
          AND b.CompanyId = @CompanyId
          AND a.AttendanceDate >= @FromDate
          AND a.Status IN (1, 2)
          AND a.CheckIn IS NOT NULL
          AND a.CheckOut IS NULL
    ),
    Late30 = (
        SELECT COUNT(*)
        FROM AttendanceRecords a
        INNER JOIN Employees e ON a.EmployeeId = e.Id
        INNER JOIN Branches b ON e.BranchId = b.Id
        WHERE a.IsDeleted = 0
          AND e.IsDeleted = 0
          AND b.IsDeleted = 0
          AND b.CompanyId = @CompanyId
          AND a.AttendanceDate >= @FromDate
          AND a.Status = 2
    ),
    Absent30 = (
        SELECT COUNT(*)
        FROM AttendanceRecords a
        INNER JOIN Employees e ON a.EmployeeId = e.Id
        INNER JOIN Branches b ON e.BranchId = b.Id
        WHERE a.IsDeleted = 0
          AND e.IsDeleted = 0
          AND b.IsDeleted = 0
          AND b.CompanyId = @CompanyId
          AND a.AttendanceDate >= @FromDate
          AND a.Status = 3
    );

SELECT ISNULL(NULLIF(b.Name, ''), 'Not Set') AS Name, COUNT(*) AS Total
FROM Employees e
INNER JOIN Branches b ON e.BranchId = b.Id
WHERE e.IsDeleted = 0
  AND b.IsDeleted = 0
  AND b.CompanyId = @CompanyId
GROUP BY ISNULL(NULLIF(b.Name, ''), 'Not Set')
ORDER BY Total DESC, Name;

SELECT ISNULL(NULLIF(d.Name, ''), 'Not Set') AS Name, COUNT(*) AS Total
FROM Employees e
INNER JOIN Branches b ON e.BranchId = b.Id
LEFT JOIN Departments d
    ON e.DepartmentId = d.Id
   AND d.IsDeleted = 0
WHERE e.IsDeleted = 0
  AND b.IsDeleted = 0
  AND b.CompanyId = @CompanyId
GROUP BY ISNULL(NULLIF(d.Name, ''), 'Not Set')
ORDER BY Total DESC, Name;

SELECT ISNULL(NULLIF(e.Gender, ''), 'Not Set') AS Name, COUNT(*) AS Total
FROM Employees e
INNER JOIN Branches b ON e.BranchId = b.Id
WHERE e.IsDeleted = 0
  AND b.IsDeleted = 0
  AND b.CompanyId = @CompanyId
GROUP BY ISNULL(NULLIF(e.Gender, ''), 'Not Set')
ORDER BY Total DESC, Name;

SELECT ISNULL(NULLIF(e.Country, ''), 'Not Set') AS Name, COUNT(*) AS Total
FROM Employees e
INNER JOIN Branches b ON e.BranchId = b.Id
WHERE e.IsDeleted = 0
  AND b.IsDeleted = 0
  AND b.CompanyId = @CompanyId
GROUP BY ISNULL(NULLIF(e.Country, ''), 'Not Set')
ORDER BY Total DESC, Name;

SELECT ISNULL(NULLIF(e.Nationality, ''), 'Not Set') AS Name, COUNT(*) AS Total
FROM Employees e
INNER JOIN Branches b ON e.BranchId = b.Id
WHERE e.IsDeleted = 0
  AND b.IsDeleted = 0
  AND b.CompanyId = @CompanyId
GROUP BY ISNULL(NULLIF(e.Nationality, ''), 'Not Set')
ORDER BY Total DESC, Name;

SELECT ISNULL(NULLIF(e.ContractType, ''), 'Not Set') AS Name, COUNT(*) AS Total
FROM Employees e
INNER JOIN Branches b ON e.BranchId = b.Id
WHERE e.IsDeleted = 0
  AND b.IsDeleted = 0
  AND b.CompanyId = @CompanyId
GROUP BY ISNULL(NULLIF(e.ContractType, ''), 'Not Set')
ORDER BY Total DESC, Name;

SELECT ISNULL(NULLIF(r.Status, ''), 'Not Set') AS Name, COUNT(*) AS Total
FROM SelfServiceRequests r
INNER JOIN Employees e ON r.EmployeeId = e.Id
INNER JOIN Branches b ON e.BranchId = b.Id
WHERE e.IsDeleted = 0
  AND b.IsDeleted = 0
  AND b.CompanyId = @CompanyId
GROUP BY ISNULL(NULLIF(r.Status, ''), 'Not Set')
ORDER BY Total DESC, Name;

SELECT TOP 12
    e.EmployeeNo,
    e.FullName,
    ISNULL(e.Position, '') AS Position,
    e.ContractEndDate
FROM Employees e
INNER JOIN Branches b ON e.BranchId = b.Id
WHERE e.IsDeleted = 0
  AND e.IsActive = 1
  AND b.IsDeleted = 0
  AND b.CompanyId = @CompanyId
  AND e.ContractEndDate IS NOT NULL
  AND e.ContractEndDate >= @Today
  AND e.ContractEndDate <= DATEADD(day, 60, @Today)
ORDER BY e.ContractEndDate;

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
INNER JOIN Branches b ON e.BranchId = b.Id
WHERE e.IsDeleted = 0
  AND b.IsDeleted = 0
  AND b.CompanyId = @CompanyId
ORDER BY r.CreatedAt DESC;

-- قوائم اليوم بالأسماء (نمط كيان): نسختان محرك/خام، والاختيار بالكود حسب PulseFromEngine
SELECT TOP 8 e.EmployeeNo, e.FullName, d.CheckIn, d.LateHours
FROM DayAttendances d
INNER JOIN Employees e ON d.EmployeeId = e.Id
INNER JOIN Branches b ON e.BranchId = b.Id
WHERE b.CompanyId = @CompanyId AND d.WorkDate = @Today
  AND d.IsAnalyzed = 1 AND d.Status = N'Late'
ORDER BY d.LateHours DESC;

SELECT TOP 8 e.EmployeeNo, e.FullName, a.CheckIn, CAST(0 AS decimal(5,2)) AS LateHours
FROM AttendanceRecords a
INNER JOIN Employees e ON a.EmployeeId = e.Id
INNER JOIN Branches b ON e.BranchId = b.Id
WHERE a.IsDeleted = 0 AND e.IsDeleted = 0 AND b.IsDeleted = 0
  AND b.CompanyId = @CompanyId AND a.AttendanceDate = @Today AND a.Status = 2
ORDER BY a.CheckIn DESC;

SELECT TOP 8 e.EmployeeNo, e.FullName, d.CheckIn, CAST(0 AS decimal(5,2)) AS LateHours
FROM DayAttendances d
INNER JOIN Employees e ON d.EmployeeId = e.Id
INNER JOIN Branches b ON e.BranchId = b.Id
WHERE b.CompanyId = @CompanyId AND d.WorkDate = @Today
  AND d.IsAnalyzed = 1 AND d.Status = N'Incomplete'
ORDER BY d.CheckIn;

SELECT TOP 8 e.EmployeeNo, e.FullName, a.CheckIn, CAST(0 AS decimal(5,2)) AS LateHours
FROM AttendanceRecords a
INNER JOIN Employees e ON a.EmployeeId = e.Id
INNER JOIN Branches b ON e.BranchId = b.Id
WHERE a.IsDeleted = 0 AND e.IsDeleted = 0 AND b.IsDeleted = 0
  AND b.CompanyId = @CompanyId AND a.AttendanceDate = @Today
  AND a.Status IN (1, 2) AND a.CheckIn IS NOT NULL AND a.CheckOut IS NULL
ORDER BY a.CheckIn;
""";

        var connection = _dbContext.Database.GetDbConnection();
        var shouldClose = connection.State != ConnectionState.Open;

        if (shouldClose)
        {
            await connection.OpenAsync();
        }

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.Transaction = _dbContext.Database
                .CurrentTransaction?
                .GetDbTransaction();

            HrmsDatabase.AddParameter(
                command,
                "@CompanyId",
                CompanyId!.Value);

            await using var reader = await command.ExecuteReaderAsync();

            if (await reader.ReadAsync())
            {
                Companies = HrmsDatabase.GetInt(reader, "Companies");
                Branches = HrmsDatabase.GetInt(reader, "Branches");
                Departments = HrmsDatabase.GetInt(reader, "Departments");
                Employees = HrmsDatabase.GetInt(reader, "Employees");
                ActiveEmployees = HrmsDatabase.GetInt(reader, "ActiveEmployees");
                InactiveEmployees = Employees - ActiveEmployees;
                Devices = HrmsDatabase.GetInt(reader, "Devices");
                Shifts = HrmsDatabase.GetInt(reader, "Shifts");
                AttendanceRecords = HrmsDatabase.GetInt(reader, "AttendanceRecords");
                TodayPresent = HrmsDatabase.GetInt(reader, "TodayPresent");
                TodayLate = HrmsDatabase.GetInt(reader, "TodayLate");
                TodayAbsent = HrmsDatabase.GetInt(reader, "TodayAbsent");
                TodayMissingOut = HrmsDatabase.GetInt(reader, "TodayMissingOut");

                // إن كان يوم اليوم محللاً بمحرك الحضور الجديد (DayAttendances) فأرقامه
                // المشتقة أدق من سجلات AttendanceRecords الخام — نعرضها بدلاً منها.
                PulseFromEngine = HrmsDatabase.GetInt(reader, "EngineAnalyzedToday") > 0;
                if (PulseFromEngine)
                {
                    TodayPresent = HrmsDatabase.GetInt(reader, "EnginePresent");
                    TodayLate = HrmsDatabase.GetInt(reader, "EngineLate");
                    TodayAbsent = HrmsDatabase.GetInt(reader, "EngineAbsent");
                    TodayMissingOut = HrmsDatabase.GetInt(reader, "EngineIncomplete");
                }
                PendingRequests = HrmsDatabase.GetInt(reader, "PendingRequests");
                ApprovedRequests30 = HrmsDatabase.GetInt(reader, "ApprovedRequests30");
                RejectedRequests30 = HrmsDatabase.GetInt(reader, "RejectedRequests30");
                Contracts30 = HrmsDatabase.GetInt(reader, "Contracts30");
                MissingPunches30 = HrmsDatabase.GetInt(reader, "MissingPunches30");
                Late30 = HrmsDatabase.GetInt(reader, "Late30");
                Absent30 = HrmsDatabase.GetInt(reader, "Absent30");
            }

            await reader.NextResultAsync();
            ByBranch = NormalizeDistributionRows(
                await ReadNameCountRowsAsync(reader),
                limitToTopRows: true);

            await reader.NextResultAsync();
            ByDepartment = NormalizeDistributionRows(
                await ReadNameCountRowsAsync(reader),
                limitToTopRows: true);

            await reader.NextResultAsync();
            ByGender = NormalizeDistributionRows(
                await ReadNameCountRowsAsync(reader),
                limitToTopRows: true);

            await reader.NextResultAsync();
            ByCountry = NormalizeDistributionRows(
                await ReadNameCountRowsAsync(reader),
                limitToTopRows: true);

            await reader.NextResultAsync();
            ByNationality = NormalizeDistributionRows(
                await ReadNameCountRowsAsync(reader),
                limitToTopRows: true);

            await reader.NextResultAsync();
            ByContractType = NormalizeDistributionRows(
                await ReadNameCountRowsAsync(reader),
                limitToTopRows: true);

            await reader.NextResultAsync();
            RequestsByStatus = NormalizeDistributionRows(
                await ReadNameCountRowsAsync(reader),
                limitToTopRows: false);

            await reader.NextResultAsync();
            ExpiringContracts = await ReadContractRowsAsync(reader);

            await reader.NextResultAsync();
            LatestRequests = await ReadRequestRowsAsync(reader);

            // قوائم اليوم بالأسماء: المصدر الأدق أولاً (محرك الحضور) وإلا الخام
            await reader.NextResultAsync();
            var engineLate = await ReadTodayEmployeeRowsAsync(reader);
            await reader.NextResultAsync();
            var rawLate = await ReadTodayEmployeeRowsAsync(reader);
            await reader.NextResultAsync();
            var engineMissing = await ReadTodayEmployeeRowsAsync(reader);
            await reader.NextResultAsync();
            var rawMissing = await ReadTodayEmployeeRowsAsync(reader);

            TodayLateList = PulseFromEngine ? engineLate : rawLate;
            TodayMissingOutList = PulseFromEngine ? engineMissing : rawMissing;
        }
        finally
        {
            if (shouldClose &&
                _dbContext.Database.CurrentTransaction == null)
            {
                await connection.CloseAsync();
            }
        }
    }

    private static async Task<List<NameCountRow>>
        ReadNameCountRowsAsync(DbDataReader reader)
    {
        var rows = new List<NameCountRow>();

        while (await reader.ReadAsync())
        {
            rows.Add(new NameCountRow
            {
                Name = HrmsDatabase.GetString(reader, "Name"),
                Total = HrmsDatabase.GetInt(reader, "Total")
            });
        }

        return rows;
    }

    private static async Task<List<TodayEmployeeRow>>
        ReadTodayEmployeeRowsAsync(DbDataReader reader)
    {
        var rows = new List<TodayEmployeeRow>();

        while (await reader.ReadAsync())
        {
            rows.Add(new TodayEmployeeRow
            {
                EmployeeNo = HrmsDatabase.GetString(reader, "EmployeeNo"),
                EmployeeName = HrmsDatabase.GetString(reader, "FullName"),
                CheckIn = HrmsDatabase.GetDateTime(reader, "CheckIn"),
                LateHours = reader["LateHours"] is decimal late ? late : 0
            });
        }

        return rows;
    }

    private static async Task<List<ContractRow>>
        ReadContractRowsAsync(DbDataReader reader)
    {
        var rows = new List<ContractRow>();

        while (await reader.ReadAsync())
        {
            rows.Add(new ContractRow
            {
                EmployeeNo = HrmsDatabase.GetString(reader, "EmployeeNo"),
                EmployeeName = HrmsDatabase.GetString(reader, "FullName"),
                Position = HrmsDatabase.GetString(reader, "Position"),
                ContractEndDate = HrmsDatabase.GetDateOnly(
                    reader,
                    "ContractEndDate")
            });
        }

        return rows;
    }

    private static async Task<List<RequestRow>>
        ReadRequestRowsAsync(DbDataReader reader)
    {
        var rows = new List<RequestRow>();

        while (await reader.ReadAsync())
        {
            rows.Add(new RequestRow
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

        return rows;
    }

    private static List<NameCountRow> NormalizeDistributionRows(
        IEnumerable<NameCountRow> rows,
        bool limitToTopRows)
    {
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

        if (!limitToTopRows ||
            normalizedRows.Count <= DistributionTopLimit)
        {
            return normalizedRows;
        }

        var topRows = normalizedRows
            .Take(DistributionTopLimit)
            .ToList();
        var otherTotal = normalizedRows
            .Skip(DistributionTopLimit)
            .Sum(row => row.Total);

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

    private static string NormalizeDistributionName(
        string? name)
    {
        return string.IsNullOrWhiteSpace(name) ||
               name.Trim().Equals(
                   "Not Set",
                   StringComparison.OrdinalIgnoreCase)
            ? "غير محدد"
            : name.Trim();
    }

    public class CompanyOption
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public bool IsActive { get; set; }

        /// <summary>لترتيب الشركات: الشركة ذات الموظفين تتصدر وتصير الافتراضية.</summary>
        public int EmployeeCount { get; set; }
    }

    public class NameCountRow
    {
        public string Name { get; set; } = string.Empty;
        public int Total { get; set; }
    }

    public class TodayEmployeeRow
    {
        public string EmployeeNo { get; set; } = string.Empty;
        public string EmployeeName { get; set; } = string.Empty;
        public DateTime? CheckIn { get; set; }
        public decimal LateHours { get; set; }
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
