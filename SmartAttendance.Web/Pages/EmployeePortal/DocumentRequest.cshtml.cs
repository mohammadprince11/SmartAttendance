using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;
using SmartAttendance.Web.Infrastructure.Security;

namespace SmartAttendance.Web.Pages.EmployeePortal;

/// <summary>
/// بوابة الموظف — طلب وثيقة (شهادة راتب · كتاب تعريف · …).
///
/// ⭐ **الموظف لا يرى إلا ما يستحقّه**: القوالب تُرشَّح بشروط الجمهور نفسها قبل
/// عرضها، فلا يطلب وثيقةً لا تخصّه ثم تُرفض — رفضٌ كان يُفسَّر تعسّفاً.
/// </summary>
public class DocumentRequestModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly IWebHostEnvironment _environment;

    public DocumentRequestModel(ApplicationDbContext db, IWebHostEnvironment environment)
    {
        _db = db;
        _environment = environment;
    }

    [TempData] public string? StatusMessage { get; set; }

    [BindProperty] public int TemplateId { get; set; }
    [BindProperty] public string? Reason { get; set; }
    [BindProperty] public IFormFile? Attachment { get; set; }

    public List<DocumentTemplateStore.Template> Available { get; private set; } = new();
    public List<DocumentRequestStore.Request> MyRequests { get; private set; } = new();

    public async Task<IActionResult> OnGetAsync()
    {
        if (!await SelfServiceAccessPolicy.IsAllowedAsync(_db, HttpContext, "DocumentRequest")) return Forbid();
        var employeeId = await ResolveEmployeeIdAsync();
        if (employeeId <= 0)
        {
            return Forbid();
        }

        Available = await DocumentRequestStore.RequestableAsync(_db, employeeId, DateOnly.FromDateTime(DateTime.Today));
        MyRequests = await DocumentRequestStore.LoadAsync(_db, employeeId);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!await SelfServiceAccessPolicy.IsAllowedAsync(_db, HttpContext, "DocumentRequest")) return Forbid();
        var employeeId = await ResolveEmployeeIdAsync();
        if (employeeId <= 0)
        {
            StatusMessage = "تعذّر تحديد هويتك.";
            return RedirectToPage();
        }

        // ⚠️ الأهلية تُعاد **بالخادم** لا يُوثق باختيار النموذج: نموذجٌ معدَّل يدوياً
        // كان يطلب أي قالب بمعرّفه.
        var allowed = await DocumentRequestStore.RequestableAsync(_db, employeeId, DateOnly.FromDateTime(DateTime.Today));
        var template = allowed.FirstOrDefault(row => row.Id == TemplateId);

        if (template is null)
        {
            StatusMessage = "هذه الوثيقة غير متاحة لك.";
            return RedirectToPage();
        }

        string? key = null;
        string? fileName = null;

        if (Attachment is { Length: > 0 })
        {
            var extension = Path.GetExtension(Attachment.FileName);

            if (!ProtectedFileStore.IsAllowedExtension(extension) || !ProtectedFileStore.IsAllowedSize(Attachment.Length))
            {
                StatusMessage = "المرفق غير مسموح (النوع أو الحجم).";
                return RedirectToPage();
            }

            key = ProtectedFileStore.BuildStorageKey(employeeId, "doc-request", extension);
            var root = ProtectedFileStore.ResolveRoot(_environment.ContentRootPath);

            await using var stream = Attachment.OpenReadStream();
            if (!await ProtectedFileStore.SaveAsync(root, key, stream))
            {
                StatusMessage = "تعذّر حفظ المرفق.";
                return RedirectToPage();
            }

            fileName = Path.GetFileName(Attachment.FileName);
        }

        if (DocumentRequestPolicy.Validate(
                template.IsReasonRequired, template.IsAttachmentRequired, Reason, key is not null) is { } error)
        {
            StatusMessage = error;
            return RedirectToPage();
        }

        await DocumentRequestStore.CreateAsync(_db, employeeId, template, Reason, key, fileName);

        StatusMessage = "أُرسل طلبك — ستُشعَر عند اعتماده.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostWithdrawAsync(int id)
    {
        if (!await SelfServiceAccessPolicy.IsAllowedAsync(_db, HttpContext, "DocumentRequest")) return Forbid();
        var employeeId = await ResolveEmployeeIdAsync();
        var done = employeeId > 0 && await DocumentRequestStore.WithdrawAsync(_db, id, employeeId);

        StatusMessage = done ? "سُحب الطلب." : "تعذّر سحب الطلب (قد يكون حُسم).";
        return RedirectToPage();
    }

    private async Task<int> ResolveEmployeeIdAsync()
    {
        var employeeIdClaim = User.FindFirstValue("EmployeeId");
        if (int.TryParse(employeeIdClaim, out var claimEmployeeId) && claimEmployeeId > 0)
            return claimEmployeeId;

        var username = User.Identity?.Name ?? User.FindFirstValue(ClaimTypes.Name);
        if (!string.IsNullOrWhiteSpace(username))
            return await HrmsDatabase.ScalarAsync<int>(
                _db,
                "SELECT TOP 1 ISNULL(EmployeeId, 0) FROM AppLoginUsers WHERE Username = @Username AND IsActive = 1",
                command => HrmsDatabase.AddParameter(command, "@Username", username));
        return 0;
    }
}
