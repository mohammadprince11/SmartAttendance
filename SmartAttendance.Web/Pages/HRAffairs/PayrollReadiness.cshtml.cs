using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;

namespace SmartAttendance.Web.Pages.HRAffairs;

public class PayrollReadinessModel : PageModel
{
    private readonly ApplicationDbContext _dbContext;

    public PayrollReadinessModel(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [BindProperty(SupportsGet = true)]
    public DateOnly? FromDate { get; set; }

    [BindProperty(SupportsGet = true)]
    public DateOnly? ToDate { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? SearchTerm { get; set; }

    [BindProperty(SupportsGet = true)]
    public int MaxRows { get; set; } = 200;

    public List<PayrollReadinessRow> Rows { get; set; } = new();

    public int TotalRows => Rows.Count;
    public int ReadyRows => Rows.Count(x => x.RiskScore == 0);
    public int RiskRows => Rows.Count(x => x.RiskScore > 0);

    public async Task OnGetAsync()
    {
        await HrmsDatabase.EnsureCreatedAsync(_dbContext);

        FromDate ??= DateOnly.FromDateTime(DateTime.Today.AddDays(-30));
        ToDate ??= DateOnly.FromDateTime(DateTime.Today);
        MaxRows = MaxRows <= 0 ? 200 : Math.Min(MaxRows, 1000);

        Rows = await HrmsDatabase.QueryAsync(
            _dbContext,
            """
SELECT TOP (@MaxRows)
    e.Id AS EmployeeId,
    e.EmployeeNo,
    e.FullName,
    ISNULL(e.Position, '') AS Position,
    ISNULL(d.Name, '') AS DepartmentName,
    ISNULL(b.Name, '') AS BranchName,
    ISNULL(ar.AbsentCount, 0) AS AbsentCount,
    ISNULL(ar.LateCount, 0) AS LateCount,
    ISNULL(ar.MissingPunchCount, 0) AS MissingPunchCount,
    ISNULL(req.PendingRequests, 0) AS PendingRequests,
    ISNULL(doc.ExpiredDocuments, 0) AS ExpiredDocuments,
    ISNULL(doc.ExpiringDocuments, 0) AS ExpiringDocuments
FROM Employees e
LEFT JOIN Departments d ON e.DepartmentId = d.Id
LEFT JOIN Branches b ON d.BranchId = b.Id
OUTER APPLY
(
    SELECT
        SUM(CASE WHEN Status = 3 THEN 1 ELSE 0 END) AS AbsentCount,
        SUM(CASE WHEN Status = 2 THEN 1 ELSE 0 END) AS LateCount,
        SUM(CASE WHEN CheckIn IS NULL OR CheckOut IS NULL THEN 1 ELSE 0 END) AS MissingPunchCount
    FROM AttendanceRecords ar
    WHERE ar.EmployeeId = e.Id
      AND ar.AttendanceDate BETWEEN @FromDate AND @ToDate
) ar
OUTER APPLY
(
    SELECT COUNT(*) AS PendingRequests
    FROM SelfServiceRequests r
    WHERE r.EmployeeId = e.Id
      AND r.Status = 'Pending'
) req
OUTER APPLY
(
    SELECT
        SUM(CASE WHEN ExpiryDate IS NOT NULL AND ExpiryDate < CAST(GETDATE() AS date) THEN 1 ELSE 0 END) AS ExpiredDocuments,
        SUM(CASE WHEN ExpiryDate IS NOT NULL AND ExpiryDate >= CAST(GETDATE() AS date) AND ExpiryDate <= DATEADD(day, 30, CAST(GETDATE() AS date)) THEN 1 ELSE 0 END) AS ExpiringDocuments
    FROM EmployeeDocuments ed
    WHERE ed.EmployeeId = e.Id
) doc
WHERE e.IsActive = 1
  AND
  (
      @SearchTerm = ''
      OR e.EmployeeNo LIKE '%' + @SearchTerm + '%'
      OR e.FullName LIKE '%' + @SearchTerm + '%'
      OR ISNULL(e.Position, '') LIKE '%' + @SearchTerm + '%'
      OR ISNULL(d.Name, '') LIKE '%' + @SearchTerm + '%'
      OR ISNULL(b.Name, '') LIKE '%' + @SearchTerm + '%'
  )
ORDER BY
    (ISNULL(ar.AbsentCount, 0) + ISNULL(ar.MissingPunchCount, 0) + ISNULL(req.PendingRequests, 0) + ISNULL(doc.ExpiredDocuments, 0) + ISNULL(doc.ExpiringDocuments, 0)) DESC,
    e.EmployeeNo;
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@FromDate", FromDate.Value.ToDateTime(TimeOnly.MinValue));
                HrmsDatabase.AddParameter(command, "@ToDate", ToDate.Value.ToDateTime(TimeOnly.MinValue));
                HrmsDatabase.AddParameter(command, "@SearchTerm", string.IsNullOrWhiteSpace(SearchTerm) ? "" : SearchTerm.Trim());
                HrmsDatabase.AddParameter(command, "@MaxRows", MaxRows);
            },
            reader => new PayrollReadinessRow
            {
                EmployeeId = HrmsDatabase.GetInt(reader, "EmployeeId"),
                EmployeeNo = HrmsDatabase.GetString(reader, "EmployeeNo"),
                FullName = HrmsDatabase.GetString(reader, "FullName"),
                Position = HrmsDatabase.GetString(reader, "Position"),
                DepartmentName = HrmsDatabase.GetString(reader, "DepartmentName"),
                BranchName = HrmsDatabase.GetString(reader, "BranchName"),
                AbsentCount = HrmsDatabase.GetInt(reader, "AbsentCount"),
                LateCount = HrmsDatabase.GetInt(reader, "LateCount"),
                MissingPunchCount = HrmsDatabase.GetInt(reader, "MissingPunchCount"),
                PendingRequests = HrmsDatabase.GetInt(reader, "PendingRequests"),
                ExpiredDocuments = HrmsDatabase.GetInt(reader, "ExpiredDocuments"),
                ExpiringDocuments = HrmsDatabase.GetInt(reader, "ExpiringDocuments")
            });
    }

    public class PayrollReadinessRow
    {
        public int EmployeeId { get; set; }
        public string EmployeeNo { get; set; } = "";
        public string FullName { get; set; } = "";
        public string Position { get; set; } = "";
        public string DepartmentName { get; set; } = "";
        public string BranchName { get; set; } = "";
        public int AbsentCount { get; set; }
        public int LateCount { get; set; }
        public int MissingPunchCount { get; set; }
        public int PendingRequests { get; set; }
        public int ExpiredDocuments { get; set; }
        public int ExpiringDocuments { get; set; }
        public int RiskScore => AbsentCount + MissingPunchCount + PendingRequests + ExpiredDocuments + ExpiringDocuments;
        public string RiskClass => RiskScore == 0 ? "ok" : RiskScore <= 3 ? "warn" : "danger";
        public string RiskText => RiskScore == 0 ? "جاهز" : RiskScore <= 3 ? "مراجعة" : "خطر";
    }
}
