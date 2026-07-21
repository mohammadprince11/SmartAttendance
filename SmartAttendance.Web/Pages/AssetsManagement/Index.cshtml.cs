using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SmartAttendance.Domain.Enums;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;

namespace SmartAttendance.Web.Pages.AssetsManagement;

/// <summary>
/// إدارة العهد المركزية (نمط كيان قسم 17.5): سجل عهد كل الموظفين فوق
/// EmployeeFileRecords (نوع Asset) مع فلاتر وتتبع الإرجاع. التحرير التفصيلي
/// يبقى ببطاقة العهد بملف الموظف؛ هنا عرض شامل + إجراء «تم الإرجاع» السريع.
/// </summary>
public class IndexModel : PageModel
{
    private readonly ApplicationDbContext _dbContext;

    public IndexModel(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public sealed class AssetRow
    {
        public int Id { get; set; }
        public int EmployeeId { get; set; }
        public string EmployeeNo { get; set; } = string.Empty;
        public string EmployeeName { get; set; } = string.Empty;
        public string DepartmentName { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string? Subtitle { get; set; }
        public DateOnly? FromDate { get; set; }
        public DateOnly? ToDate { get; set; }
        public decimal? Amount { get; set; }
        public bool IsCurrent { get; set; }
        public bool IsReturned { get; set; }
        public DateOnly? ReturnDate { get; set; }
        public string? AttachmentPath { get; set; }
        public bool EmployeeAcknowledged { get; set; }
        public DateTime? AcknowledgedAt { get; set; }
    }

    [BindProperty(SupportsGet = true)] public string? Search { get; set; }
    [BindProperty(SupportsGet = true)] public string Status { get; set; } = "held"; // held | returned | all

    [TempData] public string? Message { get; set; }

    public List<AssetRow> Rows { get; set; } = new();
    public int HeldCount { get; set; }
    public int ReturnedCount { get; set; }
    public decimal HeldValueTotal { get; set; }

    public async Task OnGetAsync()
    {
        await EmployeeRecordsSchema.EnsureAsync(_dbContext);

        var query = _dbContext.EmployeeFileRecords.AsNoTracking()
            .Where(r => r.RecordType == EmployeeRecordType.Asset && !r.Employee.IsDeleted);

        if (!string.IsNullOrWhiteSpace(Search))
        {
            var s = Search.Trim();
            query = query.Where(r =>
                r.Title.Contains(s) ||
                r.Employee.FullName.Contains(s) ||
                r.Employee.EmployeeNo.Contains(s));
        }

        var all = await query
            .OrderByDescending(r => r.Id)
            .Select(r => new AssetRow
            {
                Id = r.Id,
                EmployeeId = r.EmployeeId,
                EmployeeNo = r.Employee.EmployeeNo,
                EmployeeName = r.Employee.FullName,
                DepartmentName = r.Employee.Department.Name,
                Title = r.Title,
                Subtitle = r.Subtitle,
                FromDate = r.FromDate,
                ToDate = r.ToDate,
                Amount = r.Amount,
                IsCurrent = r.IsCurrent,
                IsReturned = r.IsReturned,
                ReturnDate = r.ReturnDate,
                AttachmentPath = r.AttachmentPath,
                EmployeeAcknowledged = r.EmployeeAcknowledged,
                AcknowledgedAt = r.AcknowledgedAt
            })
            .ToListAsync();

        HeldCount = all.Count(r => !r.IsReturned);
        ReturnedCount = all.Count(r => r.IsReturned);
        HeldValueTotal = all.Where(r => !r.IsReturned).Sum(r => r.Amount ?? 0);

        Rows = Status switch
        {
            "returned" => all.Where(r => r.IsReturned).ToList(),
            "all" => all,
            _ => all.Where(r => !r.IsReturned).ToList()
        };
    }

    /// <summary>إجراء سريع: تعليم العهدة مُرجَعة بتاريخ اليوم.</summary>
    public async Task<IActionResult> OnPostMarkReturnedAsync(int recordId)
    {
        var record = await _dbContext.EmployeeFileRecords
            .FirstOrDefaultAsync(r => r.Id == recordId && r.RecordType == EmployeeRecordType.Asset);
        if (record != null)
        {
            record.IsReturned = true;
            record.ReturnDate = DateOnly.FromDateTime(DateTime.Today);
            record.IsCurrent = false;
            record.UpdatedAt = DateTime.UtcNow;
            record.UpdatedBy = User.Identity?.Name ?? "System";
            await _dbContext.SaveChangesAsync();
            Message = "تم تعليم العهدة كمُرجَعة.";
        }
        return RedirectToPage(null, null, new { Search, Status });
    }
}
