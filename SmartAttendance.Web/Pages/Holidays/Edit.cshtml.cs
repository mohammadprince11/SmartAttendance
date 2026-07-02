using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartAttendance.Application.Holidays.Services;
using SmartAttendance.Application.Holidays.ViewModels;

namespace SmartAttendance.Web.Pages.Holidays;

public class EditModel : PageModel
{
    private readonly IHolidayService _holidayService;

    public EditModel(IHolidayService holidayService)
    {
        _holidayService = holidayService;
    }

    [BindProperty]
    public HolidayEditViewModel Holiday { get; set; } = new();

    public string? ErrorMessage { get; set; }

    public async Task<IActionResult> OnGetAsync(int id)
    {
        var holiday = await _holidayService.GetEditByIdAsync(id);

        if (holiday == null)
            return NotFound();

        Holiday = holiday;

        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
            return Page();

        var updated = await _holidayService.UpdateAsync(Holiday);

        if (!updated)
        {
            ErrorMessage = "Holiday not found or could not be updated.";
            return Page();
        }

        TempData["SuccessMessage"] = "Holiday updated successfully.";

        return RedirectToPage("./Index");
    }
}
