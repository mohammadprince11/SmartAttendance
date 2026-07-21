using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;

namespace SmartAttendance.Web.Pages.HrSettings;

/// <summary>
/// إعداد مخطط رمز الموظف التلقائي (نمط كيان: مقاطع = بادئة ثابتة + رقم متسلسل).
/// </summary>
public class EmployeeCodeSchemaPageModel : PageModel
{
    private readonly ApplicationDbContext _dbContext;

    public EmployeeCodeSchemaPageModel(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [TempData]
    public string? Message { get; set; }

    public EmployeeCodeSchema.SchemaRow Schema { get; set; } = new();
    public string Preview { get; set; } = string.Empty;

    public async Task OnGetAsync()
    {
        Schema = await EmployeeCodeSchema.GetAsync(_dbContext) ?? new EmployeeCodeSchema.SchemaRow();
        Preview = Schema.Prefix + (Schema.LastNumber + 1).ToString(new string('0', Math.Clamp(Schema.Digits, 1, 12)));
    }

    public async Task<IActionResult> OnPostAsync(string prefix, int digits, int lastNumber, bool isActive)
    {
        await EmployeeCodeSchema.SaveAsync(_dbContext, prefix?.Trim() ?? string.Empty, digits, lastNumber, isActive);
        Message = "تم حفظ مخطط رمز الموظف.";
        return RedirectToPage();
    }
}
