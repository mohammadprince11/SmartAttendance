using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;
using SmartAttendance.Web.Infrastructure.Security;

namespace SmartAttendance.Web.Pages.MissingPunchRequests;

/// <summary>
/// إدارة طلبات البصمة المفقودة (/MissingPunchRequests) — نمط كيان قسم 36.ب: جدول
/// طلبات بفلاتر غنية، إنشاء/تحرير طلب، والبتّ (موافقة تُنشئ بصمة/رفض/إلغاء). التحرير
/// والبتّ محكومان بصلاحية الصفحة (Attendance.MissingPunch) مع تجاوز الأدمن.
/// </summary>
public class IndexModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly IAccessRoleService _accessRoles;

    public IndexModel(ApplicationDbContext db, IAccessRoleService accessRoles)
    {
        _db = db;
        _accessRoles = accessRoles;
    }

    public const string PageCode = "Attendance.MissingPunch";

    [BindProperty(SupportsGet = true)] public string? Search { get; set; }
    [BindProperty(SupportsGet = true)] public string? Status { get; set; }
    [BindProperty(SupportsGet = true)] public string? PunchType { get; set; }
    [BindProperty(SupportsGet = true)] public string? FDept { get; set; }
    [BindProperty(SupportsGet = true)] public string? FBranch { get; set; }
    [BindProperty(SupportsGet = true)] public string? FPosition { get; set; }
    [BindProperty(SupportsGet = true)] public string? From { get; set; }
    [BindProperty(SupportsGet = true)] public string? To { get; set; }

    public List<MissingPunchRequestStore.Request> Items { get; set; } = new();
    public List<EmployeeOption> Employees { get; set; } = new();
    public List<PunchSemanticStore.PunchSemantic> Semantics { get; set; } = new();
    public List<string> AllDepartments { get; set; } = new();
    public List<string> AllBranches { get; set; } = new();
    public List<string> AllJobTitles { get; set; } = new();

    public int PendingCount { get; set; }
    public int ApprovedCount { get; set; }
    public int RejectedCount { get; set; }

    // صلاحيات فعّالة (الأدمن يتجاوز)
    public bool CanCreate { get; set; }
    public bool CanEdit { get; set; }
    public bool CanDelete { get; set; }

    public sealed class EmployeeOption
    {
        public int Id { get; set; }
        public string No { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
    }

    private async Task ResolvePermissionsAsync()
    {
        var role = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;
        if (RoleRouteCatalog.IsAdmin(role))
        {
            CanCreate = CanEdit = CanDelete = true;
            return;
        }
        var systemUserId = PeopleAccessContext.GetSystemUserId(HttpContext) ?? 0;
        var profile = systemUserId > 0 ? await _accessRoles.ResolveAsync(systemUserId, HttpContext.RequestAborted) : null;
        CanCreate = profile == null || profile.Can(PageCode, "Create");
        CanEdit = profile == null || profile.Can(PageCode, "Edit");
        CanDelete = profile == null || profile.Can(PageCode, "Delete");
    }

    public async Task OnGetAsync()
    {
        await ResolvePermissionsAsync();

        Items = await MissingPunchRequestStore.ListAsync(_db, new MissingPunchRequestStore.Filter
        {
            Search = Search,
            Status = Status,
            PunchType = PunchType,
            Department = FDept,
            Branch = FBranch,
            Position = FPosition,
            From = DateOnly.TryParse(From, out var f) ? f : null,
            To = DateOnly.TryParse(To, out var t) ? t : null
        });

        // العدّادات على كامل الطلبات (بلا فلتر الحالة) لعرض النبض الصحيح
        var all = await MissingPunchRequestStore.ListAsync(_db, new MissingPunchRequestStore.Filter());
        PendingCount = all.Count(r => r.Status == MissingPunchRequestStore.Pending);
        ApprovedCount = all.Count(r => r.Status == MissingPunchRequestStore.Approved);
        RejectedCount = all.Count(r => r.Status == MissingPunchRequestStore.Rejected);

        Semantics = await PunchSemanticStore.ListAsync(_db);
        Employees = await HrmsDatabase.QueryAsync(_db,
            "SELECT Id, ISNULL(EmployeeNo, N'') AS EmployeeNo, ISNULL(FullName, N'') AS FullName FROM Employees WHERE ISNULL(IsDeleted,0)=0 AND ISNULL(IsActive,1)=1 ORDER BY FullName;",
            command => { },
            reader => new EmployeeOption
            {
                Id = HrmsDatabase.GetInt(reader, "Id"),
                No = HrmsDatabase.GetString(reader, "EmployeeNo"),
                Name = HrmsDatabase.GetString(reader, "FullName")
            });

        (AllDepartments, AllBranches, AllJobTitles) = await MassScopeResolver.OrgListsAsync(_db);
    }

    private object Route() => new { Search, Status, PunchType, FDept, FBranch, FPosition, From, To };

    private string UserName => User?.Identity?.Name ?? "system";

    public async Task<IActionResult> OnPostSaveAsync()
    {
        await ResolvePermissionsAsync();
        var f = Request.Form;
        var id = int.TryParse(f["Id"], out var i) ? i : 0;

        if (id == 0 ? !CanCreate : !CanEdit)
        {
            TempData["MpMessage"] = "لا تملك صلاحية " + (id == 0 ? "إنشاء" : "تحرير") + " الطلبات.";
            TempData["MpOk"] = false;
            return RedirectToPage(Route());
        }

        // وقت البصمة = تاريخ + وقت
        DateTime? punchAt = null;
        if (DateOnly.TryParse(f["PunchDate"], out var d) && TimeOnly.TryParse(f["PunchTime"], out var tm))
            punchAt = d.ToDateTime(tm);

        if (punchAt == null)
        {
            TempData["MpMessage"] = "أدخل تاريخ ووقت البصمة.";
            TempData["MpOk"] = false;
            return RedirectToPage(Route());
        }

        var req = new MissingPunchRequestStore.Request
        {
            Id = id,
            EmployeeId = int.TryParse(f["EmployeeId"], out var e) ? e : 0,
            PunchAt = punchAt.Value,
            PunchType = f["PunchType"].ToString() is { Length: > 0 } pt ? pt : "In",
            PunchSemanticId = int.TryParse(f["PunchSemanticId"], out var si) && si > 0 ? si : null,
            Reason = string.IsNullOrWhiteSpace(f["Reason"]) ? null : f["Reason"].ToString().Trim(),
            Source = "مباشر"
        };

        var (ok, message) = await MissingPunchRequestStore.SaveAsync(_db, req, UserName);
        TempData["MpMessage"] = message;
        TempData["MpOk"] = ok;
        return RedirectToPage(Route());
    }

    public async Task<IActionResult> OnPostApproveAsync(int id)
    {
        await ResolvePermissionsAsync();
        if (!CanEdit) { TempData["MpMessage"] = "لا تملك صلاحية البتّ في الطلبات."; TempData["MpOk"] = false; return RedirectToPage(Route()); }
        var note = Request.Form["DecisionNote"].ToString();
        var (ok, message) = await MissingPunchRequestStore.ApproveAsync(_db, id, string.IsNullOrWhiteSpace(note) ? null : note.Trim(), UserName);
        TempData["MpMessage"] = message; TempData["MpOk"] = ok;
        return RedirectToPage(Route());
    }

    public async Task<IActionResult> OnPostRejectAsync(int id)
    {
        await ResolvePermissionsAsync();
        if (!CanEdit) { TempData["MpMessage"] = "لا تملك صلاحية البتّ في الطلبات."; TempData["MpOk"] = false; return RedirectToPage(Route()); }
        var note = Request.Form["DecisionNote"].ToString();
        var (ok, message) = await MissingPunchRequestStore.RejectAsync(_db, id, string.IsNullOrWhiteSpace(note) ? null : note.Trim(), UserName);
        TempData["MpMessage"] = message; TempData["MpOk"] = ok;
        return RedirectToPage(Route());
    }

    public async Task<IActionResult> OnPostCancelAsync(int id)
    {
        await ResolvePermissionsAsync();
        if (!CanEdit) { TempData["MpMessage"] = "لا تملك صلاحية البتّ في الطلبات."; TempData["MpOk"] = false; return RedirectToPage(Route()); }
        var (ok, message) = await MissingPunchRequestStore.CancelAsync(_db, id, UserName);
        TempData["MpMessage"] = message; TempData["MpOk"] = ok;
        return RedirectToPage(Route());
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        await ResolvePermissionsAsync();
        if (!CanDelete) { TempData["MpMessage"] = "لا تملك صلاحية حذف الطلبات."; TempData["MpOk"] = false; return RedirectToPage(Route()); }
        await MissingPunchRequestStore.DeleteAsync(_db, id);
        TempData["MpMessage"] = "حُذف الطلب.";
        TempData["MpOk"] = true;
        return RedirectToPage(Route());
    }
}
