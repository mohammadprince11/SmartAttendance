using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;

namespace SmartAttendance.Web.Pages.Alerts;

public class IndexModel : PageModel
{
    private readonly ApplicationDbContext _dbContext;

    public IndexModel(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [BindProperty(SupportsGet = true)]
    public int Days { get; set; } = 30;

    public List<ContractAlertRow> ContractAlerts { get; set; } = new();

    public List<DocumentAlertRow> DocumentAlerts { get; set; } = new();

    public int ExpiredContractsCount => ContractAlerts.Count(a => a.DaysLeft < 0);

    public int ExpiringContractsCount => ContractAlerts.Count(a => a.DaysLeft >= 0);

    public int ExpiredDocumentsCount => DocumentAlerts.Count(a => a.DaysLeft < 0);

    public int ExpiringDocumentsCount => DocumentAlerts.Count(a => a.DaysLeft >= 0);

    public async Task OnGetAsync()
    {
        await HrmsDatabase.EnsureCreatedAsync(_dbContext);

        Days = Math.Clamp(Days, 1, 365);

        var today = DateOnly.FromDateTime(DateTime.Today);
        var limit = today.AddDays(Days);

        await LoadContractAlertsAsync(today, limit);
        await LoadDocumentAlertsAsync(limit);
    }

    private async Task LoadContractAlertsAsync(DateOnly today, DateOnly limit)
    {
        ContractAlerts = await _dbContext.Employees
            .AsNoTracking()
            .Where(e => e.IsActive && e.ContractEndDate != null && e.ContractEndDate <= limit)
            .OrderBy(e => e.ContractEndDate)
            .Select(e => new ContractAlertRow
            {
                EmployeeId = e.Id,
                EmployeeNo = e.EmployeeNo,
                FullName = e.FullName,
                DepartmentName = e.Department.Name,
                BranchName = e.Branch.Name,
                ContractType = e.ContractType ?? string.Empty,
                ContractEndDate = e.ContractEndDate!.Value
            })
            .ToListAsync();

        foreach (var alert in ContractAlerts)
        {
            alert.DaysLeft = alert.ContractEndDate.DayNumber - today.DayNumber;
        }
    }

    private async Task LoadDocumentAlertsAsync(DateOnly limit)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);

        DocumentAlerts = await HrmsDatabase.QueryAsync(
            _dbContext,
            """
SELECT
    x.Id,
    x.EmployeeId,
    e.EmployeeNo,
    e.FullName,
    x.DocumentType,
    x.FileName,
    x.ExpiryDate
FROM
(
    SELECT
        d.Id,
        d.EmployeeId,
        d.DocumentType,
        d.FileName,
        d.ExpiryDate,
        ROW_NUMBER() OVER (
            PARTITION BY d.EmployeeId, d.DocumentType
            ORDER BY d.UploadedAt DESC, d.Id DESC) AS RowNo
    FROM EmployeeDocuments d
) x
INNER JOIN Employees e ON x.EmployeeId = e.Id
WHERE x.RowNo = 1
  AND e.IsActive = 1
  AND x.ExpiryDate IS NOT NULL
  AND x.ExpiryDate <= @Limit
ORDER BY x.ExpiryDate;
""",
            command => HrmsDatabase.AddParameter(command, "@Limit", limit),
            reader => new DocumentAlertRow
            {
                DocumentId = HrmsDatabase.GetInt(reader, "Id"),
                EmployeeId = HrmsDatabase.GetInt(reader, "EmployeeId"),
                EmployeeNo = HrmsDatabase.GetString(reader, "EmployeeNo"),
                FullName = HrmsDatabase.GetString(reader, "FullName"),
                DocumentType = HrmsDatabase.GetString(reader, "DocumentType"),
                FileName = HrmsDatabase.GetString(reader, "FileName"),
                ExpiryDate = HrmsDatabase.GetDateOnly(reader, "ExpiryDate") ?? today
            });

        foreach (var alert in DocumentAlerts)
        {
            alert.DaysLeft = alert.ExpiryDate.DayNumber - today.DayNumber;
        }
    }

    public string StatusText(int daysLeft)
    {
        if (daysLeft < 0)
        {
            return "منتهي";
        }

        return daysLeft == 0 ? "ينتهي اليوم" : $"باقي {daysLeft} يوم";
    }

    public string StatusClass(int daysLeft)
    {
        if (daysLeft < 0)
        {
            return "rejected";
        }

        return daysLeft <= 7 ? "rejected" : "pending";
    }

    public string DocumentTypeText(string? type)
    {
        return type switch
        {
            "ID" => "هوية / بطاقة وطنية",
            "Contract" => "عقد عمل",
            "Passport" => "جواز سفر",
            "Visa" => "إقامة / فيزا",
            "Health Card" => "بطاقة صحية",
            "Certificate" => "شهادة / مؤهل",
            "Memo" => "كتاب / مذكرة",
            "Other" => "أخرى",
            _ => string.IsNullOrWhiteSpace(type) ? "غير محدد" : type
        };
    }

    public class ContractAlertRow
    {
        public int EmployeeId { get; set; }

        public string EmployeeNo { get; set; } = string.Empty;

        public string FullName { get; set; } = string.Empty;

        public string DepartmentName { get; set; } = string.Empty;

        public string BranchName { get; set; } = string.Empty;

        public string ContractType { get; set; } = string.Empty;

        public DateOnly ContractEndDate { get; set; }

        public int DaysLeft { get; set; }
    }

    public class DocumentAlertRow
    {
        public int DocumentId { get; set; }

        public int EmployeeId { get; set; }

        public string EmployeeNo { get; set; } = string.Empty;

        public string FullName { get; set; } = string.Empty;

        public string DocumentType { get; set; } = string.Empty;

        public string FileName { get; set; } = string.Empty;

        public DateOnly ExpiryDate { get; set; }

        public int DaysLeft { get; set; }
    }
}
