using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;

namespace SmartAttendance.Web.Pages.EmployeeOnlinePunches;

/// <summary>
/// إدارة البصمات عبر الإنترنت (/EmployeeOnlinePunches) — نمط كيان قسم 36.ج: عرض
/// بصمات المتصفح/الجوال (Source=موبايل) بفلاتر، وحذف المختار منها. تدخل اشتقاق
/// اليومية كأي بصمة عند «تحديث الحضور».
/// </summary>
public class IndexModel : PageModel
{
    private readonly ApplicationDbContext _db;

    public IndexModel(ApplicationDbContext db)
    {
        _db = db;
    }

    [BindProperty(SupportsGet = true)] public string? Search { get; set; }
    [BindProperty(SupportsGet = true)] public string? PunchType { get; set; }
    [BindProperty(SupportsGet = true)] public string? FDept { get; set; }
    [BindProperty(SupportsGet = true)] public string? FBranch { get; set; }
    [BindProperty(SupportsGet = true)] public string? From { get; set; }
    [BindProperty(SupportsGet = true)] public string? To { get; set; }

    public List<OnlinePunchStore.OnlinePunch> Items { get; set; } = new();
    public List<string> AllDepartments { get; set; } = new();
    public List<string> AllBranches { get; set; } = new();

    public int InCount { get; set; }
    public int OutCount { get; set; }

    public async Task OnGetAsync()
    {
        Items = await OnlinePunchStore.ListAsync(_db, new OnlinePunchStore.Filter
        {
            Search = Search,
            PunchType = PunchType,
            Department = FDept,
            Branch = FBranch,
            From = DateOnly.TryParse(From, out var f) ? f : null,
            To = DateOnly.TryParse(To, out var t) ? t : null
        });

        InCount = Items.Count(x => x.PunchType == "In");
        OutCount = Items.Count(x => x.PunchType == "Out");

        (AllDepartments, AllBranches, _) = await MassScopeResolver.OrgListsAsync(_db);
    }

    private object Route() => new { Search, PunchType, FDept, FBranch, From, To };

    public async Task<IActionResult> OnPostDeleteSelectedAsync()
    {
        var ids = Request.Form["SelectedIds"].Where(v => int.TryParse(v, out _)).Select(int.Parse).ToList();
        if (ids.Count > 0)
        {
            var n = await OnlinePunchStore.DeleteManyAsync(_db, ids);
            TempData["OpMessage"] = $"حُذفت {n} بصمة عبر الإنترنت.";
            TempData["OpOk"] = true;
        }
        else
        {
            TempData["OpMessage"] = "حدد بصمات أولاً.";
            TempData["OpOk"] = false;
        }
        return RedirectToPage(Route());
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        await OnlinePunchStore.DeleteManyAsync(_db, new[] { id });
        TempData["OpMessage"] = "حُذفت البصمة.";
        TempData["OpOk"] = true;
        return RedirectToPage(Route());
    }
}
