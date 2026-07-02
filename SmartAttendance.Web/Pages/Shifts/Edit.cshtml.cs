using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartAttendance.Application.Shifts.Services;
using SmartAttendance.Application.Shifts.ViewModels;

namespace SmartAttendance.Web.Pages.Shifts;

public class EditModel : PageModel
{
    private readonly IShiftService _shiftService;

    public EditModel(IShiftService shiftService)
    {
        _shiftService = shiftService;
    }

    [BindProperty]
    public ShiftEditViewModel Shift { get; set; } = new();

    public string? ErrorMessage { get; set; }

    public async Task<IActionResult> OnGetAsync(int id)
    {
        var shift = await _shiftService.GetEditByIdAsync(id);

        if (shift == null)
            return NotFound();

        Shift = shift;

        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
            return Page();

        var updated = await _shiftService.UpdateAsync(Shift);

        if (!updated)
        {
            ErrorMessage = "Shift not found or shift code already exists.";
            return Page();
        }

        TempData["SuccessMessage"] = "Shift updated successfully.";

        return RedirectToPage("./Index");
    }
}
