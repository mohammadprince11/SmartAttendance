using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using SmartAttendance.Application.AttendanceRecords.Services;
using SmartAttendance.Application.AttendanceRecords.ViewModels;
using SmartAttendance.Application.Devices.ViewModels;
using SmartAttendance.Domain.Enums;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Security;
using SmartAttendance.Web.Pages.Shared;

namespace SmartAttendance.Web.Pages.AttendanceRecords;

public class CreateModel : PageModel
{
    private readonly IAttendanceRecordService _attendanceRecordService;
    private readonly ApplicationDbContext _db;
    private readonly ICompanyScopeProvider _companyScope;

    public CreateModel(
        IAttendanceRecordService attendanceRecordService,
        ApplicationDbContext db,
        ICompanyScopeProvider companyScope)
    {
        _attendanceRecordService = attendanceRecordService;
        _db = db;
        _companyScope = companyScope;
    }

    [BindProperty]
    public AttendanceRecordCreateViewModel AttendanceRecord { get; set; } = new();

    /// <summary>رمز الموظف المحسوم واسمه — يبدأ بهما <c>_EmployeePicker</c> معبَّأً بعد إعادة العرض.</summary>
    public string? SelectedEmployeeCode { get; set; }

    public string? SelectedEmployeeName { get; set; }

    public IEnumerable<DeviceListViewModel> Devices { get; set; } = new List<DeviceListViewModel>();

    public IEnumerable<SelectListItem> Sources { get; set; } = new List<SelectListItem>();

    public IEnumerable<SelectListItem> Statuses { get; set; } = new List<SelectListItem>();

    public string? ErrorMessage { get; set; }

    public async Task OnGetAsync()
    {
        await LoadDropdownsAsync();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        await LoadDropdownsAsync();

        if (!ModelState.IsValid)
            return Page();

        // 🛡️ لا تُنشئ بصمةً لموظفٍ خارج شركاتك — المعرّف يأتي من النموذج فلا يُوثَق به.
        var scope = await _companyScope.GetAsync(HttpContext.RequestAborted);
        if (!await EmployeeCompanyGuard.CanAccessEmployeeAsync(_db, AttendanceRecord.EmployeeId, scope))
        {
            ErrorMessage = "الموظف غير موجود أو خارج نطاق صلاحيتك.";
            return Page();
        }

        var created = await _attendanceRecordService.CreateAsync(AttendanceRecord);

        if (!created)
        {
            ErrorMessage = "Invalid employee, device, or check-out time.";
            return Page();
        }

        TempData["SuccessMessage"] = "Attendance record created successfully.";

        return RedirectToPage("./Index");
    }

    private async Task LoadDropdownsAsync()
    {
        // المنتقي لا يحمّل أحداً — يُقرأ صفٌّ واحد فقط للموظف المحسوم إن وُجد.
        var identity = await EmployeePickerLookup.LoadAsync(_db, AttendanceRecord.EmployeeId);
        SelectedEmployeeCode = identity?.Code;
        SelectedEmployeeName = identity?.Name;

        Devices = await _attendanceRecordService.GetDevicesForDropdownAsync();

        Sources = Enum.GetValues<AttendanceSource>()
            .Select(x => new SelectListItem(x.ToString(), x.ToString()))
            .ToList();

        Statuses = Enum.GetValues<AttendanceStatus>()
            .Select(x => new SelectListItem(x.ToString(), x.ToString()))
            .ToList();
    }
}
