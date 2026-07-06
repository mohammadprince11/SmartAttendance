using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.HrSettings;

namespace SmartAttendance.Web.Pages.HrSettings;

public class TerminationReasonsModel : PageModel
{
    private readonly ApplicationDbContext _db;

    public TerminationReasonsModel(ApplicationDbContext db)
    {
        _db = db;
    }

    public List<TerminationReasonRow> Reasons { get; private set; } = new();

    [BindProperty] public string Name { get; set; } = string.Empty;
    [BindProperty] public bool IsMandatory { get; set; }
    [BindProperty] public bool RequiresSelfService { get; set; } = true;
    [BindProperty] public decimal EndOfServicePercent { get; set; } = 100;

    public async Task OnGetAsync()
    {
        Reasons = await HrSettingsStore.LoadTerminationReasonsAsync(_db);
    }

    public async Task<IActionResult> OnPostAddAsync()
    {
        if (!string.IsNullOrWhiteSpace(Name))
        {
            await HrSettingsStore.AddTerminationReasonAsync(_db, Name, IsMandatory, RequiresSelfService, EndOfServicePercent);
            TempData["SuccessMessage"] = "تمت إضافة سبب إيقاف جديد.";
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostUpdateAsync(int id, string name, bool isMandatory, bool requiresSelfService, decimal endOfServicePercent, bool isActive)
    {
        if (!string.IsNullOrWhiteSpace(name))
        {
            await HrSettingsStore.UpdateTerminationReasonAsync(_db, id, name, isMandatory, requiresSelfService, endOfServicePercent, isActive);
            TempData["SuccessMessage"] = "تم تعديل سبب الإيقاف.";
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        await HrSettingsStore.DeleteTerminationReasonAsync(_db, id);
        TempData["SuccessMessage"] = "تم حذف سبب الإيقاف.";
        return RedirectToPage();
    }
}
