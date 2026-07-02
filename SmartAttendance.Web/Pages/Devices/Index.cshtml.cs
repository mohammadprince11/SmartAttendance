using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartAttendance.Application.Devices.Services;
using SmartAttendance.Application.Devices.ViewModels;

namespace SmartAttendance.Web.Pages.Devices;

public class IndexModel : PageModel
{
    private readonly IDeviceService _deviceService;

    public IndexModel(IDeviceService deviceService)
    {
        _deviceService = deviceService;
    }

    public IEnumerable<DeviceListViewModel> Devices { get; set; } = new List<DeviceListViewModel>();

    [BindProperty(SupportsGet = true)]
    public string? SearchTerm { get; set; }

    public async Task OnGetAsync()
    {
        Devices = await _deviceService.GetAllAsync(SearchTerm);
    }
}
