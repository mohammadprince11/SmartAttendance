using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;

namespace SmartAttendance.Web.Pages.EmployeeGeoLocations;

/// <summary>
/// المواقع الجغرافية للموظفين (/EmployeeGeoLocations) — الصفحة الرابعة بقسم «حضور
/// الموظفين» (نمط كيان): قائمة موظفين + عمود Geo Location + تعيين جماعي لموقع
/// جغرافي، مع إدارة تعريفات المواقع (اسم/إحداثيات/نصف قطر). إنفاذ البصم المكاني مؤجَّل.
/// </summary>
public class IndexModel : PageModel
{
    private readonly ApplicationDbContext _dbContext;

    public IndexModel(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [BindProperty(SupportsGet = true)]
    public string? Search { get; set; }

    [BindProperty(SupportsGet = true)]
    public string Filter { get; set; } = "All";   // All | Assigned | Unassigned

    public List<GeoLocationStore.EmployeeGeoRow> Rows { get; set; } = new();
    public List<GeoLocationStore.GeoLocation> Locations { get; set; } = new();
    public int TotalRows { get; set; }
    public int AssignedCount { get; set; }
    public int UnassignedCount { get; set; }

    public async Task OnGetAsync()
    {
        Locations = (await GeoLocationStore.ListLocationsAsync(_dbContext)).Where(l => l.IsActive).ToList();

        var all = await GeoLocationStore.ListEmployeesAsync(_dbContext);
        AssignedCount = all.Count(r => r.GeoLocationId != null);
        UnassignedCount = all.Count - AssignedCount;

        var filtered = all;
        if (Filter == "Assigned") filtered = filtered.Where(r => r.GeoLocationId != null).ToList();
        if (Filter == "Unassigned") filtered = filtered.Where(r => r.GeoLocationId == null).ToList();

        if (!string.IsNullOrWhiteSpace(Search))
        {
            var v = Search.Trim();
            filtered = filtered.Where(r =>
                r.EmployeeNo.Contains(v, StringComparison.OrdinalIgnoreCase) ||
                r.EmployeeName.Contains(v, StringComparison.OrdinalIgnoreCase) ||
                r.Department.Contains(v, StringComparison.OrdinalIgnoreCase) ||
                r.Branch.Contains(v, StringComparison.OrdinalIgnoreCase)).ToList();
        }
        TotalRows = filtered.Count;
        Rows = filtered;
    }

    public async Task<IActionResult> OnPostAssignAsync()
    {
        var ids = ParseSelected();
        var geoId = int.TryParse(Request.Form["GeoLocationId"], out var g) ? g : 0;
        if (ids.Count == 0 || geoId <= 0)
        {
            TempData["SuccessMessage"] = "حدد موظفين واختر موقعاً أولاً.";
        }
        else
        {
            var count = await GeoLocationStore.AssignAsync(_dbContext, ids, geoId);
            TempData["SuccessMessage"] = $"تم تعيين الموقع لـ{count} موظفاً.";
        }
        return RedirectToPage(new { Search, Filter });
    }

    public async Task<IActionResult> OnPostUnassignAsync()
    {
        var ids = ParseSelected();
        if (ids.Count == 0)
        {
            TempData["SuccessMessage"] = "حدد موظفين أولاً.";
        }
        else
        {
            await GeoLocationStore.UnassignAsync(_dbContext, ids);
            TempData["SuccessMessage"] = $"أُلغي تعيين الموقع لـ{ids.Count} موظفاً.";
        }
        return RedirectToPage(new { Search, Filter });
    }

    public async Task<IActionResult> OnPostSaveLocationAsync()
    {
        var form = Request.Form;
        var loc = new GeoLocationStore.GeoLocation
        {
            Id = int.TryParse(form["LocId"], out var id) ? id : 0,
            Name = form["LocName"].ToString().Trim(),
            Latitude = decimal.TryParse(form["LocLat"], out var la) ? la : 0,
            Longitude = decimal.TryParse(form["LocLng"], out var lo) ? lo : 0,
            RadiusMeters = int.TryParse(form["LocRadius"], out var r) ? Math.Max(1, r) : 100,
            IsActive = form["LocActive"] == "true"
        };
        if (string.IsNullOrWhiteSpace(loc.Name))
        {
            TempData["SuccessMessage"] = "اسم الموقع مطلوب.";
            return RedirectToPage(new { Search, Filter });
        }
        await GeoLocationStore.SaveLocationAsync(_dbContext, loc);
        TempData["SuccessMessage"] = loc.Id > 0 ? "تم تحديث الموقع." : "تمت إضافة الموقع.";
        return RedirectToPage(new { Search, Filter });
    }

    public async Task<IActionResult> OnPostDeleteLocationAsync(int id)
    {
        await GeoLocationStore.DeleteLocationAsync(_dbContext, id);
        TempData["SuccessMessage"] = "تم حذف الموقع (وإلغاء تعيينه من الموظفين).";
        return RedirectToPage(new { Search, Filter });
    }

    private List<int> ParseSelected() =>
        Request.Form["SelectedIds"]
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => int.TryParse(v, out var id) ? id : 0)
            .Where(id => id > 0).Distinct().ToList();
}
