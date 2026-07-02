using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartAttendance.Application.Branches.ViewModels;
using SmartAttendance.Application.Devices.Services;
using SmartAttendance.Application.Devices.ViewModels;

namespace SmartAttendance.Web.Pages.Devices;

public class CreateModel : PageModel
{
    private readonly IDeviceService _deviceService;

    public CreateModel(IDeviceService deviceService)
    {
        _deviceService = deviceService;
    }

    [BindProperty]
    public DeviceCreateViewModel Device { get; set; } = new();

    public IEnumerable<BranchListViewModel> Branches { get; set; } = new List<BranchListViewModel>();

    public string? ErrorMessage { get; set; }

    public async Task OnGetAsync()
    {
        Branches = await _deviceService.GetBranchesForDropdownAsync();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        Branches = await _deviceService.GetBranchesForDropdownAsync();

        if (!ModelState.IsValid)
            return Page();

        var created = await _deviceService.CreateAsync(Device);

        if (!created)
        {
            ErrorMessage = "Device serial number already exists or selected branch is invalid.";
            return Page();
        }

        TempData["SuccessMessage"] = "Device created successfully.";

        return RedirectToPage("./Index");
    }
}
