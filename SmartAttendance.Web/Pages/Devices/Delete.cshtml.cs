using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartAttendance.Application.Devices.Services;
using SmartAttendance.Application.Devices.ViewModels;

namespace SmartAttendance.Web.Pages.Devices;

public class DeleteModel : PageModel
{
    private readonly IDeviceService _deviceService;

    public DeleteModel(IDeviceService deviceService)
    {
        _deviceService = deviceService;
    }

    public DeviceDetailsViewModel Device { get; set; } = new();

    public string? ErrorMessage { get; set; }

    public async Task<IActionResult> OnGetAsync(int id)
    {
        var device = await _deviceService.GetByIdAsync(id);

        if (device == null)
            return NotFound();

        Device = device;

        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int id)
    {
        var deleted = await _deviceService.DeleteAsync(id);

        if (!deleted)
        {
            ErrorMessage = "Device not found or could not be deleted.";

            var device = await _deviceService.GetByIdAsync(id);
            if (device != null)
                Device = device;

            return Page();
        }

        TempData["SuccessMessage"] = "Device deleted successfully.";

        return RedirectToPage("./Index");
    }
}
