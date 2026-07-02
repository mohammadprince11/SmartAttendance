using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartAttendance.Application.Branches.ViewModels;
using SmartAttendance.Application.Devices.Services;
using SmartAttendance.Application.Devices.ViewModels;

namespace SmartAttendance.Web.Pages.Devices;

public class EditModel : PageModel
{
    private readonly IDeviceService _deviceService;

    public EditModel(IDeviceService deviceService)
    {
        _deviceService = deviceService;
    }

    [BindProperty]
    public DeviceEditViewModel Device { get; set; } = new();

    public IEnumerable<BranchListViewModel> Branches { get; set; } = new List<BranchListViewModel>();

    public string? ErrorMessage { get; set; }

    public async Task<IActionResult> OnGetAsync(int id)
    {
        Branches = await _deviceService.GetBranchesForDropdownAsync();

        var device = await _deviceService.GetEditByIdAsync(id);

        if (device == null)
            return NotFound();

        Device = device;

        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        Branches = await _deviceService.GetBranchesForDropdownAsync();

        if (!ModelState.IsValid)
            return Page();

        var updated = await _deviceService.UpdateAsync(Device);

        if (!updated)
        {
            ErrorMessage = "Device not found, serial number already exists, or selected branch is invalid.";
            return Page();
        }

        TempData["SuccessMessage"] = "Device updated successfully.";

        return RedirectToPage("./Index");
    }
}
