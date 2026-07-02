using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using SmartAttendance.Application.AttendanceRecords.Services;
using SmartAttendance.Application.AttendanceRecords.ViewModels;
using SmartAttendance.Application.Devices.ViewModels;
using SmartAttendance.Application.Employees.ViewModels;
using SmartAttendance.Domain.Enums;

namespace SmartAttendance.Web.Pages.AttendanceRecords;

public class EditModel : PageModel
{
    private readonly IAttendanceRecordService _attendanceRecordService;

    public EditModel(IAttendanceRecordService attendanceRecordService)
    {
        _attendanceRecordService = attendanceRecordService;
    }

    [BindProperty]
    public AttendanceRecordEditViewModel AttendanceRecord { get; set; } = new();

    public IEnumerable<EmployeeListViewModel> Employees { get; set; } = new List<EmployeeListViewModel>();

    public IEnumerable<DeviceListViewModel> Devices { get; set; } = new List<DeviceListViewModel>();

    public IEnumerable<SelectListItem> Sources { get; set; } = new List<SelectListItem>();

    public IEnumerable<SelectListItem> Statuses { get; set; } = new List<SelectListItem>();

    public string? ErrorMessage { get; set; }

    public async Task<IActionResult> OnGetAsync(int id)
    {
        await LoadDropdownsAsync();

        var attendanceRecord = await _attendanceRecordService.GetEditByIdAsync(id);

        if (attendanceRecord == null)
            return NotFound();

        AttendanceRecord = attendanceRecord;

        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        await LoadDropdownsAsync();

        if (!ModelState.IsValid)
            return Page();

        var updated = await _attendanceRecordService.UpdateAsync(AttendanceRecord);

        if (!updated)
        {
            ErrorMessage = "Attendance record not found, invalid employee/device, or check-out time.";
            return Page();
        }

        TempData["SuccessMessage"] = "Attendance record updated successfully.";

        return RedirectToPage("./Index");
    }

    private async Task LoadDropdownsAsync()
    {
        Employees = await _attendanceRecordService.GetEmployeesForDropdownAsync();
        Devices = await _attendanceRecordService.GetDevicesForDropdownAsync();

        Sources = Enum.GetValues<AttendanceSource>()
            .Select(x => new SelectListItem(x.ToString(), x.ToString()))
            .ToList();

        Statuses = Enum.GetValues<AttendanceStatus>()
            .Select(x => new SelectListItem(x.ToString(), x.ToString()))
            .ToList();
    }
}
