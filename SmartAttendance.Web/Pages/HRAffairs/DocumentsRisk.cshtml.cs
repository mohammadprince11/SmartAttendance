using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;

namespace SmartAttendance.Web.Pages.HRAffairs;

public class DocumentsRiskModel : PageModel
{
    private readonly ApplicationDbContext _dbContext;

    public DocumentsRiskModel(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [BindProperty(SupportsGet = true)]
    public string? SearchTerm { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? RiskType { get; set; } = "all";

    [BindProperty(SupportsGet = true)]
    public int MaxRows { get; set; } = 200;

    public List<DocumentRiskRow> Rows { get; set; } = new();

    public int ExpiredCount => Rows.Count(x => x.RiskClass == "danger");
    public int ExpiringCount => Rows.Count(x => x.RiskClass == "warn");
    public int TotalRows => Rows.Count;

    public async Task OnGetAsync()
    {
        await HrmsDatabase.EnsureCreatedAsync(_dbContext);

        MaxRows = MaxRows <= 0 ? 200 : Math.Min(MaxRows, 1000);
        var normalizedRisk = string.IsNullOrWhiteSpace(RiskType) ? "all" : RiskType.Trim().ToLowerInvariant();

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
    ed.DocumentType,
    ed.FileName,
    ed.ExpiryDate,
    ISNULL(ed.Notes, '') AS Notes,
    ed.UploadedAt
FROM EmployeeDocuments ed
INNER JOIN Employees e ON ed.EmployeeId = e.Id
LEFT JOIN Departments d ON e.DepartmentId = d.Id
LEFT JOIN Branches b ON d.BranchId = b.Id
WHERE ed.ExpiryDate IS NOT NULL
  AND
  (
      @RiskType = 'all'
      OR (@RiskType = 'expired' AND ed.ExpiryDate < CAST(GETDATE() AS date))
      OR (@RiskType = 'expiring' AND ed.ExpiryDate >= CAST(GETDATE() AS date) AND ed.ExpiryDate <= DATEADD(day, 30, CAST(GETDATE() AS date)))
  )
  AND
  (
      @SearchTerm = ''
      OR e.EmployeeNo LIKE '%' + @SearchTerm + '%'
      OR e.FullName LIKE '%' + @SearchTerm + '%'
      OR ed.DocumentType LIKE '%' + @SearchTerm + '%'
      OR ISNULL(d.Name, '') LIKE '%' + @SearchTerm + '%'
      OR ISNULL(b.Name, '') LIKE '%' + @SearchTerm + '%'
  )
ORDER BY ed.ExpiryDate ASC, e.EmployeeNo;
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@RiskType", normalizedRisk);
                HrmsDatabase.AddParameter(command, "@SearchTerm", string.IsNullOrWhiteSpace(SearchTerm) ? "" : SearchTerm.Trim());
                HrmsDatabase.AddParameter(command, "@MaxRows", MaxRows);
            },
            reader => new DocumentRiskRow
            {
                EmployeeId = HrmsDatabase.GetInt(reader, "EmployeeId"),
                EmployeeNo = HrmsDatabase.GetString(reader, "EmployeeNo"),
                FullName = HrmsDatabase.GetString(reader, "FullName"),
                Position = HrmsDatabase.GetString(reader, "Position"),
                DepartmentName = HrmsDatabase.GetString(reader, "DepartmentName"),
                BranchName = HrmsDatabase.GetString(reader, "BranchName"),
                DocumentType = HrmsDatabase.GetString(reader, "DocumentType"),
                FileName = HrmsDatabase.GetString(reader, "FileName"),
                ExpiryDate = HrmsDatabase.GetDateOnly(reader, "ExpiryDate"),
                Notes = HrmsDatabase.GetString(reader, "Notes"),
                UploadedAt = HrmsDatabase.GetDateTime(reader, "UploadedAt")
            });
    }

    public string DisplayDate(DateOnly? value) => value?.ToString("yyyy-MM-dd") ?? "-";

    public class DocumentRiskRow
    {
        public int EmployeeId { get; set; }
        public string EmployeeNo { get; set; } = "";
        public string FullName { get; set; } = "";
        public string Position { get; set; } = "";
        public string DepartmentName { get; set; } = "";
        public string BranchName { get; set; } = "";
        public string DocumentType { get; set; } = "";
        public string FileName { get; set; } = "";
        public DateOnly? ExpiryDate { get; set; }
        public string Notes { get; set; } = "";
        public DateTime? UploadedAt { get; set; }

        public int DaysLeft => ExpiryDate.HasValue
            ? ExpiryDate.Value.DayNumber - DateOnly.FromDateTime(DateTime.Today).DayNumber
            : 9999;

        public string RiskClass => DaysLeft < 0 ? "danger" : DaysLeft <= 30 ? "warn" : "ok";
        public string RiskText => DaysLeft < 0 ? "منتهي" : DaysLeft <= 30 ? "ينتهي قريباً" : "سليم";
    }
}
