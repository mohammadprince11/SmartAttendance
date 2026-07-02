using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartAttendance.Application.Shifts.Services;
using SmartAttendance.Application.Shifts.ViewModels;

namespace SmartAttendance.Web.Pages.Shifts;

public class DeleteModel : PageModel
{
    private readonly IShiftService _shiftService;

    public DeleteModel(IShiftService shiftService)
    {
        _shiftService = shiftService;
    }

    public ShiftDetailsViewModel Shift { get; set; } = new();

    public string? ErrorMessage { get; set; }

    public async Task<IActionResult> OnGetAsync(int id)
    {
        var shift = await _shiftService.GetByIdAsync(id);

        if (shift == null)
            return NotFound();

        Shift = shift;

        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int id)
    {
        var deleted = await _shiftService.DeleteAsync(id);

        if (!deleted)
        {
            ErrorMessage = "Shift not found or could not be deleted.";

            var shift = await _shiftService.GetByIdAsync(id);
            if (shift != null)
                Shift = shift;

            return Page();
        }

        TempData["SuccessMessage"] = "Shift deleted successfully.";

        return RedirectToPage("./Index");
    }
}
