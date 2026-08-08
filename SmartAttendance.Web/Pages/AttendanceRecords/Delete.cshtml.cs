using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartAttendance.Application.AttendanceRecords.Services;
using SmartAttendance.Application.AttendanceRecords.ViewModels;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Security;

namespace SmartAttendance.Web.Pages.AttendanceRecords;

public class DeleteModel : PageModel
{
    private readonly IAttendanceRecordService _attendanceRecordService;
    private readonly ApplicationDbContext _db;
    private readonly ICompanyScopeProvider _companyScope;

    public DeleteModel(
        IAttendanceRecordService attendanceRecordService,
        ApplicationDbContext db,
        ICompanyScopeProvider companyScope)
    {
        _attendanceRecordService = attendanceRecordService;
        _db = db;
        _companyScope = companyScope;
    }

    /// <summary>
    /// «هل الصفّ ضمن شركاتي؟» — مغلق الفشل. حذفٌ/عرضٌ لسجلّ شركةٍ أخرى بمعرّفٍ من
    /// الرابط كان ممكناً قبل هذا الحارس.
    /// </summary>
    private async Task<bool> CanAccessAsync(int id) =>
        await EmployeeCompanyGuard.CanAccessOwnedRowAsync(
            _db, EmployeeCompanyGuard.Tables.AttendanceRecords, "Id", id,
            await _companyScope.GetAsync(HttpContext.RequestAborted));

    public AttendanceRecordDetailsViewModel AttendanceRecord { get; set; } = new();

    public string? ErrorMessage { get; set; }

    public async Task<IActionResult> OnGetAsync(int id)
    {
        var attendanceRecord = await _attendanceRecordService.GetByIdAsync(id);

        if (attendanceRecord == null)
            return NotFound();

        // 🛡️ لا تعرض سجلّ شركةٍ أخرى — NotFound لا Forbid كي لا يُستدلّ على وجوده.
        if (!await CanAccessAsync(id))
            return NotFound();

        AttendanceRecord = attendanceRecord;

        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int id)
    {
        // 🛡️ الحارس نفسه قبل الحذف — المعرّف من الرابط فلا يُوثَق به.
        if (!await CanAccessAsync(id))
            return NotFound();

        var deleted = await _attendanceRecordService.DeleteAsync(id);

        if (!deleted)
        {
            ErrorMessage = "Attendance record not found or could not be deleted.";

            var attendanceRecord = await _attendanceRecordService.GetByIdAsync(id);
            if (attendanceRecord != null)
                AttendanceRecord = attendanceRecord;

            return Page();
        }

        TempData["SuccessMessage"] = "Attendance record deleted successfully.";

        return RedirectToPage("./Index");
    }
}
