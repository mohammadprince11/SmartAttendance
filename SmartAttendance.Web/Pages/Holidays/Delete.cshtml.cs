using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartAttendance.Application.Holidays.Services;
using SmartAttendance.Application.Holidays.ViewModels;
using SmartAttendance.Web.Infrastructure.Security;

namespace SmartAttendance.Web.Pages.Holidays;

public class DeleteModel : PageModel
{
    private readonly IHolidayService _holidayService;
    private readonly ICompanyScopeProvider _companyScope;

    public DeleteModel(IHolidayService holidayService,ICompanyScopeProvider companyScope)
    {
        _holidayService = holidayService;
        _companyScope=companyScope;
    }

    public HolidayDetailsViewModel Holiday { get; set; } = new();

    public string? ErrorMessage { get; set; }

    public async Task<IActionResult> OnGetAsync(int id)
    {
        if(!(await _companyScope.GetAsync(HttpContext.RequestAborted)).IsUnrestricted) return Forbid();
        var holiday = await _holidayService.GetByIdAsync(id);

        if (holiday == null)
            return NotFound();

        Holiday = holiday;

        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int id)
    {
        if(!(await _companyScope.GetAsync(HttpContext.RequestAborted)).IsUnrestricted) return Forbid();
        var deleted = await _holidayService.DeleteAsync(id);

        if (!deleted)
        {
            ErrorMessage = "Holiday not found or could not be deleted.";

            var holiday = await _holidayService.GetByIdAsync(id);
            if (holiday != null)
                Holiday = holiday;

            return Page();
        }

        TempData["SuccessMessage"] = "Holiday deleted successfully.";

        return RedirectToPage("./Index");
    }
}
