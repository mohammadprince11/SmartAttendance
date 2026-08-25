using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;
using SmartAttendance.Web.Infrastructure.Security;

namespace SmartAttendance.Web.Pages.Documents;

/// <summary>
/// مراجعة طلبات الوثائق (نظير تبويب «طلبات الوثائق» بكيان).
///
/// ⭐ **الاعتماد يولّد الوثيقة فوراً** بمعاملة واحدة — اعتمادٌ بلا توليد يترك موظفاً
/// ينتظر وثيقةً «معتمدة» لا وجود لها.
/// </summary>
public class RequestsModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly IWebHostEnvironment _environment;
    private readonly ICompanyScopeProvider _companyScope;

    public RequestsModel(
        ApplicationDbContext db,
        IWebHostEnvironment environment,
        ICompanyScopeProvider companyScope)
    {
        _db = db;
        _environment = environment;
        _companyScope = companyScope;
    }

    [BindProperty(SupportsGet = true)] public string StatusFilter { get; set; } = "Pending";

    public List<DocumentRequestStore.Request> Requests { get; private set; } = new();
    public int PendingCount { get; private set; }

    public async Task OnGetAsync()
    {
        var scope = await _companyScope.GetAsync(HttpContext.RequestAborted);
        var status = StatusFilter == "All" ? null : StatusFilter;
        Requests = await DocumentRequestStore.LoadAsync(_db, status: status, scope: scope);
        PendingCount = await DocumentRequestStore.PendingCountAsync(_db, scope);
    }

    public async Task<IActionResult> OnPostApproveAsync(int id)
    {
        var scope = await _companyScope.GetAsync(HttpContext.RequestAborted);
        var (ok, documentId, message) = await DocumentRequestStore.ApproveAsync(
            _db, id, User.Identity?.Name, DateOnly.FromDateTime(DateTime.Today), scope);

        if (!ok)
        {
            TempData["ErrorMessage"] = message;
            return RedirectToPage(new { statusFilter = StatusFilter });
        }

        TempData["SuccessMessage"] = message;
        return RedirectToPage("./View", new { id = documentId });
    }

    public async Task<IActionResult> OnPostRejectAsync(int id, string? note)
    {
        var scope = await _companyScope.GetAsync(HttpContext.RequestAborted);
        var (ok, message) = await DocumentRequestStore.RejectAsync(
            _db, id, User.Identity?.Name, note, scope);

        if (ok) TempData["SuccessMessage"] = message;
        else TempData["ErrorMessage"] = message;

        return RedirectToPage(new { statusFilter = StatusFilter });
    }

    /// <summary>مرفق الطلب — من الجذر المحميّ، لا رابط مباشر.</summary>
    public async Task<IActionResult> OnGetAttachmentAsync(int id)
    {
        var scope = await _companyScope.GetAsync(HttpContext.RequestAborted);
        var request = await DocumentRequestStore.FindAsync(_db, id, scope);
        if (request is null || !request.HasAttachment)
        {
            return NotFound();
        }

        var root = ProtectedFileStore.ResolveRoot(_environment.ContentRootPath);
        if (!ProtectedFileStore.TryResolvePhysicalPath(root, request.AttachmentKey, out var path)
            || !System.IO.File.Exists(path))
        {
            return NotFound();
        }

        var extension = Path.GetExtension(request.AttachmentName ?? path);
        return PhysicalFile(
            path, ProtectedFileStore.ContentTypeFor(extension), request.AttachmentName ?? "attachment" + extension);
    }
}
