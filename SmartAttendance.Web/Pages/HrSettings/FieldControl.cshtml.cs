using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;

namespace SmartAttendance.Web.Pages.HrSettings;

/// <summary>
/// التحكم بالحقول (نمط كيان): مفتاح إلزامي مركزي لكل حقل بشاشة الموظف.
/// </summary>
public class FieldControlModel : PageModel
{
    private readonly ApplicationDbContext _dbContext;

    public FieldControlModel(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public HashSet<string> RequiredKeys { get; set; } = new();

    public async Task OnGetAsync()
    {
        RequiredKeys = await EmployeeFieldControl.GetRequiredKeysAsync(_dbContext);
    }

    public async Task<IActionResult> OnPostAsync(List<string>? required)
    {
        await EmployeeFieldControl.SaveAsync(_dbContext, required ?? new List<string>());
        TempData["SuccessMessage"] = "تم حفظ إعدادات التحكم بالحقول.";
        return RedirectToPage();
    }
}
