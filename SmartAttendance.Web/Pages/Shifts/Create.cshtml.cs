using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartAttendance.Application.Shifts.Services;
using SmartAttendance.Application.Shifts.ViewModels;

namespace SmartAttendance.Web.Pages.Shifts;

public class CreateModel : PageModel
{
    private readonly IShiftService _shiftService;

    public CreateModel(IShiftService shiftService)
    {
        _shiftService = shiftService;
    }

    [BindProperty]
    public ShiftCreateViewModel Shift { get; set; } = new();

    public string? ErrorMessage { get; set; }

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
            return Page();

        var created = await _shiftService.CreateAsync(Shift);

        if (!created)
        {
            ErrorMessage = "Shift code already exists.";
            return Page();
        }

        TempData["SuccessMessage"] = "Shift created successfully.";

        return RedirectToPage("./Index");
    }
}
