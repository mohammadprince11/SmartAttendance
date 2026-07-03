using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartAttendance.Application.Setup.Services;
using SmartAttendance.Application.Setup.ViewModels;

namespace SmartAttendance.Web.Pages.Setup;

public class IndexModel : PageModel
{
    private readonly ISetupService _setupService;

    public IndexModel(ISetupService setupService)
    {
        _setupService = setupService;
    }

    public SystemSetupViewModel SetupStatus { get; set; } = new();

    [BindProperty]
    public BulkAssignShiftViewModel BulkAssignShift { get; set; } = new();

    public string? ErrorMessage { get; set; }

    public async Task OnGetAsync()
    {
        SetupStatus = await _setupService.GetSetupStatusAsync();
    }

    public async Task<IActionResult> OnPostBulkAssignShiftAsync()
    {
        var result = await _setupService.BulkAssignShiftAsync(BulkAssignShift);

        if (result.Success)
            TempData["SuccessMessage"] = result.Message;
        else
            TempData["ErrorMessage"] = result.Message;

        return RedirectToPage();
    }
}
