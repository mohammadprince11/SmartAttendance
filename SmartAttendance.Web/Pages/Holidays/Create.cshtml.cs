using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartAttendance.Application.Holidays.Services;
using SmartAttendance.Application.Holidays.ViewModels;

namespace SmartAttendance.Web.Pages.Holidays;

public class CreateModel : PageModel
{
    private readonly IHolidayService _holidayService;

    public CreateModel(IHolidayService holidayService)
    {
        _holidayService = holidayService;
    }

    [BindProperty]
    public HolidayCreateViewModel Holiday { get; set; } = new();

    public string? ErrorMessage { get; set; }

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
            return Page();

        var created = await _holidayService.CreateAsync(Holiday);

        if (!created)
        {
            ErrorMessage = "Holiday could not be created.";
            return Page();
        }

        TempData["SuccessMessage"] = "Holiday created successfully.";

        return RedirectToPage("./Index");
    }
}
