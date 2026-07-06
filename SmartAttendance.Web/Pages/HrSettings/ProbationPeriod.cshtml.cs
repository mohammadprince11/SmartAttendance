using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.HrSettings;

namespace SmartAttendance.Web.Pages.HrSettings;

public class ProbationPeriodModel : PageModel
{
    private readonly ApplicationDbContext _db;

    public ProbationPeriodModel(ApplicationDbContext db)
    {
        _db = db;
    }

    [BindProperty] public int DurationValue { get; set; } = 90;
    [BindProperty] public string DurationUnit { get; set; } = "Day";
    [BindProperty] public string StartBasis { get; set; } = "HireDate";
    [BindProperty] public bool ExcludeHolidays { get; set; }
    [BindProperty] public bool AllowExtension { get; set; }
    [BindProperty] public int ExtensionDays { get; set; }

    public async Task OnGetAsync()
    {
        await LoadAsync();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        await HrSettingsStore.SetAsync(_db, "Probation.DurationValue", Math.Max(0, DurationValue).ToString());
        await HrSettingsStore.SetAsync(_db, "Probation.DurationUnit", DurationUnit);
        await HrSettingsStore.SetAsync(_db, "Probation.StartBasis", StartBasis);
        await HrSettingsStore.SetAsync(_db, "Probation.ExcludeHolidays", ExcludeHolidays.ToString());
        await HrSettingsStore.SetAsync(_db, "Probation.AllowExtension", AllowExtension.ToString());
        await HrSettingsStore.SetAsync(_db, "Probation.ExtensionDays", Math.Max(0, ExtensionDays).ToString());

        TempData["SuccessMessage"] = "تم حفظ إعدادات فترة التجربة.";
        return RedirectToPage();
    }

    private async Task LoadAsync()
    {
        DurationValue = int.TryParse(await HrSettingsStore.GetAsync(_db, "Probation.DurationValue", "90"), out var days) ? days : 90;
        DurationUnit = await HrSettingsStore.GetAsync(_db, "Probation.DurationUnit", "Day");
        StartBasis = await HrSettingsStore.GetAsync(_db, "Probation.StartBasis", "HireDate");
        ExcludeHolidays = bool.TryParse(await HrSettingsStore.GetAsync(_db, "Probation.ExcludeHolidays", "False"), out var exclude) && exclude;
        AllowExtension = bool.TryParse(await HrSettingsStore.GetAsync(_db, "Probation.AllowExtension", "False"), out var allow) && allow;
        ExtensionDays = int.TryParse(await HrSettingsStore.GetAsync(_db, "Probation.ExtensionDays", "0"), out var ext) ? ext : 0;
    }
}
