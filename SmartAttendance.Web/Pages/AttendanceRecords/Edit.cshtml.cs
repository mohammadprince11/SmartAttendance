using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using SmartAttendance.Application.AttendanceRecords.Services;
using SmartAttendance.Application.AttendanceRecords.ViewModels;
using SmartAttendance.Application.Devices.ViewModels;
using SmartAttendance.Domain.Enums;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Pages.Shared;

namespace SmartAttendance.Web.Pages.AttendanceRecords;

public class EditModel : PageModel
{
    private readonly IAttendanceRecordService _attendanceRecordService;
    private readonly ApplicationDbContext _db;

    public EditModel(IAttendanceRecordService attendanceRecordService, ApplicationDbContext db)
    {
        _attendanceRecordService = attendanceRecordService;
        _db = db;
    }

    [BindProperty]
    public AttendanceRecordEditViewModel AttendanceRecord { get; set; } = new();

    /// <summary>رمز الموظف المحسوم واسمه — يبدأ بهما <c>_EmployeePicker</c> معبَّأً.</summary>
    public string? SelectedEmployeeCode { get; set; }

    public string? SelectedEmployeeName { get; set; }

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

        // ⚠️ بعد إسناد السجلّ لا قبله: `LoadDropdownsAsync` يعمل قبل القراءة،
        //    فلو وُضع النداء هناك لبحث عن الموظف صفر وبدأ المنتقي فارغاً بالتعديل.
        await LoadPickerIdentityAsync();

        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        await LoadDropdownsAsync();
        await LoadPickerIdentityAsync();

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

    /// <summary>صفٌّ واحد للموظف المحسوم — المنتقي لا يحمّل قائمة.</summary>
    private async Task LoadPickerIdentityAsync()
    {
        var identity = await EmployeePickerLookup.LoadAsync(_db, AttendanceRecord.EmployeeId);
        SelectedEmployeeCode = identity?.Code;
        SelectedEmployeeName = identity?.Name;
    }

    private async Task LoadDropdownsAsync()
    {
        Devices = await _attendanceRecordService.GetDevicesForDropdownAsync();

        Sources = Enum.GetValues<AttendanceSource>()
            .Select(x => new SelectListItem(x.ToString(), x.ToString()))
            .ToList();

        Statuses = Enum.GetValues<AttendanceStatus>()
            .Select(x => new SelectListItem(x.ToString(), x.ToString()))
            .ToList();
    }
}
