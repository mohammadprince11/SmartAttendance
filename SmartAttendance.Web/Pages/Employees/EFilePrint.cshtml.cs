using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SmartAttendance.Domain.Enums;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;
using SmartAttendance.Web.Infrastructure.Security;

namespace SmartAttendance.Web.Pages.Employees;

/// <summary>
/// إصدار الملف الإلكتروني للموظف مستنداً واحداً (نظير «خيارات إضافية ← إصدار
/// بصيغة PDF» بكيان): صفحة بلا قوقعة تُطبع من المتصفح حفظاً كـPDF.
///
/// المحتوى إداريّ فقط — بيانات التعريف والسجلات وفهرس الوثائق. لا ماليات هنا:
/// الملف الإلكتروني عند كيان لا يشمل الراتب، وحقولنا الحساسة محكومة بشاشتها.
/// </summary>
public class EFilePrintModel : PageModel
{
    private readonly ApplicationDbContext _dbContext;
    private readonly ICompanyScopeProvider _companyScope;

    public EFilePrintModel(ApplicationDbContext dbContext, ICompanyScopeProvider companyScope)
    {
        _dbContext = dbContext;
        _companyScope = companyScope;
    }

    public sealed record RecordLine(string Title, string? Subtitle, DateOnly? FromDate, DateOnly? ToDate, decimal? Amount, string? Note);

    public sealed record DocumentLine(string DocumentType, string FileName, DateTime UploadedAt, DateOnly? ExpiryDate);

    public string EmployeeNo { get; private set; } = string.Empty;
    public string FullName { get; private set; } = string.Empty;
    public string? Position { get; private set; }
    public string? BranchName { get; private set; }
    public string? DepartmentName { get; private set; }
    public string? Nationality { get; private set; }
    public string? NationalId { get; private set; }
    public DateOnly HireDate { get; private set; }
    public DateOnly? BirthDate { get; private set; }
    public string? Email { get; private set; }
    public string? Phone { get; private set; }

    /// <summary>أقسام السجلات بترتيب عرضٍ ثابت: (عنوان القسم، سطوره).</summary>
    public List<(string Section, List<RecordLine> Lines)> RecordSections { get; } = new();

    public List<DocumentLine> Documents { get; private set; } = new();

    public DateTime GeneratedAt { get; } = DateTime.Now;
    public string GeneratedBy { get; private set; } = string.Empty;

    private static readonly (EmployeeRecordType Type, string Title)[] SectionOrder =
    {
        (EmployeeRecordType.Education, "التعليم"),
        (EmployeeRecordType.Experience, "الخبرات"),
        (EmployeeRecordType.Certificate, "الشهادات"),
        (EmployeeRecordType.Training, "الدورات التدريبية"),
        (EmployeeRecordType.Medical, "الملفات الطبية"),
        (EmployeeRecordType.Asset, "العهد"),
        (EmployeeRecordType.Address, "العناوين"),
        (EmployeeRecordType.EmergencyContact, "جهات اتصال الطوارئ"),
        (EmployeeRecordType.Residency, "الإقامة"),
    };

    public async Task<IActionResult> OnGetAsync(int id)
    {
        // نطاق الشركة أولاً: طلب ملفِ موظفٍ خارج شركاتي = غير موجود، لا «ممنوع».
        var scope = await _companyScope.GetAsync(HttpContext.RequestAborted);
        if (!scope.IsUnrestricted)
        {
            var allowed = await EmployeeCompanyGuard.FilterEmployeesInScopeAsync(
                _dbContext, new[] { id }, scope, HttpContext.RequestAborted);
            if (!allowed.Contains(id))
                return NotFound();
        }

        var employee = await _dbContext.Employees.AsNoTracking()
            .Where(x => x.Id == id && !x.IsDeleted)
            .Select(x => new
            {
                x.EmployeeNo,
                x.FullName,
                x.Position,
                Branch = x.Branch != null ? x.Branch.Name : null,
                Department = x.Department != null ? x.Department.Name : null,
                x.Nationality,
                x.NationalId,
                x.HireDate,
                x.BirthDate,
                x.Email,
                x.Phone
            })
            .FirstOrDefaultAsync();

        if (employee is null)
            return NotFound();

        EmployeeNo = employee.EmployeeNo;
        FullName = employee.FullName;
        Position = employee.Position;
        BranchName = employee.Branch;
        DepartmentName = employee.Department;
        Nationality = employee.Nationality;
        NationalId = employee.NationalId;
        HireDate = employee.HireDate;
        BirthDate = employee.BirthDate;
        Email = employee.Email;
        Phone = employee.Phone;
        GeneratedBy = User.Identity?.Name ?? "System";

        var records = await _dbContext.EmployeeFileRecords.AsNoTracking()
            .Where(r => r.EmployeeId == id)
            .OrderByDescending(r => r.Id)
            .Select(r => new { r.RecordType, r.Title, r.Subtitle, r.FromDate, r.ToDate, r.Amount, r.Note })
            .ToListAsync();

        foreach (var (type, title) in SectionOrder)
        {
            var lines = records
                .Where(r => r.RecordType == type)
                .Select(r => new RecordLine(r.Title, r.Subtitle, r.FromDate, r.ToDate, r.Amount, r.Note))
                .ToList();
            if (lines.Count > 0)
                RecordSections.Add((title, lines));
        }

        // فهرس الوثائق بالأسماء فقط — الملفات نفسها مشفّرة بالقرص ولا تُضمَّن.
        Documents = (await HrmsDatabase.QueryAsync(
            _dbContext,
            """
SELECT DocumentType, FileName, UploadedAt, ExpiryDate
FROM EmployeeDocuments
WHERE EmployeeId = @EmployeeId
ORDER BY Id DESC;
""",
            command => HrmsDatabase.AddParameter(command, "@EmployeeId", id),
            reader => new DocumentLine(
                HrmsDatabase.GetString(reader, "DocumentType"),
                HrmsDatabase.GetString(reader, "FileName"),
                HrmsDatabase.GetDateTime(reader, "UploadedAt") ?? DateTime.MinValue,
                HrmsDatabase.GetDateOnly(reader, "ExpiryDate")))).ToList();

        return Page();
    }
}
