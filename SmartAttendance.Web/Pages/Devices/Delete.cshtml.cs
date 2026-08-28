using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartAttendance.Application.Devices.Services;
using SmartAttendance.Application.Devices.ViewModels;
using Microsoft.EntityFrameworkCore;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Security;

namespace SmartAttendance.Web.Pages.Devices;

public class DeleteModel : PageModel
{
    private readonly IDeviceService _deviceService;
    private readonly ApplicationDbContext _dbContext;
    private readonly ICompanyScopeProvider _companyScope;

    public DeleteModel(IDeviceService deviceService,ApplicationDbContext dbContext,ICompanyScopeProvider companyScope)
    {
        _deviceService = deviceService;
        _dbContext=dbContext;
        _companyScope=companyScope;
    }

    public DeviceDetailsViewModel Device { get; set; } = new();

    public string? ErrorMessage { get; set; }

    public async Task<IActionResult> OnGetAsync(int id)
    {
        if(!await CanAccessAsync(id)) return NotFound();
        var device = await _deviceService.GetByIdAsync(id);

        if (device == null)
            return NotFound();

        Device = device;

        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int id)
    {
        if(!await CanAccessAsync(id)) return NotFound();
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

    private async Task<bool> CanAccessAsync(int id)
    {
        var scope=await _companyScope.GetAsync(HttpContext.RequestAborted);
        if(scope.IsDeniedAll) return false;
        var allowed=scope.AllowedCompanyIds.ToArray();
        return await _dbContext.Devices.AsNoTracking().AnyAsync(device=>device.Id==id&&
            (scope.IsUnrestricted||allowed.Contains(device.Branch.CompanyId)),HttpContext.RequestAborted);
    }
}
